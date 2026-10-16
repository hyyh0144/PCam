// 真实 WPF 合成器的像素/输出测试，及层级、断线和恢复的状态回归。
using PhoneCam;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Reflection;
using System.Collections.Concurrent;
using System.Windows.Threading;
static class Program {
    static void Check(bool condition,string text){if(!condition)throw new Exception(text);}
    static byte[] Picture(byte r,byte g,byte b,int w=640,int h=480){
        byte[] pixels=new byte[w*h*3];for(int i=0;i<pixels.Length;i+=3){pixels[i]=b;pixels[i+1]=g;pixels[i+2]=r;}
        var source=BitmapSource.Create(w,h,96,96,PixelFormats.Bgr24,null,pixels,w*3);var encoder=new JpegBitmapEncoder{QualityLevel=95};encoder.Frames.Add(BitmapFrame.Create(source));using var stream=new MemoryStream();encoder.Save(stream);return stream.ToArray();
    }
    static byte[] Quadrants(){
        const int w=120,h=80;byte[] pixels=new byte[w*h*3];
        for(int y=0;y<h;y++)for(int x=0;x<w;x++){
            var color=y<h/2?(x<w/2?(255,0,0):(0,255,0)):(x<w/2?(0,0,255):(255,255,0));
            int i=(y*w+x)*3;pixels[i]=(byte)color.Item3;pixels[i+1]=(byte)color.Item2;pixels[i+2]=(byte)color.Item1;
        }
        var image=BitmapSource.Create(w,h,96,96,PixelFormats.Bgr24,null,pixels,w*3);var encoder=new JpegBitmapEncoder{QualityLevel=100};encoder.Frames.Add(BitmapFrame.Create(image));using var output=new MemoryStream();encoder.Save(output);return output.ToArray();
    }
    static void RotationChecks(){
        var id=Guid.NewGuid();var other=Guid.NewGuid();var model=new Composition();model.Connect(id);model.Connect(other);
        byte[] input=Quadrants();var frames=new Dictionary<Guid,byte[]>{{id,input},{other,input}};
        using var composer=new FrameComposer();
        var corners=new[]{(255,0,0),(0,0,255),(255,255,0),(0,255,0)};
        for(int step=0;step<4;step++){
            var result=composer.Compose(model.Snapshot(),frames).GetAwaiter().GetResult();
            Check(result.Image.PixelWidth==(step%2==0?120:80) && result.Image.PixelHeight==(step%2==0?80:120),"rotation swaps canvas dimensions");
            foreach(var image in new[]{result.Image,Jpeg(result.Jpeg)}){
                var c=Pixel(image,10,10);var expected=corners[step];
                Check(Math.Abs(c.R-expected.Item1)<25 && Math.Abs(c.G-expected.Item2)<25 && Math.Abs(c.B-expected.Item3)<25,"clockwise quadrant agrees in preview and OBS JPEG");
            }
            Check(ReferenceEquals(result.Jpeg,input)==(step==0),"only zero rotation uses JPEG passthrough");
            model.Rotate(id,90);Check(model.Rotation(other)==0,"other phone rotation remains independent");
        }
        Check(model.Rotation(id)==0,"four rotations return to zero");model.Rotate(id,-90);Check(model.Rotation(id)==270,"counterclockwise normalization");
        int revision=model.Revision;model.ResetRotation(id);Check(model.Rotation(id)==0 && model.Revision>revision,"reset forces recomposition without new frame");
        model.Rotate(other,90);model.Stack(other,id);
        var composed=composer.Compose(model.Snapshot(),frames).GetAwaiter().GetResult();var bounds=composed.Bounds[other];
        Check(Math.Abs(bounds.Width/bounds.Height-80.0/120)<.01,"rotated overlay preserves aspect ratio");
        var newId=Guid.NewGuid();model.Disconnect(other,true);model.Rebind(other,newId);Check(model.Rotation(newId)==90,"reconnect preserves per-phone rotation");
        model.Reset();Check(model.Rotation(newId)==0,"QR reset clears rotation");
        Console.WriteLine("PASS: independent four-direction rotation, quadrant pixels, preview/OBS agreement, unchanged-frame angle cache, rotated overlay, reconnect and reset");
    }
    static (int R,int G,int B) Pixel(BitmapSource source,int x,int y){var converted=new FormatConvertedBitmap(source,PixelFormats.Bgr32,null,0);byte[] pixel=new byte[4];converted.CopyPixels(new Int32Rect(x,y,1,1),pixel,4,0);return(pixel[2],pixel[1],pixel[0]);}
    static BitmapSource Jpeg(byte[] bytes){using var stream=new MemoryStream(bytes);return BitmapDecoder.Create(stream,BitmapCreateOptions.None,BitmapCacheOption.OnLoad).Frames[0];}
    [STAThread] static void Main(string[] args){
        AuditChecks.Run();
        RotationChecks();
        var a=Guid.NewGuid();var b=Guid.NewGuid();var c=Guid.NewGuid();var d=Guid.NewGuid();var e=Guid.NewGuid();
        var model=new Composition();foreach(var id in new[]{a,b,c,d,e})model.Connect(id);
        Check(model.Main==a && model.Order.Count==1,"first phone remains main; later phones not auto-stacked");
        Check(model.Promote(b) && model.Order.Count==1 && model.Main==b,"double-click alone must not add overlays");
        Check(model.Promote(a) && model.Order.Count==1,"repeated main switches never accumulate overlays");
        Check(model.Stack(a,b) && model.Main==b && model.Order[0]==a,"A onto B means B bottom");
        using var composer=new FrameComposer();var frames=new Dictionary<Guid,byte[]>{{a,Picture(255,0,0)},{b,Picture(0,0,255)},{c,Picture(0,255,0,480,640)},{d,Picture(255,255,0)}};
        ComposedFrame Draw()=>composer.Compose(model.Snapshot(),frames).GetAwaiter().GetResult();
        var result=Draw();var bg=Pixel(result.Image,30,30);Check(bg.B>240 && bg.R<10,"blue main pixels");
        var bound=result.Bounds[a];var foreground=Pixel(result.Image,(int)(bound.X+bound.Width/2),(int)(bound.Y+bound.Height/2));Check(foreground.R>240 && foreground.B<10,"red overlay pixels");
        var encoded=Pixel(Jpeg(result.Jpeg),(int)(bound.X+bound.Width/2),(int)(bound.Y+bound.Height/2));Check(encoded.R>235 && encoded.B<15,"OBS receives same composed image");
        Check(model.Stack(c,a) && model.Stack(d,c),"three overlays");var old=model.Order.ToArray();Check(!model.Stack(e,a) && old.SequenceEqual(model.Order),"fifth layer rejected atomically");
        model.Place(c,new(.1,.1,.4,.4));result=Draw();var portrait=result.Bounds[c];Check(Math.Abs(portrait.Width/portrait.Height-.75)<.01,"portrait aspect retained");
        model.Place(a,new(.1,.1,.4,.4));var top=Pixel(Draw().Image,140,140);Check(top.G>240 && top.R<10,"list top paints over lower overlay");
        Check(model.Promote(a) && model.Main==a && !model.Order.Contains(b),"double-click promotes overlay without auto-stacking old main");
        model.Disconnect(a);frames.Remove(a);Check(model.Main==a && model.MainOffline,"disconnection keeps offline bottom");result=Draw();bg=Pixel(result.Image,2,2);Check(bg.R==0 && bg.G==0 && bg.B==0 && result.Bounds.Count==2,"black base preserves manually stacked overlays");
        Check(model.Promote(c) && model.Main==c && !model.Order.Contains(a),"double-click restores an online main");
        model.Disconnect(d);frames.Remove(d);Check(!model.Order.Contains(d),"disconnected overlay removed");
        model.Remove(b);Check(model.Order.Count==1,"remove overlay retains connection");result=Draw();Check(ReferenceEquals(result.Jpeg,frames[c]),"single source bypasses re-encoding");
        model.Disconnect(c);frames.Remove(c);model.Connect(e);Check(model.Main==c && model.MainOffline,"new connection cannot replace offline main");Check(model.Stack(e,b) && model.Main==b && !model.Order.Contains(c),"drag onto unused online phone replaces offline slot");
        model.Place(e,new(-5,3,2,2));var box=model.Snapshot()[0].Box;Check(box.X>=0 && box.Y>=0 && box.X+box.Width<=1 && box.Y+box.Height<=1,"drag bounds clamped");
        model.Reset();foreach(var id in new[]{a,b,c,d,e})model.Connect(id);model.Stack(b,a);model.Stack(c,b);model.Stack(d,c);Check(model.Promote(e) && model.Main==e && model.Order.Count==4 && !model.Order.Contains(a) && model.IsOnline(a),"switch main retains existing overlays without stacking previous main");
        model.Reset();Check(model.Main==null && model.Order.Count==0,"reset clears composition");
        // 重连保留小画面位置，用户另选主画面后不抢回底层。
        model.Connect(a);model.Connect(b);model.Stack(b,a);model.Place(b,new(.12,.23,.3,.4));
        var saved=model.Snapshot()[0].Box;model.Disconnect(b,true);model.Rebind(b,c);
        Check(model.Order[0]==c && model.Snapshot()[0].Box==saved && model.IsOnline(c),"overlay reconnect retains position and order");
        model.Disconnect(a,true);model.Rebind(a,d);Check(model.Main==d && !model.MainOffline,"main reconnect restores same bottom");
        model.Disconnect(d,true);model.Promote(c);model.Rebind(d,e);
        Check(model.Main==c && !model.Order.Contains(e),"reconnected old main cannot override user selection");model.Reset();
        // 留下可检查的测试合成图；蓝色底图和红色小画面，不含编辑边框或文字。
        model.Connect(b);model.Connect(a);model.Stack(a,b);frames[a]=Picture(255,0,0);result=Draw();
        if(args.Length>0){Directory.CreateDirectory(args[0]);File.WriteAllBytes(Path.Combine(args[0],"composition.jpg"),result.Jpeg);WindowVisual(args[0],a,b,frames);}
        Console.WriteLine("PASS: layer ordering, 4-layer limit, promotion, disconnect retention, recovery, aspect/position bounds, real preview/JPEG pixels, single-source fast path");
    }
    static void WindowVisual(string folder,Guid a,Guid b,Dictionary<Guid,byte[]> input){
        var app=new Application();System.Threading.SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());var window=new MainWindow();
        T Field<T>(string name)=>(T)typeof(MainWindow).GetField(name,BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(window)!;
        void Call(string name)=>typeof(MainWindow).GetMethod(name,BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.DeclaredOnly)!.Invoke(window,null);
        var model=Field<Composition>("composition");var known=Field<Dictionary<Guid,Peer>>("known");var frames=Field<ConcurrentDictionary<Guid,byte[]>>("frames");
        foreach(var id in new[]{a,b}){known[id]=new Peer(id,id.ToString(),id==a?"手机 A":"手机 B");frames[id]=input[id];model.Connect(id);}model.Stack(a,b);
        Call("SyncDevices");Call("UpdateCompositionHint");Call("Render");
        var root=(FrameworkElement)window.Content;((System.Windows.Controls.Panel)root).Background=window.Background;root.Measure(new Size(900,700));root.Arrange(new Rect(0,0,900,700));root.UpdateLayout();
        void Pump(){var frame=new DispatcherFrame();var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(200)};timer.Tick+=(_,_)=>{timer.Stop();frame.Continue=false;};timer.Start();Dispatcher.PushFrame(frame);}
        Pump();var image=(System.Windows.Controls.Image)window.FindName("Preview");Check(image.Source!=null,"window renders composition");
        var list=(System.Windows.Controls.ListBox)window.FindName("Devices");var before=image.Source;list.SelectedIndex=list.Items.Count-1;Check(ReferenceEquals(before,image.Source),"single click cannot switch video");
        ((System.Windows.Controls.Button)window.FindName("RotatePicture")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));Pump();
        Check(model.Rotation(b)==90 && ((BitmapSource)image.Source!).PixelWidth==480 && ((BitmapSource)image.Source!).PixelHeight==640,"actual rotation button recomposes latest frame without new input");
        var output=Jpeg(Field<byte[]>("composedJpeg"));Check(output.PixelWidth==480 && output.PixelHeight==640,"actual OBS output follows PC rotation button");
        ((System.Windows.Controls.Button)window.FindName("RotationReset")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));Pump();
        Check(model.Rotation(b)==0 && ((BitmapSource)image.Source!).PixelWidth==640,"actual reset restores received direction");
        Console.WriteLine("PASS: real PC rotation/reset controls update preview and OBS without receiving a new frame");
        // 走实际列表 PreviewDrop 路由，验证字符串负载和落点识别，而非只测试布局模型。
        model.Remove(a);Call("SyncDevices");root.UpdateLayout();
        var target=list.ItemContainerGenerator.ContainerFromIndex(list.Items.Count-1) as System.Windows.Controls.ListBoxItem;
        Check(target!=null,"drop target exists");
        var point=target!.TranslatePoint(new Point(12,target.ActualHeight*.85),list);
        var constructor=typeof(DragEventArgs).GetConstructors(BindingFlags.NonPublic|BindingFlags.Instance).Single(c=>c.GetParameters().Length==5);
        var drop=(DragEventArgs)constructor.Invoke(new object[]{new DataObject("PCam.PhoneLayer",a.ToString()),DragDropKeyStates.LeftMouseButton,DragDropEffects.Move,list,point});
        drop.RoutedEvent=DragDrop.PreviewDropEvent;list.RaiseEvent(drop);
        Check(model.Main==b && model.Order.Count==2 && model.Order[0]==a,"actual list drop on lower row stacks rather than promotes");Pump();
        Console.WriteLine("PASS: actual WPF PreviewDrop route with text device ID and bottom-of-row target");
        void Save(string file){root.UpdateLayout();var bitmap=new RenderTargetBitmap(900,700,96,96,PixelFormats.Pbgra32);bitmap.Render(root);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var stream=File.Create(Path.Combine(folder,file));encoder.Save(stream);}
        list.SelectedIndex=0;Save("window.png");model.Disconnect(b);frames.TryRemove(b,out _);Call("SyncDevices");Call("UpdateCompositionHint");Call("Render");Pump();Save("offline.png");
        Check(((System.Windows.Controls.TextBlock)window.FindName("CompositionHint")).Visibility==Visibility.Visible,"offline notice local UI");
        Check(model.Promote(a),"recover");Call("SyncDevices");Check(list.Items.Count==1,"offline placeholder removed after promotion");
        window.Close();app.Shutdown();Console.WriteLine("PASS: actual window rendering, single-click isolation, offline notice and stale-row cleanup");
    }
}
