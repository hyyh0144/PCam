using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
namespace PhoneCam;
public static class RecordingSettings
{
    [DllImport("shell32.dll")]static extern int SHGetKnownFolderPath(in Guid folder,uint flags,nint token,out nint path);
    static readonly string settings=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"PCam","recording.json");
    public static string Downloads(){
        var id=new Guid("374DE290-123F-4565-9164-39C4925E467B");
        Marshal.ThrowExceptionForHR(SHGetKnownFolderPath(in id,0,0,out var path));
        try{return Marshal.PtrToStringUni(path)!;}finally{Marshal.FreeCoTaskMem(path);}
    }
    static bool IsFolder(string? path){
        if(string.IsNullOrWhiteSpace(path)||path.IndexOfAny(Path.GetInvalidPathChars())>=0)return false;
        try{return Path.IsPathFullyQualified(path)&&Path.GetFullPath(path)==path;}
        catch(Exception e) when(e is ArgumentException or NotSupportedException){return false;}
    }
    public static string Load()=>Read(settings)??Downloads();
    internal static string? Read(string filePath){
        try{
            // 外部编辑的配置先限制大小，再解析 JSON；读取句柄也不共享写入，防止检查后文件继续增长。
            using var file=new FileStream(filePath,FileMode.Open,FileAccess.Read,FileShare.Read);
            if(file.Length<=65536){
                using var reader=new StreamReader(file);
                var path=JsonSerializer.Deserialize<string>(reader.ReadToEnd());
                if(IsFolder(path))return path;
            }
        }catch(Exception e) when(e is IOException or UnauthorizedAccessException or JsonException){}
        return null;
    }
    public static void Save(string folder){
        if(!IsFolder(folder))throw new ArgumentException("保存目录无效");
        Directory.CreateDirectory(Path.GetDirectoryName(settings)!);
        string temp=settings+"."+Guid.NewGuid().ToString("N")+".tmp";
        try{File.WriteAllText(temp,JsonSerializer.Serialize(folder));File.Move(temp,settings,true);}
        finally{if(File.Exists(temp))File.Delete(temp);}
    }
}
