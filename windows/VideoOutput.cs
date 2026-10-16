// 会议出口只输出 JPEG；统计、按钮和设备名称不进入视频流。
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using System.IO;
using System.Text;
namespace PhoneCam;
public static class VideoOutput
{
    static readonly byte[] black = LoadBlack();
    static byte[] LoadBlack()
    {
        using var input = typeof(VideoOutput).Assembly.GetManifestResourceStream("PCam.Black.jpg")!;
        using var output = new MemoryStream(); input.CopyTo(output); return output.ToArray();
    }
    public static void Map(WebApplication web, Func<byte[]?> currentFrame)
    {
        web.MapGet("/", () => Results.Content("<!doctype html><html><head><meta charset='utf-8'></head><body style='margin:0;background:black;overflow:hidden'><img src='/stream' style='width:100vw;height:100vh;object-fit:contain'></body></html>", "text/html"));
        web.MapGet("/frame", (HttpContext context) => {
            context.Response.Headers.CacheControl = "no-store";
            var frame = currentFrame(); return frame == null ? Results.StatusCode(503) : Results.Bytes(frame, "image/jpeg");
        });
        web.MapGet("/stream", async (HttpContext context) => {
            context.Response.ContentType = "multipart/x-mixed-replace; boundary=pcamframe";
            context.Response.Headers.CacheControl = "no-store";
            byte[]? previous = null;
            try
            {
                while (!context.RequestAborted.IsCancellationRequested)
                {
                    var frame = currentFrame() ?? black;
                    if (!ReferenceEquals(frame, previous))
                    {
                        var header = Encoding.ASCII.GetBytes($"--pcamframe\r\nContent-Type: image/jpeg\r\nContent-Length: {frame.Length}\r\n\r\n");
                        await context.Response.Body.WriteAsync(header, context.RequestAborted);
                        await context.Response.Body.WriteAsync(frame, context.RequestAborted);
                        await context.Response.Body.WriteAsync(new byte[] { 13, 10 }, context.RequestAborted);
                        await context.Response.Body.FlushAsync(context.RequestAborted);
                        previous = frame;
                    }
                    // 有新帧立即送出；慢客户端等待写入时不建立帧队列。
                    await Task.Delay(10, context.RequestAborted);
                }
            }
            catch (Exception e) when (e is OperationCanceledException or IOException) { }
        });
    }
}
