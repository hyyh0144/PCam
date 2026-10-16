using System.Windows;
using System.Runtime.InteropServices;
namespace PhoneCam;
public partial class App : Application {
    [DllImport("shell32.dll",CharSet=CharSet.Unicode,PreserveSig=true)]
    static extern int SetCurrentProcessExplicitAppUserModelID(string appId);

    protected override void OnStartup(StartupEventArgs e){
        if(e.Args.Length==2 && e.Args[0]=="--virtual-camera-test"){
            ShutdownMode=ShutdownMode.OnExplicitShutdown;CameraSelfTest.Start(e.Args[1],this);return;
        }
        if(e.Args.Length>0 && e.Args[0]=="--install-camera"){
            
            try{if(e.Args.Length!=2)throw new ArgumentException("Missing user identity");CameraInstallation.Install(e.Args[1]);Shutdown(0);}
            catch(Exception error){MessageBox.Show("虚拟摄像头安装失败："+error.Message,"PCam");Shutdown(1);}
            return;
        }
        // Keep the taskbar identity stable across versioned EXE filenames and
        // .NET single-file extraction paths. Set it before creating any window.
        Marshal.ThrowExceptionForHR(SetCurrentProcessExplicitAppUserModelID("PCam.Desktop"));
        base.OnStartup(e);MainWindow=new MainWindow();MainWindow.Show();
    }
}
