import cn.phonecam.*;
import java.io.*;
import java.net.*;
import java.lang.management.*;
import java.util.*;
import java.util.concurrent.*;
import java.util.concurrent.atomic.*;
public final class DisconnectStressTest {
 static final AtomicReference<Throwable> failure=new AtomicReference<>();
 static void until(java.util.function.BooleanSupplier test)throws Exception{long end=System.nanoTime()+TimeUnit.SECONDS.toNanos(5);while(!test.getAsBoolean()){if(failure.get()!=null)throw new AssertionError(failure.get());if(System.nanoTime()>end)throw new AssertionError("timeout");Thread.sleep(5);}}
 public static void main(String[] args)throws Exception{
  Thread.setDefaultUncaughtExceptionHandler((t,e)->failure.compareAndSet(null,e));
  ExecutorService io=Executors.newCachedThreadPool();Set<Socket> sockets=ConcurrentHashMap.newKeySet();AtomicInteger audio=new AtomicInteger(),callbacks=new AtomicInteger();
  try(ServerSocket server=new ServerSocket(0,50,InetAddress.getByName("127.0.0.1"))){
   io.execute(()->{try{while(!server.isClosed()){Socket s=server.accept();sockets.add(s);io.execute(()->{try(Socket socket=s){DataInputStream in=new DataInputStream(socket.getInputStream());DataOutputStream out=new DataOutputStream(socket.getOutputStream());byte[] auth=new byte[16];in.readFully(auth);out.write(1);out.flush();byte[] hello=new byte[in.readUnsignedShort()];in.readFully(hello);boolean sound=new String(hello,"UTF-8").contains("\n@audio:");if(sound){audio.incrementAndGet();out.write(1);}else out.write("PCAMA002FEDCBA9876543210".getBytes("US-ASCII"));out.flush();while(in.read()!=-1){}}catch(IOException expected){}finally{sockets.remove(s);}});}}catch(IOException expected){}});
   String id=UUID.randomUUID().toString();PairingInfo info=PairingInfo.parse("phonecam://pair?v=2&hosts=127.0.0.1&port="+server.getLocalPort()+"&code=0123456789ABCDEF&id="+id+"&name=test");
   try(ConnectionHub hub=new ConnectionHub(UUID.randomUUID().toString(),"disconnect test",s->callbacks.incrementAndGet())){
    for(int i=0;i<100;i++)hub.disconnectAll();
    for(int i=0;i<40;i++){hub.connect(info);until(()->hub.connectedCount()==1);if((i&1)==0){int before=audio.get();hub.setAudio(id,true);until(()->audio.get()>before);}if((i&2)==0)hub.disconnect(id);else hub.disconnectAll();hub.disconnect(id);hub.disconnectAll();until(()->!hub.hasTargets()&&!hub.hasAudioTargets());Thread.sleep(15);}
    until(()->sockets.isEmpty());if(ManagementFactory.getThreadMXBean().findDeadlockedThreads()!=null)throw new AssertionError("deadlock");
   }
   if(failure.get()!=null)throw new AssertionError(failure.get());
   System.out.println("PASS 100 empty disconnect-all calls; 40 real TCP connect/disconnect cycles; single/all/repeated disconnect; 20 audio-enabled sessions; no uncaught exceptions or Java monitor deadlocks; callbacks="+callbacks.get());
  }finally{for(Socket socket:sockets)socket.close();io.shutdownNow();}
 }
}
