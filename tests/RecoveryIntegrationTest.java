// 跨语言复用手机实际连接代码；断网后保留麦克风开关，手动断开与失效二维码不重连。
import cn.phonecam.*;
import java.nio.file.*;
import java.nio.charset.StandardCharsets;
import java.util.concurrent.*;
import java.util.concurrent.atomic.AtomicBoolean;
public final class RecoveryIntegrationTest {
    static void until(java.util.function.BooleanSupplier test) throws Exception {long deadline=System.nanoTime()+TimeUnit.SECONDS.toNanos(25);while(!test.getAsBoolean()){if(System.nanoTime()>deadline)throw new AssertionError("recovery timeout");Thread.sleep(20);}}
    public static void main(String[] args) throws Exception {
        Path root=Paths.get(args[0]);PairingInfo info=PairingInfo.parse(new String(Files.readAllBytes(root.resolve("a.txt")),StandardCharsets.UTF_8));
        ScheduledExecutorService feed=Executors.newSingleThreadScheduledExecutor();AtomicBoolean paused=new AtomicBoolean();
        try(ConnectionHub hub=new ConnectionHub(java.util.UUID.randomUUID().toString(),"恢复测试手机",s -> {},System.out::println)){
            hub.connect(info);until(()->hub.connectedCount()==1);hub.setAudio(info.id,true);
            feed.scheduleAtFixedRate(()->{if(!paused.get())hub.broadcast(new byte[]{(byte)255,(byte)216,(byte)255,(byte)217});hub.broadcastAudio(System.nanoTime()/1000,new byte[960]);},0,20,TimeUnit.MILLISECONDS);
            until(()->Files.exists(root.resolve("transport-dropped")));until(()->Files.exists(root.resolve("reconnected")));
            if(!hub.audioEnabled(info.id))throw new AssertionError("microphone intent lost on transport recovery");
            paused.set(true);Files.write(root.resolve("pause-video"),new byte[]{1});until(()->Files.exists(root.resolve("pause-passed")));
            paused.set(false);until(()->Files.exists(root.resolve("resume-passed")));
            hub.disconnect(info.id);Files.write(root.resolve("manual-stop"),new byte[]{1});until(()->Files.exists(root.resolve("manual-passed")));
            if(hub.hasTargets()||hub.hasAudioTargets())throw new AssertionError("manual stop retained target");
            hub.connect(info);until(()->Files.exists(root.resolve("pairing-reset")));until(()->!hub.hasTargets());
            if(hub.hasAudioTargets())throw new AssertionError("revoked pairing retained microphone");
            Files.write(root.resolve("java-passed"),new byte[]{1});
        }finally{feed.shutdownNow();}
        System.out.println("PASS: Java reconnect, heartbeat, original microphone state, manual stop and expired-pairing cleanup");
    }
}
