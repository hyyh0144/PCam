// 实际 HTTP 视频流逐字节验证；统计不会被叠加到 JPEG 或页面。
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using PhoneCam;
using System.Net.Http;
using System.Text;
public static class VideoOutputTests
{
    public static async Task Run()
    {
        var meter = new RateMeter(0); meter.Add(250_000); meter.Add(250_000);
        var sample = meter.Sample(2);
        if (sample.Mbps != 2 || sample.Fps != 1 || meter.Sample(3) != (0, 0)) throw new Exception("码率统计错误");
        using var resource = typeof(VideoOutputTests).Assembly.GetManifestResourceStream("PCam.Black.jpg")!;
        using var memory = new MemoryStream(); resource.CopyTo(memory); byte[] original = memory.ToArray();
        byte[]? latest = original;
        var builder = WebApplication.CreateBuilder(); builder.Logging.ClearProviders(); builder.WebHost.UseUrls("http://127.0.0.1:0");
        await using var app = builder.Build(); VideoOutput.Map(app, () => Volatile.Read(ref latest)); await app.StartAsync();
        try
        {
            using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
            string page = await client.GetStringAsync("/");
            if (!page.Contains("src='/stream'") || page.Contains("Mbps") || page.Contains("fps") || page.Contains("Stats")) throw new Exception("会议页面混入统计");
            if (!(await client.GetByteArrayAsync("/frame")).SequenceEqual(original)) throw new Exception("静态帧改变了图像");
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var response = await client.GetAsync("/stream", HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (response.Content.Headers.ContentType?.MediaType != "multipart/x-mixed-replace") throw new Exception("视频流 MIME 错误");
            using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            if (!(await ReadPart(stream, timeout.Token)).SequenceEqual(original)) throw new Exception("视频帧被修改或叠加文字");
            Volatile.Write(ref latest, null);
            var blank = await ReadPart(stream, timeout.Token);
            if (!blank.SequenceEqual(original)) throw new Exception("断开后未输出黑画面");
            for (int i = 0; i < 5; i++) {
                Volatile.Write(ref latest, original.ToArray());
                if (!(await ReadPart(stream, timeout.Token)).SequenceEqual(original)) throw new Exception("连续帧被修改");
            }
            Console.WriteLine("PASS：码率按实际时间统计、MJPEG连续帧、断开清屏、会议页面/原始图像无统计叠加");
        }
        finally { await app.StopAsync(); }
    }
    static async Task<byte[]> ReadPart(Stream stream, CancellationToken token)
    {
        var header = new List<byte>(); var one = new byte[1];
        while (true) {
            await stream.ReadExactlyAsync(one, token); header.Add(one[0]);
            if (header.Count > 1024) throw new Exception("流头过长");
            int n = header.Count;
            if (n >= 4 && header[n - 4] == 13 && header[n - 3] == 10 && header[n - 2] == 13 && header[n - 1] == 10) break;
        }
        string text = Encoding.ASCII.GetString(header.ToArray());
        int length = int.Parse(text.Split("Content-Length: ")[1].Split('\r')[0]);
        var bytes = new byte[length]; await stream.ReadExactlyAsync(bytes, token); return bytes;
    }
}
