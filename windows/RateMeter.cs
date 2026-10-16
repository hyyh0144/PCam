// 按实际经过时间计算接收码率和帧率，只供控制界面使用。
using System.Diagnostics;
namespace PhoneCam;
public sealed class RateMeter
{
    readonly object gate = new();
    long bytes, frames;
    double last;
    public static double Now => (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency;
    public RateMeter(double? start = null) => last = start ?? Now;
    public void Add(int byteCount) { lock (gate) { bytes += byteCount; frames++; } }
    public (double Mbps, double Fps) Sample(double now)
    {
        lock (gate)
        {
            if (now <= last) return (0, 0);
            double elapsed = now - last;
            var result = (bytes * 8.0 / elapsed / 1_000_000, frames / elapsed);
            bytes = frames = 0; last = now; return result;
        }
    }
}
