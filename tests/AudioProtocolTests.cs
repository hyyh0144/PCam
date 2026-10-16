// 真实 TCP 验证音频授权、字节传输、静音与视频隔离。
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Text;
using PhoneCam;
static class AudioProtocolTests {
    static void Check(bool ok,string text){if(!ok)throw new Exception(text);}
    static async Task Until(Func<bool> f){for(int i=0;i<150;i++){if(f())return;await Task.Delay(20);}throw new Exception("Audio test timeout");}
    static async Task Auth(TcpClient c,Receiver r,string hello){
        await c.ConnectAsync("127.0.0.1",r.Port);var s=c.GetStream();await s.WriteAsync(Encoding.ASCII.GetBytes(r.Code));
        byte[] ack=new byte[1];await s.ReadExactlyAsync(ack);Check(ack[0]==1,"auth");
        byte[] bytes=Encoding.UTF8.GetBytes(hello),size=new byte[2];BinaryPrimitives.WriteUInt16BigEndian(size,(ushort)bytes.Length);
        await s.WriteAsync(size);await s.WriteAsync(bytes);
    }
    static async Task<string> Video(TcpClient c,Receiver r,string id){await Auth(c,r,id+"\nphone");byte[] grant=new byte[24];await c.GetStream().ReadExactlyAsync(grant);Check(Encoding.ASCII.GetString(grant,0,8)=="PCAMA001","grant magic");return Encoding.ASCII.GetString(grant,8,16);}
    static async Task Audio(TcpClient c,Receiver r,string id,string token,int expected=1){await Auth(c,r,id+"\n@audio:"+token);byte[] ack=new byte[1];await c.GetStream().ReadExactlyAsync(ack);Check(ack[0]==expected,"audio authorization");}
    static byte[] Packet(long time,int length=960){byte[] bytes=new byte[12+960];BinaryPrimitives.WriteInt32BigEndian(bytes,length);BinaryPrimitives.WriteInt64BigEndian(bytes.AsSpan(4),time);for(int i=12;i<bytes.Length;i++)bytes[i]=(byte)(i%251);return bytes;}
    static async Task Closed(TcpClient c){try{Check(await c.GetStream().ReadAsync(new byte[1]).AsTask().WaitAsync(TimeSpan.FromSeconds(3))==0,"audio should close");}catch(System.IO.IOException e) when(e.InnerException is SocketException se && se.SocketErrorCode==SocketError.ConnectionReset){}}
    public static async Task Fixture(string root) {
        Directory.CreateDirectory(root);using var a=new Receiver();using var b=new Receiver();var ar=a.Run();var br=b.Run();
        int audioA=0,audioB=0,videoA=0,videoB=0,invalid=0;
        byte[] expected=Enumerable.Range(0,960).Select(i=>(byte)(i%251)).ToArray();
        a.Audio+=(p,bytes)=>{if(!bytes.SequenceEqual(expected))Interlocked.Increment(ref invalid);Interlocked.Increment(ref audioA);};
        b.Audio+=(p,bytes)=>{if(!bytes.SequenceEqual(expected))Interlocked.Increment(ref invalid);Interlocked.Increment(ref audioB);};
        a.Frame+=(p,bytes)=>Interlocked.Increment(ref videoA);b.Frame+=(p,bytes)=>Interlocked.Increment(ref videoB);
        foreach(var entry in new[]{(a,"a"),(b,"b")}) File.WriteAllText(Path.Combine(root,entry.Item2+".txt"),Pairing.Payload(new[]{"127.0.0.1"},entry.Item1.Port,entry.Item1.Code,Guid.NewGuid().ToString(),entry.Item2),new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(root,"ready"),"ok");
        async Task Wait(Func<bool> f){for(int i=0;i<1000;i++){if(f())return;await Task.Delay(20);}throw new Exception("audio fixture timeout");}
        void Signal(string name)=>File.WriteAllText(Path.Combine(root,name),"ok");
        await Wait(()=>videoA>=10 && videoB>=10);Check(audioA==0 && audioB==0,"default silent");Signal("video-only");
        await Wait(()=>audioA>=10);Check(audioB==0,"A only routing");Signal("a-only");
        await Wait(()=>File.Exists(Path.Combine(root,"muted-a")));await Task.Delay(350);int count=audioA;Signal("a-muted");
        await Wait(()=>audioB>=10);Check(audioA==count && invalid==0,"mute isolation and PCM integrity");Check(videoA>20 && videoB>20,"video still flowing");Signal("b-only");Signal("desktop-muted");
        await Wait(()=>File.Exists(Path.Combine(root,"java-passed")));
        a.Dispose();b.Dispose();await ar;await br;
        Console.WriteLine("PASS: C# validated PCM contents and independent audio routing while video continued");
    }
    public static async Task Run(){
        using var receiver=new Receiver();var running=receiver.Run();string id=Guid.NewGuid().ToString();
        int frames=0,muted=0;var chunks=new ConcurrentQueue<byte[]>();
        receiver.Frame+=(p,b)=>Interlocked.Increment(ref frames);receiver.Audio+=(p,b)=>chunks.Enqueue(b);receiver.AudioState+=(p,on)=>{if(!on)Interlocked.Increment(ref muted);};
        using var video=new TcpClient();string token=await Video(video,receiver,id);
        await video.GetStream().WriteAsync(new byte[]{0,0,0,4,255,216,255,217});await Until(()=>frames==1);
        Check(chunks.IsEmpty,"video must default to no audio");
        using(var wrong=new TcpClient())await Audio(wrong,receiver,id,"0000000000000000",0);
        using(var wrongId=new TcpClient())await Audio(wrongId,receiver,Guid.NewGuid().ToString(),token,0);
        using(var audio=new TcpClient()){
            await Audio(audio,receiver,id,token);byte[] packet=Packet(1000);
            for(int i=0;i<packet.Length;i+=37)await audio.GetStream().WriteAsync(packet.AsMemory(i,Math.Min(37,packet.Length-i)));
            await Until(()=>chunks.Count==1);Check(chunks.TryPeek(out var pcm)&&pcm.SequenceEqual(packet[12..]),"PCM bytes exact");
            await audio.GetStream().WriteAsync(Packet(1000));await Closed(audio); // 重复时间戳只关闭音频。
        }
        await video.GetStream().WriteAsync(new byte[]{0,0,0,4,255,216,255,217});await Until(()=>frames==2);
        using(var invalid=new TcpClient()){await Audio(invalid,receiver,id,token);await invalid.GetStream().WriteAsync(Packet(2000,2_000_000));await Closed(invalid);}
        int before=muted;
        using(var audio=new TcpClient()){await Audio(audio,receiver,id,token);await audio.GetStream().WriteAsync(Packet(3000));await Until(()=>chunks.Count==2);}
        await Until(()=>muted>before);
        using(var last=new TcpClient()){
            await Audio(last,receiver,id,token);video.Dispose();await Closed(last);
        }
        using(var expired=new TcpClient())await Audio(expired,receiver,id,token,0);
        receiver.Dispose();await running;
        Console.WriteLine("PASS: audio defaults off, session token/device binding, fragmented PCM bytes, malformed audio isolation, mute and video-disconnect revocation");
    }
}
