using PhoneCam;
using System.Diagnostics;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
class Program{
 [STAThread] static void Main(string[] args){if(args.Length==2&&args[0]=="--worker"){Worker(args[1]).GetAwaiter().GetResult();return;}Run().GetAwaiter().GetResult();}
 static BitmapSource Image(){byte[] pixels=new byte[320*240*4];for(int y=0;y<240;y++)for(int x=0;x<320;x++){int i=(y*320+x)*4;pixels[i]=(byte)(y>=120&&x<160?255:0);pixels[i+1]=(byte)(x>=160?255:0);pixels[i+2]=(byte)(y<120&&x<160||y>=120&&x>=160?255:0);pixels[i+3]=255;}var image=BitmapSource.Create(320,240,96,96,PixelFormats.Bgra32,null,pixels,320*4);image.Freeze();return image;}
 static async Task Worker(string path){var image=Image();var recorder=new VideoRecorder();await recorder.Start(path,image);File.WriteAllText(path+".ready","ready");var timer=Stopwatch.StartNew();long n=0;while(true){recorder.Publish(image);if(timer.Elapsed.TotalSeconds>1&&timer.Elapsed.TotalSeconds<2){byte[] pcm=new byte[960];for(int i=0;i<480;i++){short value=(short)(10000*Math.Sin((n*480+i)*2*Math.PI*440/24000));pcm[i*2]=(byte)value;pcm[i*2+1]=(byte)(value>>8);}recorder.AddAudio(pcm);n++;}else recorder.ClearAudio();await Task.Delay(20);}}
 static async Task Until(Func<bool> predicate,int seconds=20){var timer=Stopwatch.StartNew();while(!predicate()){if(timer.Elapsed.TotalSeconds>seconds)throw new Exception("Timed out");await Task.Delay(100);}}
 static async Task Run(){
 string folder=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../build/durable/"+Guid.NewGuid().ToString("N")));Directory.CreateDirectory(folder);string output=Path.Combine(folder,"test.mp4");
 var start=new ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=false,CreateNoWindow=true};start.ArgumentList.Add("--worker");start.ArgumentList.Add(output);using var worker=Process.Start(start)!;
 try{
  await Until(()=>File.Exists(output+".ready")||worker.HasExited);if(worker.HasExited)throw new Exception("Recorder child exited");string session=Directory.GetDirectories(Path.Combine(folder,".pcam-recording")).Single();string cache=Path.Combine(session,"stream.mp4");
  await Task.Delay(3500);long first=new FileInfo(cache).Length;await Task.Delay(2200);long second=new FileInfo(cache).Length;
  if(first<=1200||second<=first)throw new Exception("Second-based disk cache did not grow");if(new FileInfo(output).Length!=0)throw new Exception("Formal file committed before minute boundary");
  var active=RecordingRecovery.Recover(new[]{folder});if(active.Recovered.Any(path=>Path.GetDirectoryName(path)==folder)||!Directory.Exists(session))throw new Exception("Active recording was recovered");Console.WriteLine($"PASS temporary file grows on disk ({first}->{second} bytes), formal file waits for minute, active-session lock");
  await Until(()=>new FileInfo(output).Length>1200||worker.HasExited,75);if(worker.HasExited)throw new Exception("Recorder child exited");long minute=new FileInfo(output).Length;await Task.Delay(2500);if(new FileInfo(cache).Length<=minute)throw new Exception("Recording did not continue during commit");Console.WriteLine("PASS real 60-second formal commit while disk cache continues: "+minute);
  worker.Kill(true);await worker.WaitForExitAsync();
  // Simulate a torn tail too; only complete MP4 fragments should be copied on recovery.
  using(var file=new FileStream(cache,FileMode.Append,FileAccess.Write,FileShare.None)){file.Write(new byte[]{0,0,0,20,(byte)'m',(byte)'d',(byte)'a',(byte)'t',1,2});file.Flush(true);}
  var result=RecordingRecovery.Recover(new[]{folder});if(result.Recovered.Count!=1||result.Preserved!=0)throw new Exception("Crash recovery failed");string recovered=result.Recovered.Single();if(new FileInfo(recovered).Length<=minute)throw new Exception("Latest temporary footage was not recovered");if(new FileInfo(output).Length!=minute)throw new Exception("Previously committed video was changed");
  if(RecordingRecovery.Recover(new[]{folder}).Recovered.Count!=0)throw new Exception("Recovery repeated");Console.WriteLine("PASS killed recorder + torn tail automatically recovers latest complete fragments; previous formal file preserved; second run idempotent");
  // Recovery can itself be interrupted: an identical prefix is resumed, never truncated.
  string resume=Path.Combine(folder,".pcam-recording",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(resume);File.WriteAllText(Path.Combine(resume,"session.json"),System.Text.Json.JsonSerializer.Serialize(new{Version=1,FileName="resume.mp4",StartedUtc=DateTimeOffset.UtcNow}));File.Copy(recovered,Path.Combine(resume,"stream.mp4"));string resumeTarget=Path.Combine(folder,"resume_恢复_"+Path.GetFileName(resume)+".mp4");using(var from=File.OpenRead(recovered)){byte[] header=new byte[900];from.ReadExactly(header);File.WriteAllBytes(resumeTarget,header);}var resumed=RecordingRecovery.Recover(new[]{folder});if(resumed.Recovered.Count!=1||resumed.Recovered[0]!=resumeTarget)throw new Exception("Interrupted recovery did not resume");Console.WriteLine("PASS interrupted recovery resumes an identical partial destination");
  // Untrusted metadata cannot escape its selected parent, and no useful source is deleted on failure.
  string bad=Path.Combine(folder,".pcam-recording",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(bad);File.Copy(recovered,Path.Combine(bad,"stream.mp4"));File.WriteAllText(Path.Combine(bad,"session.json"),System.Text.Json.JsonSerializer.Serialize(new{Version=1,FileName="../outside.mp4",StartedUtc=DateTimeOffset.UtcNow}));var rejected=RecordingRecovery.Recover(new[]{folder});if(rejected.Preserved!=1||!File.Exists(Path.Combine(bad,"stream.mp4"))||File.Exists(Path.Combine(folder,"outside.mp4")))throw new Exception("Unsafe recovery metadata accepted");Console.WriteLine("PASS path traversal rejected and original cache retained");
  File.WriteAllText(Path.Combine(folder,"recovered.txt"),recovered);string testRoot=Path.GetDirectoryName(folder)!;File.WriteAllText(Path.Combine(testRoot,"latest.txt"),recovered);
 }finally{if(!worker.HasExited){worker.Kill(true);await worker.WaitForExitAsync();}}
 }
}


