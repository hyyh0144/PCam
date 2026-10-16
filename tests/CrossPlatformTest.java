// 读取 C# 生成的二维码，复用 Android 的解析、扫码和多端发送代码。
import cn.phonecam.*;
import java.awt.image.BufferedImage;
import javax.imageio.ImageIO;
import java.nio.file.*;
import java.util.UUID;
import java.util.function.BooleanSupplier;
import java.net.*;
import java.io.*;
import java.util.concurrent.CountDownLatch;
public final class CrossPlatformTest {
    static void waitFor(BooleanSupplier condition) throws Exception {
        long deadline=System.currentTimeMillis()+12000;
        while(!condition.getAsBoolean()) { if(System.currentTimeMillis()>deadline) throw new AssertionError("等待超时"); Thread.sleep(40); }
    }
    static PairingInfo read(Path path) throws Exception {
        BufferedImage image=ImageIO.read(path.toFile()); int w=image.getWidth(),h=image.getHeight();
        byte[] y=new byte[w*h];
        for(int j=0;j<h;j++) for(int i=0;i<w;i++) { int rgb=image.getRGB(i,j); y[j*w+i]=(byte)(((rgb>>16&255)*299+(rgb>>8&255)*587+(rgb&255)*114)/1000); }
        return PairingInfo.parse(QrDecoder.decode(y,w,h));
    }
    public static void main(String[] args) throws Exception {
        Path root=Paths.get(args[0]); PairingInfo a=read(root.resolve("a.png")), b=read(root.resolve("b.png"));
        if(a.port==b.port || a.port==8080 || !a.name.equals("测试电脑 A")) throw new AssertionError("二维码内容错误");
        String valid=new String(Files.readAllBytes(root.resolve("a.txt")),"UTF-8");
        String[] invalid={"https://example.com/",valid.replace("v=2","v=1"),valid.replace("port="+a.port,"port=0"),valid.replace("port="+a.port,"port=65536"),valid+"&v=2",valid.replace("127.0.0.1","999.0.0.1"),valid.replace("code="+a.code,"code=bad"),valid+"#fragment"};
        for(String text:invalid) { try { PairingInfo.parse(text); throw new AssertionError("接受了非法二维码"); } catch(IllegalArgumentException expected) {} }
        byte[] frame={(byte)255,(byte)216,0,(byte)255,(byte)217};
        CountDownLatch releaseSlow=new CountDownLatch(1);
        try(ServerSocket slowServer=new ServerSocket(0,1,InetAddress.getByName("127.0.0.1"));
            ConnectionHub phone1=new ConnectionHub(UUID.randomUUID().toString(),"测试手机一",s -> {});
            ConnectionHub phone2=new ConnectionHub(UUID.randomUUID().toString(),"测试手机二",s -> {})) {
            Thread slowThread=new Thread(() -> {
                try(Socket socket=slowServer.accept()) {
                    socket.setReceiveBufferSize(1024);
                    DataInputStream input=new DataInputStream(socket.getInputStream()); byte[] auth=new byte[16]; input.readFully(auth);
                    socket.getOutputStream().write(1);
                    byte[] hello=new byte[input.readUnsignedShort()]; input.readFully(hello);
                    releaseSlow.await(); // 故意不接收视频，验证其他目标不受影响。
                } catch(Exception ignored) {}
            }); slowThread.setDaemon(true); slowThread.start();
            phone1.connect(a); phone1.connect(b); phone2.connect(a);
            waitFor(() -> phone1.connectedCount()==2 && phone2.connectedCount()==1);
            phone1.connect(a); // 重复扫码不能新增重复连接。
            if(phone1.targets().size()!=2) throw new AssertionError("重复连接");
            for(int i=0;i<10;i++) { phone1.broadcast(frame); phone2.broadcast(frame); Thread.sleep(60); }
            waitFor(() -> Files.exists(root.resolve("both-passed")));
            PairingInfo slow=PairingInfo.parse(valid.replace("port="+a.port,"port="+slowServer.getLocalPort()).replace(a.id,UUID.randomUUID().toString()));
            phone1.connect(slow); waitFor(() -> phone1.connectedCount()==3);
            byte[] large=new byte[1024*1024]; large[0]=(byte)255; large[1]=(byte)216; large[large.length-2]=(byte)255; large[large.length-1]=(byte)217;
            for(int i=0;i<130;i++) { phone1.broadcast(large); phone2.broadcast(frame); Thread.sleep(50); }
            waitFor(() -> phone1.connectedCount()==2);
            phone1.disconnect(slow.id); // 新版保留慢端供重试，明确停止后不应再尝试连接。
            phone1.disconnect(a.id); phone2.disconnectAll();
            for(int i=0;i<10;i++) { phone1.broadcast(frame); Thread.sleep(60); }
            if(phone1.connectedCount()!=1) throw new AssertionError("断开 A 影响 B");
            Files.write(root.resolve("java-passed"),new byte[]{1});
        } finally { releaseSlow.countDown(); }
        System.out.println("PASS：C#二维码→Android识别解析、非法码拒绝、双向多端、重复扫码去重、慢端超时隔离、单路断开隔离");
    }
}
