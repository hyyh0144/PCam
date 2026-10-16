package cn.phonecam;
import android.media.AudioRecord;
import java.util.concurrent.*;
import java.util.concurrent.atomic.*;
public final class AudioCaptureLifecycleTest {
 static final AtomicReference<Throwable> uncaught=new AtomicReference<>();
 static void reset(){AudioRecord.last=null;AudioRecord.invalidState=AudioRecord.failStart=AudioRecord.failRelease=false;AudioRecord.minimum=960;android.os.Process.fail=false;Diagnostics.errors.set(0);}
 static void join(AudioCapture capture)throws Exception{java.lang.reflect.Field f=AudioCapture.class.getDeclaredField("thread");f.setAccessible(true);Thread thread=(Thread)f.get(capture);thread.join(2000);if(thread.isAlive()||uncaught.get()!=null)throw new AssertionError("worker did not exit cleanly",uncaught.get());}
 static AudioCapture.Sink sink(AtomicInteger failures){return new AudioCapture.Sink(){public void frame(long time,byte[] bytes){}public void failed(){failures.incrementAndGet();}};}
 public static void main(String[] args)throws Exception{
  Thread.setDefaultUncaughtExceptionHandler((thread,error)->uncaught.compareAndSet(null,error));AtomicInteger failures=new AtomicInteger();
  reset();AudioCapture normal=new AudioCapture(sink(failures));if(!AudioRecord.last.reading.await(2,TimeUnit.SECONDS))throw new AssertionError("read not started");normal.close();normal.close();join(normal);if(AudioRecord.last.releases.get()!=1||failures.get()!=0)throw new AssertionError("normal close failed");
  reset();android.os.Process.fail=true;AudioCapture priority=new AudioCapture(sink(failures));join(priority);if(AudioRecord.last.releases.get()!=1||failures.get()!=1)throw new AssertionError("priority failure leaked recorder");
  reset();AudioRecord.failRelease=true;AudioCapture release=new AudioCapture(sink(failures));if(!AudioRecord.last.reading.await(2,TimeUnit.SECONDS))throw new AssertionError("read not started");release.close();join(release);if(AudioRecord.last.releases.get()!=1||Diagnostics.errors.get()!=1)throw new AssertionError("release error escaped or not recorded");
  reset();AudioRecord.failStart=true;boolean rejected=false;try{new AudioCapture(sink(failures));}catch(IllegalStateException expected){rejected=true;}if(!rejected||AudioRecord.last.releases.get()!=1)throw new AssertionError("start failure leaked recorder");
  reset();AudioRecord.invalidState=true;rejected=false;try{new AudioCapture(sink(failures));}catch(IllegalStateException expected){rejected=true;}if(!rejected||AudioRecord.last.releases.get()!=1)throw new AssertionError("invalid state leaked recorder");
  reset();AudioRecord.minimum=-1;rejected=false;try{new AudioCapture(sink(failures));}catch(IllegalStateException expected){rejected=true;}if(!rejected||AudioRecord.last!=null)throw new AssertionError("invalid format accepted");
  System.out.println("PASS AudioCapture test doubles: repeated close, priority failure, release failure, start failure, invalid state/format; resources released, no uncaught worker exception");
 }
}
final class Diagnostics { static final AtomicInteger errors=new AtomicInteger();static void write(String text){errors.incrementAndGet();} }
