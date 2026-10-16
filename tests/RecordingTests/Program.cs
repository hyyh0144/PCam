using PhoneCam;
using System.Diagnostics;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
class Program{
 [STAThread] static void Main(){Run().GetAwaiter().GetResult();}
 static async Task Run(){
   string folder=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../build/recording"));Directory.CreateDirectory(folder);string path=Path.Combine(folder,"audio-toggle-"+Guid.NewGuid().ToString("N")+".mp4");
   var read=typeof(RecordingSettings).GetMethod("Read",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic)!;
   string fixture=Path.Combine(folder,"settings-fixture.json");
   foreach(string bad in new[]{"{", "null", "\"relative/path\"",new string('x',65537)}){File.WriteAllText(fixture,bad);if(read.Invoke(null,new object[]{fixture})!=null)throw new Exception("Bad settings accepted");}
   File.WriteAllText(fixture,System.Text.Json.JsonSerializer.Serialize(folder));if(!Equals(read.Invoke(null,new object[]{fixture}),folder))throw new Exception("Valid settings rejected");
   File.Delete(fixture);if(read.Invoke(null,new object[]{fixture})!=null)throw new Exception("Missing settings accepted");
   if(!Path.IsPathFullyQualified(RecordingSettings.Downloads()))throw new Exception("Downloads known folder is not absolute");
   Console.WriteLine("PASS missing/truncated/relative/oversized settings, valid directory and Downloads lookup");
   byte[] pixels=new byte[320*240*4];for(int y=0;y<240;y++)for(int x=0;x<320;x++){int i=(y*320+x)*4;pixels[i]=(byte)(y>=120&&x<160?255:0);pixels[i+1]=(byte)(x>=160?255:0);pixels[i+2]=(byte)(y<120&&x<160||y>=120&&x>=160?255:0);pixels[i+3]=255;}
   var image=BitmapSource.Create(320,240,96,96,PixelFormats.Bgra32,null,pixels,320*4);image.Freeze();
   var neverStarted=new VideoRecorder();await neverStarted.Stop().WaitAsync(TimeSpan.FromSeconds(1));await neverStarted.Stop();
   try{await neverStarted.Start(path,image);throw new Exception("Stopped recorder restarted");}catch(InvalidOperationException){}
   var unfrozen=BitmapSource.Create(1,1,96,96,PixelFormats.Bgra32,null,new byte[]{0,0,0,255},4);var invalidImage=new VideoRecorder();
   try{await invalidImage.Start(path,unfrozen);throw new Exception("Mutable frame accepted");}catch(ArgumentException){}await invalidImage.Stop();
   Console.WriteLine("PASS stop-before-start, duplicate stop, stopped start and mutable-frame rejection");
   var recorder=new VideoRecorder();await recorder.Start(path,image);
   try{await recorder.Start(path,image);throw new Exception("Duplicate start accepted");}catch(InvalidOperationException){}
   using(var payload=typeof(VideoRecorder).Assembly.GetManifestResourceStream("PCam.Recorder.dll")!){using var memory=new MemoryStream();payload.CopyTo(memory);var hash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(memory.ToArray()));string dll=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"PCam","Recorder",hash,"PCamRecorder.dll");try{using var writer=new FileStream(dll,FileMode.Open,FileAccess.Write,FileShare.ReadWrite|FileShare.Delete);throw new Exception("Loaded DLL writable");}catch(IOException){}}
   Console.WriteLine("PASS duplicate-start rejection and loaded-DLL write lock");   var clock=Stopwatch.StartNew();long audioFrame=0;
   while(clock.Elapsed.TotalSeconds<3.2){recorder.Publish(image);if(clock.Elapsed.TotalSeconds>=1&&clock.Elapsed.TotalSeconds<2){byte[] pcm=new byte[960];for(int n=0;n<480;n++){short v=(short)(10000*Math.Sin((audioFrame*480+n)*2*Math.PI*440/24000));pcm[n*2]=(byte)v;pcm[n*2+1]=(byte)(v>>8);}recorder.AddAudio(pcm);Array.Clear(pcm);audioFrame++;}else recorder.ClearAudio();await Task.Delay(20);}
   await recorder.Stop();await recorder.Stop();if(new FileInfo(path).Length<1000)throw new Exception("Empty recording");Console.WriteLine("PASS finalized MP4: "+path);
   // A collision must fail without truncating an existing recording.
   long size=new FileInfo(path).Length;var collision=new VideoRecorder();try{await collision.Start(path,image);throw new Exception("Overwrote recording");}catch(Exception e) when(e is System.Runtime.InteropServices.COMException or IOException){}try{await collision.Completion;}catch{}if(new FileInfo(path).Length!=size)throw new Exception("Existing file altered");Console.WriteLine("PASS collision preserves existing file");
   string missing=Path.Combine(path,"child.mp4");var invalid=new VideoRecorder();try{await invalid.Start(missing,image);throw new Exception("Invalid destination accepted");}catch(Exception e) when(e is System.Runtime.InteropServices.COMException or IOException){}try{await invalid.Completion;}catch{}Console.WriteLine("PASS invalid destination reports failure");
   var quick=new VideoRecorder();string quickPath=Path.Combine(folder,"quick-"+Guid.NewGuid().ToString("N")+".mp4");var starting=quick.Start(quickPath,image);var ending=quick.Stop();await starting;await ending.WaitAsync(TimeSpan.FromSeconds(10));if(new FileInfo(quickPath).Length<1000)throw new Exception("Immediate stop failed");
   Console.WriteLine("PASS immediate stop completes a nonempty MP4");
   File.WriteAllText(Path.Combine(folder,"latest.txt"),path);
 }
}








