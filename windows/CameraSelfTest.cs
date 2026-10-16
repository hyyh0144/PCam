using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
namespace PhoneCam;
// Generates test content in the real camera publisher. A separate process
// reads it through Windows camera enumeration / Media Foundation capture.
public static class CameraSelfTest
{
    public static async void Start(string folder,Application app){
        Directory.CreateDirectory(folder);var camera=new VirtualCamera();
        byte[] pixels=new byte[640*480*4];
        for(int y=0;y<480;y++)for(int x=0;x<640;x++){
            int i=(y*640+x)*4;bool left=x<320,top=y<240;
            pixels[i]=(byte)(!top&&left?255:0);pixels[i+1]=(byte)(!left?255:0);pixels[i+2]=(byte)(top&&left||!top&&!left?255:0);pixels[i+3]=255;
        }
        var image=BitmapSource.Create(640,480,96,96,PixelFormats.Bgra32,null,pixels,640*4);image.Freeze();
        camera.Status+=message=>{
            File.AppendAllText(Path.Combine(folder,"publisher.log"),message+Environment.NewLine);
            if(message.Contains("已就绪"))File.WriteAllText(Path.Combine(folder,"ready"),"camera started");
        };
        var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(100)};
        timer.Tick+=(_,_)=>{
            if(File.Exists(Path.Combine(folder,"stop"))){timer.Stop();camera.Dispose();app.Shutdown();return;}
            camera.Publish(image);
        };
        timer.Start();await camera.Initialize();
    }
}
