using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
namespace PhoneCam;

// Keep the verified file open without write/delete sharing until the native module is unloaded.
// This detects corruption and prevents ordinary file replacement during loading, not same-user malware.
sealed class RecorderComponent : IDisposable
{
    static readonly Lazy<byte[]> payload=new(()=>{using var input=typeof(RecorderComponent).Assembly.GetManifestResourceStream("PCam.Recorder.dll")??throw new IOException("缺少录制组件");using var output=new MemoryStream();input.CopyTo(output);return output.ToArray();});
    static readonly Lazy<byte[]> hash=new(()=>SHA256.HashData(payload.Value));
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern nint LoadLibraryEx(string path,nint file,uint flags);
    readonly FileStream lease;
    public nint Library {get;}
    RecorderComponent(FileStream file,nint library){lease=file;Library=library;}
    public static RecorderComponent Load(){
        string folder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"PCam","Recorder",Convert.ToHexString(hash.Value));
        CameraInstallation.RejectReparsePoints(folder);Directory.CreateDirectory(folder);string path=Path.Combine(folder,"PCamRecorder.dll");
        if(!File.Exists(path)){
            // Publish a complete DLL atomically: another process must never hash a half-written extraction.
            string temp=Path.Combine(folder,Guid.NewGuid().ToString("N")+".tmp");
            try{using(var file=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None))file.Write(payload.Value);try{File.Move(temp,path);}catch(IOException) when(File.Exists(path)){} }
            finally{if(File.Exists(temp))File.Delete(temp);}
        }
        CameraInstallation.RejectReparsePoints(path);
        var lease=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);
        try{
            if(!SHA256.HashData(lease).AsSpan().SequenceEqual(hash.Value))throw new IOException("录制组件校验失败");
            // Dependencies come from the DLL folder or System32, never the current working directory.
            nint library=LoadLibraryEx(path,0,0x00000100|0x00000800);
            if(library==0)throw new Win32Exception(Marshal.GetLastWin32Error());
            return new(lease,library);
        }catch{lease.Dispose();throw;}
    }
    public void Dispose(){try{NativeLibrary.Free(Library);}finally{lease.Dispose();}}
}
