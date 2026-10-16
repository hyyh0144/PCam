// 只在用户开启声音后创建麦克风，一次采集分发给选中的电脑。
package cn.phonecam;
import android.media.*;
final class AudioCapture implements AutoCloseable {
    interface Sink { void frame(long time,byte[] pcm); void failed(); }
    private final AudioRecord record;
    private final Sink sink;
    private volatile boolean closed;
    private final Thread thread;
    AudioCapture(Sink sink) {
        this.sink=sink;
        int minimum=AudioRecord.getMinBufferSize(24000,AudioFormat.CHANNEL_IN_MONO,AudioFormat.ENCODING_PCM_16BIT);
        if(minimum<=0)throw new IllegalStateException("不支持音频采样格式");
        record=new AudioRecord(MediaRecorder.AudioSource.MIC,24000,AudioFormat.CHANNEL_IN_MONO,AudioFormat.ENCODING_PCM_16BIT,Math.max(minimum,960*8));
        if(record.getState()!=AudioRecord.STATE_INITIALIZED) {releaseRecord();throw new IllegalStateException("麦克风初始化失败");}
        try { record.startRecording();if(record.getRecordingState()!=AudioRecord.RECORDSTATE_RECORDING)throw new IllegalStateException("麦克风启动失败"); }
        catch(RuntimeException e){releaseRecord();throw e;}
        thread=new Thread(this::read,"PCam-audio");
        try{thread.start();}catch(RuntimeException error){releaseRecord();throw error;}
    }
    private void read() {
        short[] samples=new short[480];
        try {
            android.os.Process.setThreadPriority(android.os.Process.THREAD_PRIORITY_AUDIO);
            while(!closed) {
                int count=0;
                while(count<samples.length && !closed) {
                    int n=record.read(samples,count,samples.length-count,AudioRecord.READ_BLOCKING);
                    if(n<=0)throw new IllegalStateException("麦克风读取失败");count+=n;
                }
                if(closed)break;
                byte[] pcm=new byte[960];for(int i=0;i<samples.length;i++){pcm[i*2]=(byte)samples[i];pcm[i*2+1]=(byte)(samples[i]>>8);}
                sink.frame(System.nanoTime()/1000,pcm);
            }
        } catch(RuntimeException e) {if(!closed)sink.failed();}
        finally {try{record.stop();}catch(RuntimeException ignored){}releaseRecord();}
    }
    // Release failures must not replace the original start/read error or escape the worker's finally block.
    private void releaseRecord(){try{record.release();}catch(RuntimeException error){Diagnostics.write("microphone release failed: "+error.getClass().getSimpleName());}}
    @Override public void close() {closed=true;try{record.stop();}catch(RuntimeException ignored){} }
}
