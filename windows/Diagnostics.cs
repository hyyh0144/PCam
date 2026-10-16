// 有界本地日志，不记录画面、声音或配对凭据。
using System.IO;
namespace PhoneCam;
static class Diagnostics {
    static readonly object gate=new();
    public static void Write(string message){lock(gate)try{
        string directory=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"PCam","logs");Directory.CreateDirectory(directory);
        string path=Path.Combine(directory,"connection.log");if(File.Exists(path)&&new FileInfo(path).Length>256*1024)File.Move(path,path+".old",true);
        File.AppendAllText(path,DateTimeOffset.Now.ToString("O")+" "+message+Environment.NewLine);
    }catch(IOException){}catch(UnauthorizedAccessException){} }
}
