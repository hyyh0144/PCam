using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
namespace PhoneCam;
public sealed record RecordingRecoveryResult(IReadOnlyList<string> Recovered,int Preserved);
public static class RecordingRecovery
{
    [UnmanagedFunctionPointer(CallingConvention.StdCall,CharSet=CharSet.Unicode)]delegate int ValidateNative([MarshalAs(UnmanagedType.LPWStr)]string path);
    static string ValidateSession(string path){
        string full=Path.GetFullPath(path);string id=Path.GetFileName(full);
        if(!Guid.TryParseExact(id,"N",out _)||Path.GetFileName(Path.GetDirectoryName(full))!=RecordingJournal.CacheRoot)throw new IOException("恢复目录标识无效");
        CameraInstallation.RejectReparsePoints(full);return full;
    }
    static IEnumerable<string> Candidates(IEnumerable<string>? folders){
        var result=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(string index in Entries(RecordingJournal.IndexDirectory,false))try{
            CameraInstallation.RejectReparsePoints(index);
            using var file=new FileStream(index,FileMode.Open,FileAccess.Read,FileShare.Read);if(file.Length>4096)continue;
            string? session=JsonSerializer.Deserialize<string>(file);
            if(session!=null&&Path.GetFileNameWithoutExtension(index)==Path.GetFileName(session))result.Add(ValidateSession(session));
        }catch(Exception e) when(e is IOException or UnauthorizedAccessException or JsonException or ArgumentException){Diagnostics.Write("recording recovery index: "+e.GetType().Name);}
        if(folders==null){var defaults=new List<string>();try{defaults.Add(RecordingSettings.Load());}catch{}try{defaults.Add(RecordingSettings.Downloads());}catch{}folders=defaults;}
        foreach(string folder in folders)foreach(string session in Entries(Path.Combine(folder,RecordingJournal.CacheRoot),true))try{result.Add(ValidateSession(session));}catch(Exception e) when(e is IOException or UnauthorizedAccessException or ArgumentException){Diagnostics.Write("recording recovery scan: "+e.GetType().Name);}
        return result;
    }
    static IEnumerable<string> Entries(string root,bool directories){
        // Handle errors per entry and per root; one corrupt session must not hide all later recordings.
        IEnumerator<string>? entries=null;
        try{CameraInstallation.RejectReparsePoints(root);if(Directory.Exists(root))entries=(directories?Directory.EnumerateDirectories(root):Directory.EnumerateFiles(root,"*.json")).GetEnumerator();}
        catch(Exception e) when(e is IOException or UnauthorizedAccessException or ArgumentException){Diagnostics.Write("recording recovery enumerate: "+e.GetType().Name);}
        if(entries==null)yield break;
        using(entries)while(true){string current;try{if(!entries.MoveNext())break;current=entries.Current;}catch(Exception e) when(e is IOException or UnauthorizedAccessException){Diagnostics.Write("recording recovery enumerate: "+e.GetType().Name);break;}yield return current;}
    }
    public static RecordingRecoveryResult Recover(IEnumerable<string>? folders=null){
        var recovered=new List<string>();int preserved=0;
        using var component=RecorderComponent.Load();var validate=Marshal.GetDelegateForFunctionPointer<ValidateNative>(NativeLibrary.GetExport(component.Library,"RecValidate"));
        foreach(string candidate in Candidates(folders)){
            FileStream? lease=null;
            try{
                if(!Directory.Exists(candidate)){string stale=Path.Combine(RecordingJournal.IndexDirectory,Path.GetFileName(candidate)+".json");if(File.Exists(stale))File.Delete(stale);continue;}
                string session=ValidateSession(candidate);
                string lockPath=Path.Combine(session,"session.lock");CameraInstallation.RejectReparsePoints(lockPath);
                try{lease=new FileStream(lockPath,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);}catch(IOException e) when((e.HResult&0xffff) is 32 or 33){continue;} // Only sharing/lock violations mean another active PCam process.
                string metadata=Path.Combine(session,"session.json");
                CameraInstallation.RejectReparsePoints(metadata);
                using var json=new FileStream(metadata,FileMode.Open,FileAccess.Read,FileShare.Read);if(json.Length>8192)throw new IOException("恢复记录过大");
                var manifest=JsonSerializer.Deserialize<RecordingJournal.Manifest>(json);json.Dispose();
                if(manifest==null||manifest.Version!=1||string.IsNullOrWhiteSpace(manifest.FileName)||manifest.FileName!=Path.GetFileName(manifest.FileName)||manifest.FileName.IndexOfAny(Path.GetInvalidFileNameChars())>=0||!manifest.FileName.EndsWith(".mp4",StringComparison.OrdinalIgnoreCase))throw new IOException("恢复记录无效");
                string cache=Path.Combine(session,"stream.mp4");CameraInstallation.RejectReparsePoints(cache);
                var fragments=new Mp4Fragments();long length=fragments.Update(cache);if(length==0){preserved++;continue;}
                string folder=Path.GetDirectoryName(Path.GetDirectoryName(session))!;
                string name=Path.GetFileNameWithoutExtension(manifest.FileName);if(name.Length>120)name=name[..120];
                string destination=Path.Combine(folder,name+"_恢复_"+Path.GetFileName(session)+".mp4");
                using(var input=new FileStream(cache,FileMode.Open,FileAccess.Read,FileShare.Read))
                using(var output=OpenDestination(input,ref destination,length)){
                    long existing=output.Length;if(existing>length)throw new IOException("恢复文件长度异常");
                    input.Position=output.Position=existing;CopyPrefix(input,output,length-existing);Mp4Fragments.SetDuration(output,fragments.CompleteSeconds);output.Flush(true);
                }
                if(new Mp4Fragments().Update(destination)!=length)throw new IOException("恢复文件结构检查失败");
                Marshal.ThrowExceptionForHR(validate(destination));
                recovered.Add(destination);
                RecordingJournal.Cleanup(session,Path.Combine(RecordingJournal.IndexDirectory,Path.GetFileName(session)+".json"),lease);
            }catch(Exception e) when(e is IOException or UnauthorizedAccessException or JsonException or ArgumentException or COMException){preserved++;Diagnostics.Write("recording recovery retained: "+e.Message);}
            finally{lease?.Dispose();}
        }
        return new(recovered,preserved);
    }
    static FileStream OpenDestination(FileStream input,ref string destination,long length){
        for(int attempt=0;attempt<10;attempt++){
            CameraInstallation.RejectReparsePoints(destination);
            if(File.Exists(destination)){
                var existing=new FileStream(destination,FileMode.Open,FileAccess.ReadWrite,FileShare.Read);
                try{if(MatchesPrefix(input,existing,length))return existing;}catch{existing.Dispose();throw;}
                existing.Dispose();
            }else{
                try{return new FileStream(destination,FileMode.CreateNew,FileAccess.ReadWrite,FileShare.Read);}
                catch(IOException e) when((e.HResult&0xffff) is 80 or 183){} // A racing creator owns this name.
            }
            destination=Path.Combine(Path.GetDirectoryName(destination)!,"PCam_恢复_"+Guid.NewGuid().ToString("N")+".mp4");
        }
        throw new IOException("无法预留恢复文件名，临时数据已保留");
    }
    static bool MatchesPrefix(FileStream a,FileStream b,long length){
        if(b.Length>length)return false;var duration=Mp4Fragments.DurationField(a);
        a.Position=b.Position=0;byte[] x=new byte[128*1024],y=new byte[x.Length];long left=b.Length,position=0;
        while(left>0){int count=(int)Math.Min(x.Length,left);a.ReadExactly(x.AsSpan(0,count));b.ReadExactly(y.AsSpan(0,count));
            // Recovery changes only mvhd.duration. Ignore those bytes so a crash after patching is idempotent.
            int start=(int)Math.Clamp(duration.Offset-position,0,count),end=(int)Math.Clamp(duration.Offset+duration.Count-position,0,count);
            x.AsSpan(start,end-start).Clear();y.AsSpan(start,end-start).Clear();
            if(!x.AsSpan(0,count).SequenceEqual(y.AsSpan(0,count)))return false;left-=count;position+=count;
        }return true;
    }
    internal static void CopyPrefix(Stream source,Stream destination,long remaining){
        byte[] buffer=new byte[128*1024];while(remaining>0){int count=source.Read(buffer,0,(int)Math.Min(buffer.Length,remaining));if(count==0)throw new EndOfStreamException();destination.Write(buffer,0,count);remaining-=count;}
    }
}


