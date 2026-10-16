// 仅向默认音频输出发送静音，验证实际设备初始化与切换/停止，不录制麦克风。
using NAudio.Wave;
using PhoneCam;
using System.Net.Sockets;
using System.Buffers.Binary;
using System.Text;
Console.WriteLine($"Output devices: {WaveOut.DeviceCount}; microphone devices: {WaveIn.DeviceCount}");
if(WaveOut.DeviceCount==0){Console.WriteLine("SKIP: no playback hardware");return;}
using var player=new AudioPlayback();string? error=null;player.Error+=message=>error=message;
var a=Guid.NewGuid();var b=Guid.NewGuid();byte[] silence=new byte[960];
player.Select(a);for(int i=0;i<10;i++){player.Add(a,silence);await Task.Delay(20);}
player.Select(b);player.Add(a,silence);player.Add(b,silence);await Task.Delay(80);
player.Muted(b);player.Select(null);player.Add(b,silence);
if(error!=null)throw new Exception(error);
Console.WriteLine("PASS: real output opened with silence, selection and mute/dispose completed");

// 实际接收器持续收音，电脑反复静音/恢复；手机随后独立关闭、重新开启。
using var receiver=new Receiver();var running=receiver.Run();
var device=Guid.NewGuid().ToString();Guid peerId=Guid.Empty;int received=0;
receiver.PeerChanged+=(peer,on)=>{if(on)peerId=peer.Id;};
receiver.Audio+=(peer,pcm)=>{player.Add(peer.Id,pcm);Interlocked.Increment(ref received);};
receiver.AudioState+=(peer,on)=>{if(!on)player.Muted(peer.Id);};
async Task<TcpClient> Connect(string hello){
    var c=new TcpClient();await c.ConnectAsync("127.0.0.1",receiver.Port);var stream=c.GetStream();
    await stream.WriteAsync(Encoding.ASCII.GetBytes(receiver.Code));byte[] ack=new byte[1];await stream.ReadExactlyAsync(ack);
    if(ack[0]!=1)throw new Exception("pairing rejected");
    byte[] text=Encoding.UTF8.GetBytes(device+"\n"+hello),header=new byte[2];BinaryPrimitives.WriteUInt16BigEndian(header,(ushort)text.Length);
    await stream.WriteAsync(header);await stream.WriteAsync(text);return c;
}
async Task Wait(Func<bool> condition){for(int i=0;i<150;i++){if(condition())return;await Task.Delay(20);}throw new Exception("independent sound timeout");}
using var phoneVideo=await Connect("Sound test");byte[] grant=new byte[24];await phoneVideo.GetStream().ReadExactlyAsync(grant);
string token=Encoding.ASCII.GetString(grant,8,16);await Wait(()=>peerId!=Guid.Empty);
long timestamp=0;
for(int phoneSession=0;phoneSession<2;phoneSession++){
    player.Select(null); // 电脑处于静音时，手机仍可成功开启麦克风连接。
    using var phoneAudio=await Connect("@audio:"+token);byte[] ack=new byte[1];await phoneAudio.GetStream().ReadExactlyAsync(ack);
    if(ack[0]!=1)throw new Exception("local mute rejected phone audio");
    foreach(bool muted in new[]{true,false,true,false}){
        player.Select(muted?null:peerId);int before=Volatile.Read(ref received);
        for(int i=0;i<5;i++){
            byte[] packet=new byte[972];BinaryPrimitives.WriteInt32BigEndian(packet,960);BinaryPrimitives.WriteInt64BigEndian(packet.AsSpan(4),++timestamp);
            await phoneAudio.GetStream().WriteAsync(packet);await Task.Delay(20);
        }
        await Wait(()=>Volatile.Read(ref received)==before+5);
    }
}
if(error!=null)throw new Exception(error);
player.Select(null);receiver.Dispose();await running;
Console.WriteLine("PASS: TCP audio continues through desktop mute/unmute; phone can stop/restart while desktop muted; only silence used");
