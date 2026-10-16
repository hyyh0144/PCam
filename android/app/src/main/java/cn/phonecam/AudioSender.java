// 每台电脑独立音频连接，最多保留 100ms 音频，慢端不阻塞视频。
package cn.phonecam;
import java.io.*;
import java.net.*;
import java.nio.charset.StandardCharsets;
import java.util.concurrent.*;
final class AudioSender implements AutoCloseable,Runnable {
    interface Changed { void changed(AudioSender sender,String error); }
    static final int BYTES=960; // 24kHz / 单声道 / PCM16 / 20ms
    private final String host,code,hello;
    private final int port;
    private final Changed changed;
    private final java.util.function.IntConsumer sent;
    private final ArrayBlockingQueue<Packet> queue=new ArrayBlockingQueue<>(5);
    private volatile Socket socket;
    volatile boolean closed,active;
    volatile long writeStarted;
    private static final class Packet { final long time; final byte[] data; Packet(long t,byte[] d){time=t;data=d;} }
    AudioSender(String host,int port,String code,String deviceId,String token,java.util.function.IntConsumer sent,Changed changed) {
        this.host=host;this.port=port;this.code=code;hello=deviceId+"\n@audio:"+token;this.changed=changed;this.sent=sent;
    }
    void offer(long time,byte[] pcm) {
        if(!active || closed || pcm.length!=BYTES) return;
        Packet packet=new Packet(time,pcm); if(!queue.offer(packet)) { queue.poll();queue.offer(packet); }
    }
    @Override public void run() {
        String error="";
        try {
            Socket candidate=new Socket(); socket=candidate;
            if(closed){candidate.close();return;}
            candidate.setSendBufferSize(8192);candidate.setTcpNoDelay(true);candidate.setSoTimeout(2000);
            candidate.connect(new InetSocketAddress(host,port),1500);
            DataOutputStream out=new DataOutputStream(candidate.getOutputStream());
            DataInputStream in=new DataInputStream(candidate.getInputStream());
            out.write(code.getBytes(StandardCharsets.US_ASCII));out.flush();
            if(in.read()!=1) throw new IOException("配对失效");
            byte[] bytes=hello.getBytes(StandardCharsets.UTF_8);out.writeShort(bytes.length);out.write(bytes);out.flush();
            int accepted=in.read();
            if(accepted==2){error="声音无法开启：电脑已关闭手机声音";return;}
            if(accepted!=1) throw new IOException("电脑拒绝音频连接");
            if(closed)return;
            active=true;changed.changed(this,"");
            candidate.setSoTimeout(0);
            Thread monitor=new Thread(() -> {try{in.read();}catch(IOException ignored){} close();},"PCam-audio-close");
            monitor.setDaemon(true);monitor.start();
            while(!closed) {
                Packet packet=queue.poll(250,TimeUnit.MILLISECONDS);if(packet==null)continue;
                // 积压太久的语音直接跳过，避免恢复后播放旧声音。
                if(System.nanoTime()/1000-packet.time>250000)continue;
                writeStarted=System.nanoTime();out.writeInt(BYTES);out.writeLong(packet.time);out.write(packet.data);out.flush();sent.accept(BYTES+12);writeStarted=0;
            }
        } catch(Exception e) { if(!closed)error="声音连接中断，请重新开启"; }
        finally { close();changed.changed(this,error); }
    }
    @Override public void close() {closed=true;active=false;queue.clear();Socket s=socket;if(s!=null)try{s.close();}catch(IOException ignored){} }
}
