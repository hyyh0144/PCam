using System.IO;
using System.Text.Json;
namespace PhoneCam;

// The encoder owns stream.mp4; a separate bounded writer appends only complete prefixes to the formal file.
// Preserve byte offsets: fragment tfhd/trun references must not be shifted by concatenating independent MP4s.
sealed class RecordingJournal : IDisposable
{
    internal const string CacheRoot=".pcam-recording";
    internal record Manifest(int Version,string FileName,DateTimeOffset StartedUtc);
    internal static string IndexDirectory=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"PCam","recording-recovery");
    readonly string session,index;
    readonly FileStream lease,output;
    readonly object gate=new();
    readonly Mp4Fragments scanner=new();
    Task commit=Task.CompletedTask;
    long requested,committed;double requestedSeconds,committedSeconds;
    bool committing;
    string? commitError;
    public string CacheFile {get;}
    public string SessionDirectory=>session;
    public string? CommitError {get{lock(gate)return commitError;}}
    public RecordingJournal(string destination){
        string parent=Path.GetDirectoryName(Path.GetFullPath(destination))!;Directory.CreateDirectory(parent);
        // Reservation fails rather than replacing any existing video. Keep the handle for this recording's lifetime.
        output=new FileStream(destination,FileMode.CreateNew,FileAccess.ReadWrite,FileShare.Read);
        string id=Guid.NewGuid().ToString("N");session=Path.Combine(parent,CacheRoot,id);CacheFile=Path.Combine(session,"stream.mp4");
        index=Path.Combine(IndexDirectory,id+".json");
        try{
            CameraInstallation.RejectReparsePoints(Path.Combine(parent,CacheRoot));Directory.CreateDirectory(session);
            lease=new FileStream(Path.Combine(session,"session.lock"),FileMode.CreateNew,FileAccess.ReadWrite,FileShare.None);
            WriteJson(Path.Combine(session,"session.json"),new Manifest(1,Path.GetFileName(destination),DateTimeOffset.UtcNow));
            Directory.CreateDirectory(IndexDirectory);WriteJson(index,session);
        }catch{output.Dispose();lease?.Dispose();try{Cleanup(session,index);}catch{}throw;}
    }
    internal static void WriteJson<T>(string path,T value){
        string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try{using(var file=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None)){JsonSerializer.Serialize(file,value);file.Flush(true);}File.Move(temp,path,true);}
        finally{if(File.Exists(temp))File.Delete(temp);}
    }
    public long Checkpoint()=>scanner.Update(CacheFile);
    public void RequestCommit(long complete){
        // Worker ownership is released under this same lock. Task.IsCompleted leaves a lost-wakeup window.
        lock(gate){if(complete>=requested){requested=complete;requestedSeconds=scanner.CompleteSeconds;}if(!committing){committing=true;commit=Task.Run(CommitLoop);}}
    }
    void CommitLoop(){
        while(true){
            long length;double seconds;lock(gate){length=requested;seconds=requestedSeconds;if(length<=committed){committing=false;return;}}
            try{AppendPrefix(length,seconds);lock(gate){committed=length;committedSeconds=seconds;commitError=null;}}
            catch(Exception e){lock(gate){commitError=e.Message;committing=false;}return;}
        }
    }
    void AppendPrefix(long length,double seconds){
        // A failed write/rollback can leave extra bytes. Resume from the last flushed commit, never file.Length.
        long previous=committed;
        try{
            output.SetLength(previous);
            using var source=new FileStream(CacheFile,FileMode.Open,FileAccess.Read,FileShare.ReadWrite);
            if(length>source.Length||previous>length)throw new IOException("录像整合边界异常");
            source.Position=output.Position=previous;RecordingRecovery.CopyPrefix(source,output,length-previous);
            // Request persistent storage only after the entire complete-fragment prefix has been appended.
            Mp4Fragments.SetDuration(output,seconds);output.Flush(true);
        }catch{try{output.SetLength(previous);if(previous>0)Mp4Fragments.SetDuration(output,committedSeconds);output.Flush(true);}catch{}throw;}
    }
    public async Task Finish(){
        // Native finalization has finished before this is called; no more bytes can arrive.
        long complete=Checkpoint();if(complete==0)throw new IOException("尚无完整录像片段，临时数据已保留");
        Task running;lock(gate)running=commit;await running.ConfigureAwait(false);
        RequestCommit(complete);lock(gate)running=commit;await running.ConfigureAwait(false);
        lock(gate){if(committed!=complete||commitError!=null)throw new IOException("录像整合未完成，临时数据已保留："+commitError);}
        if(new Mp4Fragments().Update(output.Name)!=complete)throw new IOException("正式录像完整性检查失败，临时数据已保留");
    }
    public Task Drain(){lock(gate)return commit;}
    public void CloseOutput()=>output.Dispose();
    public void Dispose(){output.Dispose();lease.Dispose();}
    public void RemoveCache(){Cleanup(session,index,lease);}
    internal static void Cleanup(string session,string? index,FileStream? lease=null){
        // Delete only our known files, never recursively delete a directory obtained from persisted metadata.
        CameraInstallation.RejectReparsePoints(session);
        foreach(string name in new[]{"stream.mp4","session.json"}){string path=Path.Combine(session,name);CameraInstallation.RejectReparsePoints(path);if(File.Exists(path))File.Delete(path);}
        // Keep the session exclusive until its recoverable input is gone; a concurrent startup must skip it.
        lease?.Dispose();string lockPath=Path.Combine(session,"session.lock");if(File.Exists(lockPath))File.Delete(lockPath);
        if(!Directory.EnumerateFileSystemEntries(session).Any())Directory.Delete(session);
        if(index!=null&&File.Exists(index))File.Delete(index);
    }
}




