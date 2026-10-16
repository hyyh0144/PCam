// 电脑静音不干预手机发送；视频和 PCM 音频独立连接；音频令牌绑定视频会话，断视频即撤销。
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
namespace PhoneCam;
public sealed record Peer(Guid Id,string DeviceId,string Name);
public sealed class Receiver : IDisposable
{
    public const int MaxFrame=2*1024*1024,MaxPeers=8,AudioBytes=960;
    readonly TcpListener listener;
    readonly CancellationTokenSource stop=new();
    readonly ConcurrentDictionary<Guid,TcpClient> clients=new();
    readonly ConcurrentDictionary<string,Session> sessions=new();
    readonly ConcurrentDictionary<string,Session> devices=new();
    readonly object deviceGate=new();
    readonly SemaphoreSlim videoSlots=new(MaxPeers);
    sealed class Session(Peer peer,TcpClient video) { public readonly Peer Peer=peer; public readonly TcpClient Video=video; public readonly object Gate=new(); public bool Active=true; public TcpClient? Audio; }
    string code=NewCode();
    public string Code=>Volatile.Read(ref code);
    public int Port=>((IPEndPoint)listener.LocalEndpoint).Port;
    public event Action<Peer,byte[]>? Frame;
    public event Action<Peer,bool>? PeerChanged;
    public event Action<Peer,byte[]>? Audio;
    public event Action<Peer,bool>? AudioState;
    public event Action<Peer>? FrameStalled;
    public event Action<string>? Diagnostic;
    public Receiver(int port=0)=>listener=new(IPAddress.Any,port);
    static string NewCode()=>Convert.ToHexString(RandomNumberGenerator.GetBytes(8));
    public void Reset(){Volatile.Write(ref code,NewCode());foreach(var c in clients.Values)c.Dispose();}
    public async Task Run() {
        listener.Start();
        while(!stop.IsCancellationRequested) {
            TcpClient client;
            try {client=await listener.AcceptTcpClientAsync(stop.Token).ConfigureAwait(false);}
            catch(OperationCanceledException){break;}
            catch(SocketException) when(stop.IsCancellationRequested){break;}
            if(clients.Count>=MaxPeers*3){client.Dispose();continue;}
            var id=Guid.NewGuid();clients[id]=client;_=Handle(id,client);
        }
    }
    async Task Handle(Guid id,TcpClient client) {
        Peer? peer=null;Session? session=null;string? token=null;bool videoSlot=false,heartbeat=false;
        try {
            client.NoDelay=true;using var stream=client.GetStream();string expected=Code;
            string[] parts;
            using(var timeout=CancellationTokenSource.CreateLinkedTokenSource(stop.Token)) {
                timeout.CancelAfter(TimeSpan.FromSeconds(5));byte[] auth=new byte[16];await stream.ReadExactlyAsync(auth,timeout.Token);
                bool valid=expected==Code && CryptographicOperations.FixedTimeEquals(auth,Encoding.ASCII.GetBytes(expected));
                await stream.WriteAsync(new byte[]{valid?(byte)1:(byte)0},timeout.Token);if(!valid)return;
                byte[] size=new byte[2];await stream.ReadExactlyAsync(size,timeout.Token);int length=BinaryPrimitives.ReadUInt16BigEndian(size);
                if(length<38 || length>512)throw new InvalidDataException();
                byte[] hello=new byte[length];await stream.ReadExactlyAsync(hello,timeout.Token);
                parts=new UTF8Encoding(false,true).GetString(hello).Split('\n',2);
                if(parts.Length!=2 || !Guid.TryParse(parts[0],out _) || expected!=Code)return;
                if(!parts[1].StartsWith("@audio:",StringComparison.Ordinal)) {
                    if(!videoSlots.Wait(0))return;videoSlot=true;
                    heartbeat=parts[1].StartsWith("@video2:",StringComparison.Ordinal);
                    string rawName=heartbeat?parts[1][8..]:parts[1];
                    string name=new(rawName.Where(c=>!char.IsControl(c)).Take(80).ToArray());
                    peer=new Peer(id,parts[0],string.IsNullOrWhiteSpace(name)?"Android 手机":name.Trim());
                    token=NewCode();session=new Session(peer,client);sessions[token]=session;
                    lock(deviceGate){if(devices.TryGetValue(peer.DeviceId,out var previous))previous.Video.Dispose();devices[peer.DeviceId]=session;}
                    await stream.WriteAsync(Encoding.ASCII.GetBytes((heartbeat?"PCAMA002":"PCAMA001")+token),timeout.Token);
                }
            }
            if(peer==null) {await ReadAudio(parts[0],parts[1][7..],expected,client,stream);return;}
            PeerChanged?.Invoke(peer,true);
            Diagnostic?.Invoke("video connected; heartbeat="+heartbeat);
            long lastFrame=Environment.TickCount64;bool stalled=false;
            byte[] header=new byte[4];
            while(!stop.IsCancellationRequested) {
                using var timeout=CancellationTokenSource.CreateLinkedTokenSource(stop.Token);timeout.CancelAfter(TimeSpan.FromSeconds(10));
                await stream.ReadExactlyAsync(header,timeout.Token);int length=BinaryPrimitives.ReadInt32BigEndian(header);
                if(length==0 && heartbeat){
                    if(!stalled && Environment.TickCount64-lastFrame>=5000){stalled=true;FrameStalled?.Invoke(peer);Diagnostic?.Invoke("camera frames paused; heartbeat alive");}
                    continue;
                }
                if(length<4 || length>MaxFrame)throw new InvalidDataException();
                byte[] jpeg=new byte[length];await stream.ReadExactlyAsync(jpeg,timeout.Token);
                if(jpeg[0]!=255 || jpeg[1]!=216 || jpeg[^2]!=255 || jpeg[^1]!=217)throw new InvalidDataException();
                Frame?.Invoke(peer,jpeg);
                if(stalled)Diagnostic?.Invoke("camera frames resumed");stalled=false;lastFrame=Environment.TickCount64;
            }
        } catch(Exception e) when(e is IOException or SocketException or OperationCanceledException or InvalidDataException or ObjectDisposedException or DecoderFallbackException){Diagnostic?.Invoke("connection ended: "+e.GetType().Name);}
        finally {
            if(session!=null)lock(session.Gate){session.Active=false;session.Audio?.Dispose();session.Audio=null;AudioState?.Invoke(session.Peer,false);}
            if(token!=null)sessions.TryRemove(token,out _);
            if(session!=null)lock(deviceGate){if(devices.TryGetValue(session.Peer.DeviceId,out var current) && current==session)devices.TryRemove(session.Peer.DeviceId,out _);}
            client.Dispose();clients.TryRemove(id,out _);
            if(peer!=null)PeerChanged?.Invoke(peer,false);
            if(videoSlot)videoSlots.Release();
        }
    }
    async Task ReadAudio(string deviceId,string token,string expected,TcpClient client,NetworkStream stream) {
        if(!sessions.TryGetValue(token,out var session) || session.Peer.DeviceId!=deviceId || expected!=Code) {await stream.WriteAsync(new byte[]{0},stop.Token);return;}
        bool accepted;
        lock(session.Gate) {
            accepted=session.Active;
            if(accepted){session.Audio?.Dispose();session.Audio=client;AudioState?.Invoke(session.Peer,false);}
        }
        if(!accepted){await stream.WriteAsync(new byte[]{(byte)0},stop.Token);return;}
        try {
            await stream.WriteAsync(new byte[]{1},stop.Token);long previous=-1;
            byte[] header=new byte[12];
            while(!stop.IsCancellationRequested) {
                using var timeout=CancellationTokenSource.CreateLinkedTokenSource(stop.Token);timeout.CancelAfter(TimeSpan.FromSeconds(3));
                await stream.ReadExactlyAsync(header,timeout.Token);
                int length=BinaryPrimitives.ReadInt32BigEndian(header);long time=BinaryPrimitives.ReadInt64BigEndian(header.AsSpan(4));
                if(length!=AudioBytes || time<0 || time<=previous)throw new InvalidDataException("音频格式或时间戳非法");previous=time;
                byte[] pcm=new byte[length];await stream.ReadExactlyAsync(pcm,timeout.Token);
                lock(session.Gate) {
                    if(!session.Active || session.Audio!=client || expected!=Code)return;
                    Audio?.Invoke(session.Peer,pcm);
                }
            }
        } finally {
            lock(session.Gate)if(session.Audio==client){session.Audio=null;AudioState?.Invoke(session.Peer,false);}
        }
    }
    public void Dispose(){stop.Cancel();listener.Stop();foreach(var c in clients.Values)c.Dispose();}
}
