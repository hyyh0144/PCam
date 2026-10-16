package android.media;
import java.util.concurrent.*;
import java.util.concurrent.atomic.*;
// Test double only; never compiled into the APK. It exercises application failure/cleanup paths, not Android audio.
public final class AudioRecord {
 public static final int STATE_INITIALIZED=1,RECORDSTATE_RECORDING=3,READ_BLOCKING=0;
 public static volatile AudioRecord last;
 public static volatile boolean invalidState,failStart,failRelease;
 public static volatile int minimum=960;
 public final CountDownLatch reading=new CountDownLatch(1),stopped=new CountDownLatch(1);
 public final AtomicInteger releases=new AtomicInteger();
 public static int getMinBufferSize(int rate,int channels,int encoding){return minimum;}
 public AudioRecord(int source,int rate,int channels,int encoding,int size){last=this;}
 public int getState(){return invalidState?0:STATE_INITIALIZED;}
 public void startRecording(){if(failStart)throw new IllegalStateException("test start failure");}
 public int getRecordingState(){return RECORDSTATE_RECORDING;}
 public int read(short[] bytes,int offset,int size,int mode){reading.countDown();try{stopped.await();}catch(InterruptedException e){Thread.currentThread().interrupt();}return -1;}
 public void stop(){stopped.countDown();}
 public void release(){releases.incrementAndGet();if(failRelease)throw new IllegalStateException("test release failure");}
}
