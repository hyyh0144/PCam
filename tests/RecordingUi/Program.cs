using PhoneCam;
using System.IO;
using System.Reflection;
using System.Diagnostics;
using System.Collections.Concurrent;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
class Program{
 [STAThread] static void Main(){
 var app=new Application();SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());var w=new MainWindow();
 FieldInfo Field(string n)=>typeof(MainWindow).GetField(n,BindingFlags.Instance|BindingFlags.NonPublic)!;
 void PumpUntil(Func<bool> done){var clock=Stopwatch.StartNew();while(!done()){if(clock.Elapsed.TotalSeconds>15)throw new Exception("UI timeout");var f=new DispatcherFrame();var t=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(20)};t.Tick+=(_,_)=>{t.Stop();f.Continue=false;};t.Start();Dispatcher.PushFrame(f);}}
 var toggle=(Button)w.FindName("RecordToggle");toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));if(!((TextBlock)w.FindName("RecordHint")).Text.Contains("请先连接"))throw new Exception("No-source guard");
 string dir=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../build/recording/ui"));Directory.CreateDirectory(dir);Field("recordingFolder").SetValue(w,dir);
 var id=Guid.NewGuid();((Composition)Field("composition").GetValue(w)!).Connect(id);((ConcurrentDictionary<Guid,byte[]>)Field("frames").GetValue(w)!)[id]=new byte[]{1};
 var image=BitmapSource.Create(2,2,96,96,PixelFormats.Bgra32,null,new byte[]{0,0,255,255,0,0,255,255,0,0,255,255,0,0,255,255},8);image.Freeze();Field("composed").SetValue(w,new ComposedFrame(image,Array.Empty<byte>(),new Dictionary<Guid,Rect>()));
 int before=Directory.GetFiles(dir,"*.mp4").Length;
 for(int i=0;i<2;i++){toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));PumpUntil(()=>toggle.IsEnabled);if(!Equals(toggle.Content,"停止录制"))throw new Exception(((TextBlock)w.FindName("RecordHint")).Text);var timer=Stopwatch.StartNew();PumpUntil(()=>timer.Elapsed.TotalMilliseconds>350);toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));PumpUntil(()=>toggle.IsEnabled);if(!((TextBlock)w.FindName("RecordHint")).Text.StartsWith("已保存"))throw new Exception("Save status");}
 if(Directory.GetFiles(dir,"*.mp4").Length!=before+2)throw new Exception("Repeated recording missing");Console.WriteLine("PASS actual recording button, no-source guard, custom folder, two start/stop sessions");
 // A destination that is a regular file must fail without hanging the dispatcher, then permit retry.
 string blocker=Path.Combine(dir,"blocked-"+Guid.NewGuid().ToString("N"));File.WriteAllText(blocker,"unchanged");Field("recordingFolder").SetValue(w,blocker);
 toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));PumpUntil(()=>toggle.IsEnabled);
 if(!Equals(toggle.Content,"视频录制")||!((TextBlock)w.FindName("RecordHint")).Text.StartsWith("无法开始"))throw new Exception("Failed start did not recover UI");
 if(File.ReadAllText(blocker)!="unchanged")throw new Exception("Invalid destination altered file");Field("recordingFolder").SetValue(w,dir);
 Console.WriteLine("PASS failed-start UI recovery and destination integrity");
 // Exercise the same method registered on Loaded, without launching a second camera/receiver instance.
 string fixture=Path.Combine(dir,".pcam-recording",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(fixture);
 string recent=File.ReadAllText(Path.Combine(Path.GetDirectoryName(dir)!,"latest.txt"));File.Copy(recent,Path.Combine(fixture,"stream.mp4"));
 File.WriteAllText(Path.Combine(fixture,"session.json"),System.Text.Json.JsonSerializer.Serialize(new{Version=1,FileName="startup.mp4",StartedUtc=DateTimeOffset.UtcNow}));
 string index=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"PCam","recording-recovery");Directory.CreateDirectory(index);File.WriteAllText(Path.Combine(index,Path.GetFileName(fixture)+".json"),System.Text.Json.JsonSerializer.Serialize(fixture));
 var recovery=(Task)typeof(MainWindow).GetMethod("RecoverRecordings",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(w,null)!;PumpUntil(()=>recovery.IsCompleted);recovery.GetAwaiter().GetResult();
 if(Directory.Exists(fixture)||!File.Exists(Path.Combine(dir,"startup_恢复_"+Path.GetFileName(fixture)+".mp4"))||!((TextBlock)w.FindName("RecordHint")).Text.StartsWith("已恢复"))throw new Exception("Startup recovery method/status failed");
 Console.WriteLine("PASS startup recovery method, persisted pointer, decoded recovered file, cleanup and UI notification");
 toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));PumpUntil(()=>toggle.IsEnabled);bool closed=false;w.Closed+=(_,_)=>closed=true;w.Close();PumpUntil(()=>closed);Console.WriteLine("PASS closing window finalizes active recording");app.Shutdown();
 }
}




