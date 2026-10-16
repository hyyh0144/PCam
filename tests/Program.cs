// 随机端口、多手机并发与错误隔离；--fixture 用于跨语言测试。
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Text;
using PhoneCam;
using var server = new Receiver();
var run = server.Run();
if(args.Length==2 && args[0]=="--recovery-fixture"){await RecoveryTests.Fixture(args[1]);server.Dispose();await run;return;}
if(args.Length==2 && args[0]=="--audio-fixture") {await AudioProtocolTests.Fixture(args[1]);server.Dispose();await run;return;}
if (args.Length == 2 && args[0] == "--fixture")
{
    string root = args[1]; Directory.CreateDirectory(root);
    using var other = new Receiver(); var otherRun = other.Run();
    var seenA = new ConcurrentDictionary<string, int>(); var seenB = new ConcurrentDictionary<string, int>();
    server.Frame += (p, f) => seenA.AddOrUpdate(p.DeviceId, 1, (_, n) => n + 1);
    other.Frame += (p, f) => seenB.AddOrUpdate(p.DeviceId, 1, (_, n) => n + 1);
    foreach (var item in new[] { (server, "a", "测试电脑 A"), (other, "b", "测试电脑 B") })
    {
        string text = Pairing.Payload(new[] { "127.0.0.1" }, item.Item1.Port, item.Item1.Code, Guid.NewGuid().ToString(), item.Item3);
        File.WriteAllBytes(Path.Combine(root, item.Item2 + ".png"), Pairing.Qr(text));
        File.WriteAllText(Path.Combine(root, item.Item2 + ".txt"), text, new UTF8Encoding(false));
    }
    File.WriteAllText(Path.Combine(root, "ready"), "ready");
    await Until(() => seenA.Count == 2 && seenB.Count == 1 && seenA.Values.All(n => n >= 3) && seenB.Values.All(n => n >= 3), 20000);
    File.WriteAllText(Path.Combine(root, "both-passed"), "ok");
    int before = seenB.Values.Sum();
    await Until(() => File.Exists(Path.Combine(root, "java-passed")), 15000);
    if (seenB.Values.Sum() <= before) throw new Exception("其他端断开后 B 没有继续收帧");
    other.Dispose(); await otherRun; server.Dispose(); await run;
    Console.WriteLine("PASS：C#实际收到两个手机身份、多路帧和单路断开后的持续帧");
    return;
}
await VideoOutputTests.Run();
await AudioProtocolTests.Run();
using var second = new Receiver();
var secondRun = second.Run();
if (server.Port == 0 || server.Port == second.Port) throw new Exception("随机端口分配失败");
var counts = new ConcurrentDictionary<string, int>();
var active = new ConcurrentDictionary<Guid, Peer>();
server.Frame += (p, f) => counts.AddOrUpdate(p.Name, 1, (_, n) => n + 1);
server.PeerChanged += (p, connected) => { if (connected) active[p.Id] = p; else active.TryRemove(p.Id, out _); };
async Task<TcpClient> Connect(string code, byte expected, string name)
{
    var c = new TcpClient(); await c.ConnectAsync("127.0.0.1", server.Port);
    await c.GetStream().WriteAsync(Encoding.ASCII.GetBytes(code));
    var response = new byte[1]; await c.GetStream().ReadExactlyAsync(response).AsTask().WaitAsync(TimeSpan.FromSeconds(3));
    if (response[0] != expected) throw new Exception("配对响应错误");
    if (expected == 1) {
        byte[] hello = Encoding.UTF8.GetBytes(Guid.NewGuid() + "\n" + name);
        byte[] size = new byte[2]; BinaryPrimitives.WriteUInt16BigEndian(size, (ushort)hello.Length);
        await c.GetStream().WriteAsync(size); await c.GetStream().WriteAsync(hello);
        await c.GetStream().ReadExactlyAsync(new byte[24]);
    }
    return c;
}
byte[] packet = { 0,0,0,5,255,216,0,255,217 };
using var stalled = new TcpClient(); await stalled.ConnectAsync("127.0.0.1", server.Port); // 不发送配对，不能阻塞其他端。
using (var bad = await Connect("ZZZZZZZZZZZZZZZZ", 0, "")) { }
using var a = await Connect(server.Code, 1, "手机 A");
using var b = await Connect(server.Code, 1, "手机 B");
foreach (byte value in packet) await a.GetStream().WriteAsync(new byte[] { value });
await b.GetStream().WriteAsync(packet);
await Until(() => counts.GetValueOrDefault("手机 A") == 1 && counts.GetValueOrDefault("手机 B") == 1);
await a.GetStream().WriteAsync(new byte[] { 127,255,255,255 });
await Closed(a);
await b.GetStream().WriteAsync(packet);
await Until(() => counts.GetValueOrDefault("手机 B") == 2);
using var truncated = await Connect(server.Code, 1, "半帧");
await truncated.GetStream().WriteAsync(new byte[] { 0,0,0,100,255,216 }); truncated.Dispose();
await Until(() => active.Count == 1);
string old = server.Code; server.Reset(); await Closed(b);
if (server.Code == old) throw new Exception("重置配对码失败");
using (var expired = await Connect(old, 0, "")) { }
await Until(() => active.IsEmpty);
server.Dispose(); await run; second.Dispose(); await secondRun;
Console.WriteLine("PASS：随机端口、多手机并发、慢握手隔离、TCP分片、非法长度隔离、半帧断开、重置全部断连、旧二维码失效");

static async Task Closed(TcpClient c) { if (await c.GetStream().ReadAsync(new byte[1]).AsTask().WaitAsync(TimeSpan.FromSeconds(3)) != 0) throw new Exception("连接未关闭"); }
static async Task Until(Func<bool> predicate, int ms = 4000) {
    var deadline = DateTime.UtcNow.AddMilliseconds(ms);
    while (!predicate()) { if (DateTime.UtcNow > deadline) throw new Exception("等待条件超时"); await Task.Delay(20); }
}


