// 跨语言真实音频发送，验证默认关闭和两台电脑独立开关。
import cn.phonecam.*;
import java.nio.file.*;
import java.nio.charset.StandardCharsets;
import java.util.concurrent.*;
public final class AudioIntegrationTest {
    static void until(java.util.function.BooleanSupplier check) throws Exception {
        long end=System.nanoTime()+TimeUnit.SECONDS.toNanos(12);
        while(!check.getAsBoolean()){if(System.nanoTime()>end)throw new AssertionError("audio integration timeout");Thread.sleep(20);}
    }
    public static void main(String[] args) throws Exception {
        Path root=Paths.get(args[0]);
        PairingInfo a=PairingInfo.parse(new String(Files.readAllBytes(root.resolve("a.txt")),StandardCharsets.UTF_8));
        PairingInfo b=PairingInfo.parse(new String(Files.readAllBytes(root.resolve("b.txt")),StandardCharsets.UTF_8));
        ConnectionHub hub=new ConnectionHub(java.util.UUID.randomUUID().toString(),"Audio test",message -> {});
        ScheduledExecutorService feed=Executors.newSingleThreadScheduledExecutor();
        try {
            hub.connect(a);hub.connect(b);until(()->hub.connectedCount()==2);
            if(hub.hasAudioTargets())throw new AssertionError("audio must start off");
            byte[] pcm=new byte[960];for(int i=0;i<pcm.length;i++)pcm[i]=(byte)(i%251);
            feed.scheduleAtFixedRate(()->{hub.broadcast(new byte[]{(byte)255,(byte)216,(byte)255,(byte)217});hub.broadcastAudio(System.nanoTime()/1000,pcm);},0,20,TimeUnit.MILLISECONDS);
            until(()->Files.exists(root.resolve("video-only")));
            hub.setAudio(a.id,true);until(()->Files.exists(root.resolve("a-only")));
            hub.setAudio(a.id,false);Files.write(root.resolve("muted-a"),new byte[]{1});
            until(()->Files.exists(root.resolve("a-muted")));
            hub.setAudio(b.id,true);until(()->Files.exists(root.resolve("b-only")));
            until(()->Files.exists(root.resolve("desktop-muted")));Thread.sleep(350);
            if(!hub.audioEnabled(b.id))throw new AssertionError("phone audio disconnected unexpectedly");
            hub.setAudio(b.id,false);until(()->!hub.hasAudioTargets());
            Files.write(root.resolve("java-passed"),new byte[]{1});
            System.out.println("PASS: real Java audio sender -> C# receiver, default mute, per-PC toggle, video continues");
        } finally {feed.shutdownNow();hub.close();}
    }
}
