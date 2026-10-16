using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace PhoneCam;

public sealed class VirtualCamera : IDisposable
{
    [UnmanagedFunctionPointer(CallingConvention.StdCall,CharSet=CharSet.Unicode)]delegate int Start([MarshalAs(UnmanagedType.LPWStr)]string path,[MarshalAs(UnmanagedType.LPWStr)]string sid,out nint context);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]delegate int Write(nint context,byte[] pixels,int size);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]delegate void Stop(nint context);
    readonly AutoResetEvent changed=new(false);
    readonly object gate=new();
    BitmapSource? latest;long revision;volatile bool disposed;Thread? thread;
    public event Action<string>? Status;
    int initialized;
    public async Task Initialize(){
        if(Interlocked.Exchange(ref initialized,1)!=0 || disposed)return;
        try{
            if(!OperatingSystem.IsWindowsVersionAtLeast(10,0,22000)){Status?.Invoke("虚拟摄像头需要 Windows 11");return;}
            await CameraInstallation.EnsureInstalled();
            lock(gate){
                if(disposed)return;
                thread=new Thread(Run){IsBackground=true,Name="PCam virtual camera"};
                thread.SetApartmentState(ApartmentState.STA);thread.Start();
            }
        }catch(Exception e){Diagnostics.Write("virtual camera setup: "+e.GetType().Name);Status?.Invoke("虚拟摄像头未启动："+e.Message);}
    }
    public void Publish(BitmapSource? image){if(image!=null&&!image.IsFrozen)throw new ArgumentException("Frame must be frozen");lock(gate){if(disposed)return;latest=image;revision++;changed.Set();}}
    void Run(){
        nint dll=0,context=0;Stop? stop=null;
        try{
            dll=NativeLibrary.Load(CameraInstallation.InstalledPath);
            var start=Marshal.GetDelegateForFunctionPointer<Start>(NativeLibrary.GetExport(dll,"PcStart"));
            var write=Marshal.GetDelegateForFunctionPointer<Write>(NativeLibrary.GetExport(dll,"PcWrite"));
            stop=Marshal.GetDelegateForFunctionPointer<Stop>(NativeLibrary.GetExport(dll,"PcStop"));
            Marshal.ThrowExceptionForHR(start(CameraInstallation.FrameFile,CameraInstallation.Sid,out context));
            Status?.Invoke("虚拟摄像头已就绪 · 在其他软件选择 PCam");
            // Reuse the rendering target and pixel array; allocate neither at video frame rate.
            var target=new RenderTargetBitmap(1280,720,96,96,PixelFormats.Pbgra32);
            byte[] pixels=new byte[1280*720*4];long previous=-1;
            while(!disposed){
                changed.WaitOne(33);BitmapSource? image;long current;
                lock(gate){image=latest;current=revision;}if(current==previous)continue;previous=current;
                Array.Clear(pixels);
                if(image!=null){
                    var visual=new DrawingVisual();using(var dc=visual.RenderOpen()){
                        dc.DrawRectangle(Brushes.Black,null,new Rect(0,0,1280,720));
                        dc.DrawImage(image,FrameComposer.Fit(image.PixelWidth,image.PixelHeight,new Rect(0,0,1280,720)));
                    }
                    target.Clear();target.Render(visual);target.CopyPixels(pixels,1280*4,0);
                }
                Marshal.ThrowExceptionForHR(write(context,pixels,pixels.Length));
            }
        }catch(Exception e){Diagnostics.Write("virtual camera: "+e);Status?.Invoke("虚拟摄像头不可用："+e.Message);}
        finally{
            try{if(context!=0)stop?.Invoke(context);}finally{
                if(dll!=0)NativeLibrary.Free(dll);
                lock(gate){disposed=true;latest=null;changed.Dispose();}
            }
        }
    }
    public void Dispose(){
        Thread? worker;
        lock(gate){if(disposed)return;disposed=true;latest=null;changed.Set();worker=thread;if(worker==null)changed.Dispose();}
        // Do not free native state from this thread: the STA publisher owns its COM objects.
        worker?.Join(3000);
    }
}
