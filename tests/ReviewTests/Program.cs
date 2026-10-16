using PhoneCam;
using System.Buffers.Binary;
using System.Text;
using System.Text.Json;

static class Program {
 static void Check(bool value,string message){if(!value)throw new Exception(message);}
 static string Session(string folder,string name,string fixture){
  string dir=Path.Combine(folder,".pcam-recording",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
  File.Copy(fixture,Path.Combine(dir,"stream.mp4"));File.WriteAllText(Path.Combine(dir,"session.json"),JsonSerializer.Serialize(new{Version=1,FileName=name,StartedUtc=DateTimeOffset.UtcNow}));return dir;
 }
 static byte[] Box(string kind,params byte[][] parts){int size=8+parts.Sum(x=>x.Length);var b=new byte[size];BinaryPrimitives.WriteUInt32BigEndian(b,(uint)size);Encoding.ASCII.GetBytes(kind).CopyTo(b,4);int p=8;foreach(var part in parts){part.CopyTo(b,p);p+=part.Length;}return b;}
 static async Task Main(string[] args){
  string fixture=Path.GetFullPath(args.Single()),folder=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../build/review/"+Guid.NewGuid().ToString("N")));Directory.CreateDirectory(folder);
  // A malformed directory created first used to prevent valid later sessions being discovered.
  Directory.CreateDirectory(Path.Combine(folder,".pcam-recording","not-a-session"));
  string resumed=Session(folder,"resume.mp4",fixture);string target=Path.Combine(folder,"resume_恢复_"+Path.GetFileName(resumed)+".mp4");File.Copy(fixture,target);
  using(var file=new FileStream(target,FileMode.Open,FileAccess.ReadWrite,FileShare.None))Mp4Fragments.SetDuration(file,0.5);
  string conflict=Session(folder,"conflict.mp4",fixture);string occupied=Path.Combine(folder,"conflict_恢复_"+Path.GetFileName(conflict)+".mp4");byte[] sentinel=Encoding.UTF8.GetBytes("existing user's video");File.WriteAllBytes(occupied,sentinel);
  var result=RecordingRecovery.Recover(new[]{folder});Check(result.Recovered.Count==2&&result.Preserved==0,"valid sessions skipped");Check(result.Recovered.Contains(target),"duration-patched recovery duplicated");Check(File.ReadAllBytes(occupied).SequenceEqual(sentinel),"conflicting file overwritten");Check(!Directory.Exists(resumed)&&!Directory.Exists(conflict),"successful cache not cleaned");Check(RecordingRecovery.Recover(new[]{folder}).Recovered.Count==0,"repeat recovery duplicated");
  Console.WriteLine("PASS malformed neighbor isolation, duration-patched retry, collision preservation, idempotent cleanup");
  string formal=Path.Combine(folder,"journal.mp4");using(var journal=new RecordingJournal(formal)){
   File.Copy(fixture,journal.CacheFile);long complete=journal.Checkpoint();Check(complete>0,"missing complete fragments");byte[] cache=File.ReadAllBytes(journal.CacheFile);
   // Simulate the source becoming unavailable mid-commit. Retry must use confirmed bytes, not stale file length.
   File.WriteAllBytes(journal.CacheFile,Array.Empty<byte>());journal.RequestCommit(complete);await journal.Drain();Check(journal.CommitError!=null&&new FileInfo(formal).Length==0,"failed commit was accepted");
   File.WriteAllBytes(journal.CacheFile,cache);await Task.WhenAll(Enumerable.Range(0,64).Select(_=>Task.Run(()=>journal.RequestCommit(complete))));await journal.Finish();Check(journal.CommitError==null&&new FileInfo(formal).Length==complete,"commit retry lost or corrupted");journal.CloseOutput();journal.RemoveCache();
  }Console.WriteLine("PASS failed commit preserves cache, concurrent commit requests and successful retry");
  using(var journal=new RecordingJournal(Path.Combine(folder,"rollback.mp4"))){
   byte[] all=File.ReadAllBytes(fixture);int boundary=0;
   for(int p=0;p<all.Length;){int size=checked((int)BinaryPrimitives.ReadUInt32BigEndian(all.AsSpan(p)));if(Encoding.ASCII.GetString(all,p+4,4)=="mdat"){boundary=p+size;break;}p+=size;}
   Check(boundary>0,"fixture missing first fragment");File.WriteAllBytes(journal.CacheFile,all[..boundary]);journal.RequestCommit(journal.Checkpoint());await journal.Drain();
   var field=typeof(RecordingJournal).GetField("output",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!;var output=(FileStream)field.GetValue(journal)!;
   byte[] oldHeader=new byte[Math.Min(1000,boundary)];output.Position=0;output.ReadExactly(oldHeader);
   File.WriteAllBytes(journal.CacheFile,all);long end=journal.Checkpoint();File.WriteAllBytes(journal.CacheFile,all[..boundary]);journal.RequestCommit(end);await journal.Drain();
   Check(journal.CommitError!=null&&output.Length==boundary,"prior committed bytes lost");byte[] header=new byte[oldHeader.Length];output.Position=0;output.ReadExactly(header);Check(header.SequenceEqual(oldHeader),"rollback changed prior header");
   // Model a failed truncation leaving stray bytes; the next attempt must discard them before appending.
   output.Position=output.Length;output.Write(new byte[11]);output.Flush(true);File.WriteAllBytes(journal.CacheFile,all);await journal.Finish();Check(output.Length==end,"unconfirmed tail trusted");journal.CloseOutput();journal.RemoveCache();
  }Console.WriteLine("PASS nonempty rollback preserves previous header and retry discards unconfirmed tail");
  // Synthetic broken metadata must be rejected before a sample can consume bytes from a sibling box.
  byte[] tkhd=new byte[16],mdhd=new byte[16];BinaryPrimitives.WriteUInt32BigEndian(tkhd.AsSpan(12),1);BinaryPrimitives.WriteUInt32BigEndian(mdhd.AsSpan(12),1000);
  byte[] movie=Box("moov",Box("trak",Box("tkhd",tkhd),Box("mdia",Box("mdhd",mdhd))));
  byte[] tfhd=new byte[8];BinaryPrimitives.WriteUInt32BigEndian(tfhd.AsSpan(4),1);
  byte[] trun=new byte[8];BinaryPrimitives.WriteUInt32BigEndian(trun,0x100);BinaryPrimitives.WriteUInt32BigEndian(trun.AsSpan(4),1);
  byte[] bad=Box("moof",Box("traf",Box("tfhd",tfhd),Box("trun",trun),Box("free",new byte[4])));
  foreach(var tail in new[]{bad,Box("moof",new byte[3]),Box("moof",Box("traf",Box("tfhd",new byte[]{0,0,0,1,0,0,0,1})))}){
   string path=Path.Combine(folder,Guid.NewGuid()+".mp4");using(var file=File.Create(path)){file.Write(Box("ftyp"));file.Write(movie);file.Write(tail);file.Write(Box("mdat"));}
   bool rejected=false;try{new Mp4Fragments().Update(path);}catch(IOException){rejected=true;}Check(rejected,"malformed metadata accepted");
  }Console.WriteLine("PASS truncated sample table, incomplete nested header and truncated optional tfhd fields rejected");
 }
}
