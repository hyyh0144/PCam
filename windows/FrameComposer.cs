// 专用 STA 线程只合成最新快照；预览和 OBS 共用一次合成结果。
using System.Collections.Concurrent;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace PhoneCam;
public sealed record ComposedFrame(BitmapSource Image,byte[] Jpeg,IReadOnlyDictionary<Guid,Rect> Bounds);
public sealed class FrameComposer : IDisposable
{
    readonly BlockingCollection<Action> jobs=new(1);
    readonly Dictionary<Guid,(byte[] Bytes,int Rotation,BitmapSource Image)> cache=new();
    readonly Thread thread;
    int width=640,height=480;
    public FrameComposer(){thread=new Thread(()=>{foreach(var action in jobs.GetConsumingEnumerable())action();}){IsBackground=true,Name="PCam composition"};thread.SetApartmentState(ApartmentState.STA);thread.Start();}
    public Task<ComposedFrame> Compose(VideoLayer[] layers,Dictionary<Guid,byte[]> frames){
        var completion=new TaskCompletionSource<ComposedFrame>(TaskCreationOptions.RunContinuationsAsynchronously);
        // Never block the UI producer when shutting down or when a caller queues too much work.
        try{
            if(!jobs.TryAdd(()=>{try{completion.SetResult(Draw(layers,frames));}catch(Exception e){completion.SetException(e);}}))
                completion.SetException(new InvalidOperationException("合成队列已满"));
        }catch(InvalidOperationException e){completion.SetException(e);}
        return completion.Task;
    }
    BitmapSource? Get(VideoLayer layer,Dictionary<Guid,byte[]> frames){
        var id=layer.Id;
        if(!frames.TryGetValue(id,out var bytes))return null;
        if(cache.TryGetValue(id,out var old) && ReferenceEquals(old.Bytes,bytes) && old.Rotation==layer.Rotation)return old.Image;
        if(!JpegLimits.IsSafe(bytes))return null;
        try{using var stream=new MemoryStream(bytes);var image=new BitmapImage();image.BeginInit();image.CacheOption=BitmapCacheOption.OnLoad;image.StreamSource=stream;image.EndInit();image.Freeze();BitmapSource oriented=image;
            if(layer.Rotation!=0){oriented=new TransformedBitmap(image,new RotateTransform(layer.Rotation));oriented.Freeze();}
            cache[id]=(bytes,layer.Rotation,oriented);return oriented;}
        catch(NotSupportedException){return null;}catch(FileFormatException){return null;}
    }
    public static Rect Fit(double imageWidth,double imageHeight,Rect area){double scale=Math.Min(area.Width/imageWidth,area.Height/imageHeight);double w=imageWidth*scale,h=imageHeight*scale;return new(area.X+(area.Width-w)/2,area.Y+(area.Height-h)/2,w,h);}
    ComposedFrame Draw(VideoLayer[] layers,Dictionary<Guid,byte[]> frames){
        foreach(var id in cache.Keys.Where(id=>!frames.ContainsKey(id)).ToArray())cache.Remove(id);
        BitmapSource? main=layers.Length>0 && layers[^1].Online?Get(layers[^1],frames):null;
        if(main!=null){double scale=Math.Min(1,1280.0/Math.Max(main.PixelWidth,main.PixelHeight));width=Math.Max(1,(int)(main.PixelWidth*scale));height=Math.Max(1,(int)(main.PixelHeight*scale));}
        if(layers.Length==1 && main!=null && layers[0].Rotation==0)return new(main,frames[layers[0].Id],new Dictionary<Guid,Rect>());
        var bounds=new Dictionary<Guid,Rect>();var visual=new DrawingVisual();
        using(var dc=visual.RenderOpen()){
            dc.DrawRectangle(Brushes.Black,null,new Rect(0,0,width,height));
            for(int i=layers.Length-1;i>=0;i--){
                var layer=layers[i];if(!layer.Online)continue;var image=Get(layer,frames);if(image==null)continue;
                var b=layer.Box;var rect=Fit(image.PixelWidth,image.PixelHeight,new Rect(b.X*width,b.Y*height,b.Width*width,b.Height*height));
                dc.DrawImage(image,rect);if(i<layers.Length-1)bounds[layer.Id]=rect;
            }
        }
        var result=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);result.Render(visual);result.Freeze();
        var encoder=new JpegBitmapEncoder{QualityLevel=85};encoder.Frames.Add(BitmapFrame.Create(result));using var output=new MemoryStream();encoder.Save(output);
        return new(result,output.ToArray(),bounds);
    }
    public void Dispose(){if(!jobs.IsAddingCompleted)jobs.CompleteAdding();}
}
