using PhoneCam;
using System.IO;
using System.Windows.Media.Imaging;
using System.Windows.Media;

static class AuditChecks
{
    static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
    static byte[] Header(int w,int h)=>new byte[]{255,216,255,192,0,11,8,(byte)(h>>8),(byte)h,(byte)(w>>8),(byte)w,1,1,0x11,0,255,217};
    public static void Run()
    {
        Check(JpegLimits.IsSafe(Header(1280,720)),"normal JPEG dimensions");
        Check(!JpegLimits.IsSafe(Header(65535,65535)),"oversized JPEG rejected before decode");
        Check(!JpegLimits.IsSafe(Header(8192,8192)),"pixel budget checked without integer overflow");
        Check(!JpegLimits.IsSafe(Header(0,720)),"zero width rejected");
        Check(!JpegLimits.IsSafe(new byte[]{255,216,255,192,255,255}),"truncated segment rejected");
        Check(!JpegLimits.IsSafe(new byte[]{255,216,255,218,0,2,255,217}),"missing dimensions rejected");
        var camera=new VirtualCamera();camera.Dispose();camera.Dispose();camera.Publish(null);
        camera.Initialize().GetAwaiter().GetResult(); // Disposed initialization must not install or create a worker.
        var composer=new FrameComposer();composer.Dispose();
        try{composer.Compose(Array.Empty<VideoLayer>(),new()).GetAwaiter().GetResult();throw new Exception("closed composer accepted work");}
        catch(InvalidOperationException){}
        foreach(string sid in new[]{"../bad","S-1-5-21-1-2-3-4/../target","S-1-5-18"})
        {
            try{CameraInstallation.FramesDirectory(sid);throw new Exception("unsafe SID accepted");}
            catch(ArgumentException){}
        }
        // Junction tests use only our test workspace and do not require elevation.
        string root=Path.Combine(Path.GetTempPath(),"PCam-audit-"+Guid.NewGuid());
        Directory.CreateDirectory(root);Directory.CreateDirectory(Path.Combine(root,"target"));
        try
        {
            var method=typeof(CameraInstallation).Assembly.GetType("PhoneCam.InstallDirectorySecurity")!.GetMethod("Apply")!;
            try{method.Invoke(null,new object[]{root,new System.Security.AccessControl.DirectorySecurity()});throw new Exception("untrusted directory owner accepted");}
            catch(System.Reflection.TargetInvocationException error) when(error.InnerException is IOException){}
            var start=new System.Diagnostics.ProcessStartInfo("powershell.exe"){UseShellExecute=false,CreateNoWindow=true};
            start.ArgumentList.Add("-NoProfile");start.ArgumentList.Add("-Command");
            start.ArgumentList.Add("New-Item -ItemType Junction -Path '"+root+"/link' -Target '"+root+"/target' | Out-Null");
            using var process=System.Diagnostics.Process.Start(start)!;process.WaitForExit();Check(process.ExitCode==0,"test junction creation");
            try{CameraInstallation.RejectReparsePoints(Path.Combine(root,"link","payload.dll"));throw new Exception("junction accepted");}
            catch(IOException){}
            Directory.Delete(Path.Combine(root,"target"));
            try{CameraInstallation.RejectReparsePoints(Path.Combine(root,"link","payload.dll"));throw new Exception("dangling junction accepted");}
            catch(IOException){}
            Directory.Delete(Path.Combine(root,"link"));
        }
        finally{if(Directory.Exists(Path.Combine(root,"target")))Directory.Delete(Path.Combine(root,"target"));Directory.Delete(root);}
        Console.WriteLine("PASS: JPEG decode bounds, disposed publisher/composer, unsafe identity and junction rejection");
    }
}
