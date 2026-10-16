using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace PhoneCam;

// The STA worker owns the encoder and samples the latest composition. No encoder work runs on the UI thread.
public sealed class VideoRecorder
{
    [UnmanagedFunctionPointer(CallingConvention.StdCall,CharSet=CharSet.Unicode)] delegate int StartNative([MarshalAs(UnmanagedType.LPWStr)]string path,out nint context);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int WriteNative(nint context,byte[] data,int size,long time,long duration,int audio);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int StopNative(nint context);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int FlushNative(nint context);
    [UnmanagedFunctionPointer(CallingConvention.StdCall,CharSet=CharSet.Unicode)] delegate int ValidateNative([MarshalAs(UnmanagedType.LPWStr)]string path);
    readonly object gate=new();
    readonly Queue<(long Time,byte[] Bytes)> audio=new();
    int started;
    BitmapSource? latest;
    long published;
    volatile bool stopping;
    readonly TaskCompletionSource ready=new(TaskCreationOptions.RunContinuationsAsynchronously);
    readonly TaskCompletionSource finished=new(TaskCreationOptions.RunContinuationsAsynchronously);
    readonly Stopwatch clock=new();

    RecordingJournal? journal;
    public string? StorageWarning=>journal?.CommitError;
    public TimeSpan Elapsed=>clock.Elapsed;
    public Task Completion=>finished.Task;
    public Task Start(string path,BitmapSource initial){
        ArgumentNullException.ThrowIfNull(initial);
        if(!initial.IsFrozen)throw new ArgumentException("Frame must be frozen");
        // 生命周期检查与 Stop 共用同一把锁，避免重复工作线程或等待一个永远不会启动的任务。
        lock(gate){if(started!=0||stopping)throw new InvalidOperationException("录制器不能重复启动或停止后启动");started=1;}
        Publish(initial);
        try{
            var worker=new Thread(()=>Run(path)){IsBackground=true,Name="PCam MP4 recording"};
            worker.SetApartmentState(ApartmentState.STA);worker.Start();
        }catch(Exception e){lock(gate)stopping=true;ready.TrySetException(e);finished.TrySetException(e);}
        return ready.Task;
    }
    public void Publish(BitmapSource? image){if(image!=null&&!image.IsFrozen)throw new ArgumentException("Frame must be frozen");lock(gate){if(stopping)return;latest=image;published=Stopwatch.GetTimestamp();}}
    public void AddAudio(byte[] pcm){if(pcm.Length!=960)return;lock(gate){if(stopping)return;while(audio.Count>=10)audio.Dequeue();audio.Enqueue((Stopwatch.GetTimestamp(),(byte[])pcm.Clone()));}}
    public void ClearAudio(){lock(gate)audio.Clear();}
    public Task Stop(){lock(gate){stopping=true;if(started==0){ready.TrySetCanceled();finished.TrySetResult();}}return finished.Task;}
    void Run(string path){
        RecorderComponent? component=null;nint context=0;bool finalized=false,merged=false;StopNative? stop=null;Exception? failure=null;
        try{
            journal=new RecordingJournal(path);
            component=RecorderComponent.Load();nint dll=component.Library;var start=Marshal.GetDelegateForFunctionPointer<StartNative>(NativeLibrary.GetExport(dll,"RecStart"));var write=Marshal.GetDelegateForFunctionPointer<WriteNative>(NativeLibrary.GetExport(dll,"RecWrite"));stop=Marshal.GetDelegateForFunctionPointer<StopNative>(NativeLibrary.GetExport(dll,"RecStop"));
            var flush=Marshal.GetDelegateForFunctionPointer<FlushNative>(NativeLibrary.GetExport(dll,"RecFlush"));
            Marshal.ThrowExceptionForHR(start(journal.CacheFile,out context));clock.Start();ready.TrySetResult();
            long nextFlush=TimeSpan.TicksPerSecond,nextCommit=TimeSpan.TicksPerMinute;
            var target=new RenderTargetBitmap(1280,720,96,96,PixelFormats.Pbgra32);byte[] pixels=new byte[1280*720*4],silence=new byte[960];BitmapSource? previous=null;long videoIndex=0,audioIndex=0;
            // 即使刚启动就停止，也写入最小音画样本，再 Finalize，避免空 MP4。
            while(!stopping||videoIndex==0||audioIndex==0){
                long now=clock.Elapsed.Ticks;
                if(now>=nextFlush){Marshal.ThrowExceptionForHR(flush(context));long complete=journal.Checkpoint();if(now>=nextCommit){journal.RequestCommit(complete);nextCommit=now+TimeSpan.TicksPerMinute;}nextFlush=now+TimeSpan.TicksPerSecond;}
                // Skip missed time slots after a slow encoder instead of rapidly replaying stale audio/video.
                if(videoIndex>0&&now-videoIndex*10000000/30>5000000)videoIndex=now*30/10000000;
                if(audioIndex>0&&now-audioIndex*200000>5000000){audioIndex=now/200000;ClearAudio();}
                long videoTime=videoIndex*10000000/30,audioTime=audioIndex*200000;
                if(Math.Min(videoTime,audioTime)>now){Thread.Sleep(2);continue;}
                if(videoTime<=audioTime){
                    BitmapSource? image;lock(gate)image=Stopwatch.GetElapsedTime(published).TotalSeconds<2?latest:null;
                    if(videoIndex==0||!ReferenceEquals(previous,image)){
                        var visual=new DrawingVisual();using(var dc=visual.RenderOpen()){dc.DrawRectangle(Brushes.Black,null,new Rect(0,0,1280,720));if(image!=null)dc.DrawImage(image,FrameComposer.Fit(image.PixelWidth,image.PixelHeight,new Rect(0,0,1280,720)));}
                        target.Clear();target.Render(visual);target.CopyPixels(pixels,1280*4,0);previous=image;
                    }
                    long duration=(videoIndex+1)*10000000/30-videoTime;Marshal.ThrowExceptionForHR(write(context,pixels,pixels.Length,videoTime,duration,0));videoIndex++;
                }else{
                    byte[] pcm;lock(gate){while(audio.Count>0&&Stopwatch.GetElapsedTime(audio.Peek().Time).TotalMilliseconds>200)audio.Dequeue();pcm=audio.Count>0?audio.Dequeue().Bytes:silence;}
                    Marshal.ThrowExceptionForHR(write(context,pcm,pcm.Length,audioTime,200000,1));audioIndex++;
                }
            }
        }catch(Exception e){failure=e;ready.TrySetException(e);}
        finally{
            clock.Stop();if(context!=0)try{Marshal.ThrowExceptionForHR(stop!(context));finalized=true;}catch(Exception e){failure??=e;}
            if(journal!=null&&finalized)try{journal.Finish().GetAwaiter().GetResult();merged=true;}catch(Exception e){failure??=e;}
            try{journal?.Drain().GetAwaiter().GetResult();}catch(Exception e){failure??=e;}
            try{journal?.CloseOutput();}catch(Exception e){failure??=e;}
            if(merged&&component!=null)try{var validate=Marshal.GetDelegateForFunctionPointer<ValidateNative>(NativeLibrary.GetExport(component.Library,"RecValidate"));Marshal.ThrowExceptionForHR(validate(path));if(failure==null)journal!.RemoveCache();}catch(Exception e){failure??=e;}
            // Cleanup errors must complete the public task too, otherwise the window can wait forever on Stop.
            try{journal?.Dispose();}catch(Exception e){failure??=e;}
            try{component?.Dispose();}catch(Exception e){failure??=e;}lock(gate){stopping=true;audio.Clear();latest=null;}
            if(failure!=null){finished.TrySetException(failure);}else finished.TrySetResult();
        }
    }
}







