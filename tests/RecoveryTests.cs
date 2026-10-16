// 真实 Java 发送器 / C# 接收器断线恢复，不复位配对码，验证无帧心跳与用户停止。
using PhoneCam;
using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
static class RecoveryTests {
    public static async Task Fixture(string root){
        Directory.CreateDirectory(root);using var server=new Receiver();var run=server.Run();
        int connections=0,frames=0,audio=0,stalls=0;string? firstId=null;bool changedIdentity=false;
        var online=new ConcurrentDictionary<Guid,Peer>();
        server.PeerChanged+=(p,on)=>{if(on){online[p.Id]=p;Interlocked.Increment(ref connections);if(firstId==null)firstId=p.DeviceId;else if(firstId!=p.DeviceId)changedIdentity=true;}else online.TryRemove(p.Id,out _);};
        server.Frame+=(p,b)=>Interlocked.Increment(ref frames);server.Audio+=(p,b)=>Interlocked.Increment(ref audio);server.FrameStalled+=p=>Interlocked.Increment(ref stalls);
        void Signal(string name)=>File.WriteAllText(Path.Combine(root,name),"ok");
        bool Exists(string name)=>File.Exists(Path.Combine(root,name));
        async Task Wait(Func<bool> f){for(int i=0;i<1500;i++){if(f())return;await Task.Delay(20);}throw new Exception("recovery fixture timeout");}
        File.WriteAllText(Path.Combine(root,"a.txt"),Pairing.Payload(new[]{"127.0.0.1"},server.Port,server.Code,Guid.NewGuid().ToString(),"恢复测试电脑"),new UTF8Encoding(false));Signal("ready");
        await Wait(()=>frames>=5 && audio>=5);int before=audio;
        var sockets=(ConcurrentDictionary<Guid,TcpClient>)typeof(Receiver).GetField("clients",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(server)!;
        foreach(var c in sockets.Values)c.Dispose(); // 模拟网络断开：不重置配对，也不更换监听端口。
        Signal("transport-dropped");await Wait(()=>connections>=2 && online.Count==1 && audio>before+5);Signal("reconnected");
        await Wait(()=>Exists("pause-video"));await Task.Delay(1000);int frameBefore=frames,connectionBefore=connections;
        await Task.Delay(11500);
        if(frames!=frameBefore || online.Count!=1 || connections!=connectionBefore || stalls==0)throw new Exception("no-frame heartbeat did not preserve session");
        Signal("pause-passed");await Wait(()=>frames>frameBefore+5);Signal("resume-passed");
        await Wait(()=>Exists("manual-stop"));await Wait(()=>online.IsEmpty);connectionBefore=connections;await Task.Delay(2500);
        if(connections!=connectionBefore)throw new Exception("manual disconnect reconnected");Signal("manual-passed");
        await Wait(()=>connections>connectionBefore);server.Reset();Signal("pairing-reset");
        await Wait(()=>Exists("java-passed"));if(changedIdentity)throw new Exception("device identity changed");
        server.Dispose();await run;
        Console.WriteLine("PASS: real transport/audio recovery, stable device identity, 12s camera pause with heartbeat, resume, explicit disconnect and pairing revocation");
    }
}
