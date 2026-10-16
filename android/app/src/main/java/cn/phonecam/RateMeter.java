// 应用层字节/帧增量除以实际经过时间，不使用假定的一秒。
package cn.phonecam;
public final class RateMeter {
    private long bytes,frames,last;
    public RateMeter() { this(System.nanoTime()); }
    public RateMeter(long startNanos) { last=startNanos; }
    public synchronized void add(long byteCount,long frameCount) { bytes+=byteCount; frames+=frameCount; }
    public synchronized double[] sample(long now) {
        if(now<=last) return new double[]{0,0};
        double seconds=(now-last)/1_000_000_000.0;
        double[] result={bytes*8.0/seconds/1_000_000.0,frames/seconds};
        bytes=0; frames=0; last=now; return result;
    }
}
