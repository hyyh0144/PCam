package cn.phonecam;
import java.io.*;
import java.net.*;
import java.util.*;
import java.util.concurrent.*;
import java.util.concurrent.atomic.*;
public final class ConnectionBoundaryTest {
 static PairingInfo info(){return PairingInfo.parse("phonecam://pair?v=2&hosts=127.0.0.1&port=1&code=0123456789ABCDEF&id="+UUID.randomUUID()+"&name=test");}
 static InputStream response(String text,boolean timeout){return new InputStream(){final byte[] bytes=text.getBytes(java.nio.charset.StandardCharsets.US_ASCII);int position;
  public int read()throws IOException{byte[] one=new byte[1];return read(one,0,1)<0?-1:one[0]&255;}
  public int read(byte[] b,int offset,int length)throws IOException{if(position==bytes.length){if(timeout)throw new SocketTimeoutException();return -1;}b[offset]=bytes[position++];return 1;}
 };}
 static void invalid(String text,boolean timeout)throws Exception{boolean failed=false;try{ConnectionHub.readGrant(response(text,timeout));}catch(IOException expected){failed=true;}if(!failed)throw new AssertionError("invalid/partial grant accepted");}
 static void brokenPeer(String grant)throws Exception{
  AtomicBoolean online=new AtomicBoolean();AtomicReference<ConnectionHub> holder=new AtomicReference<>();AtomicReference<Throwable> failure=new AtomicReference<>();
  try(ServerSocket server=new ServerSocket(0,1,InetAddress.getByName("127.0.0.1"))){
   Thread peer=new Thread(()->{try(Socket socket=server.accept()){
    socket.setSoTimeout(3000);DataInputStream in=new DataInputStream(socket.getInputStream());DataOutputStream out=new DataOutputStream(socket.getOutputStream());byte[] auth=new byte[16];in.readFully(auth);out.write(1);out.flush();byte[] hello=new byte[in.readUnsignedShort()];in.readFully(hello);
    out.write(grant.getBytes(java.nio.charset.StandardCharsets.US_ASCII));out.flush();Thread.sleep(1400);
   }catch(Throwable error){failure.set(error);}});peer.setDaemon(true);peer.start();
   try(ConnectionHub hub=new ConnectionHub(UUID.randomUUID().toString(),"bad peer",message->{ConnectionHub current=holder.get();if(current!=null&&current.connectedCount()>0)online.set(true);})){
    holder.set(hub);String id=UUID.randomUUID().toString();hub.connect(PairingInfo.parse("phonecam://pair?v=2&hosts=127.0.0.1&port="+server.getLocalPort()+"&code=0123456789ABCDEF&id="+id+"&name=bad"));
    peer.join(4000);long deadline=System.nanoTime()+TimeUnit.SECONDS.toNanos(2);while(hub.hasTargets()&&System.nanoTime()<deadline)Thread.sleep(10);
    if(peer.isAlive()||failure.get()!=null||online.get()||hub.hasTargets())throw new AssertionError("broken grant accepted by real transport",failure.get());
   }
  }
 }
 public static void main(String[] args)throws Exception{
  String token="0123456789ABCDEF";
  for(String version:new String[]{"PCAMA001","PCAMA002"})if(!ConnectionHub.readGrant(response(version+token,false)).equals(version+token))throw new AssertionError("fragmented grant rejected");
  if(!ConnectionHub.readGrant(response("",true)).isEmpty())throw new AssertionError("legacy timeout rejected");
  for(int i=1;i<24;i++){invalid(("PCAMA002"+token).substring(0,i),true);invalid(("PCAMA002"+token).substring(0,i),false);}
  invalid("",false);invalid("BADMAGIC"+token,false);invalid("PCAMA002"+"xxxxxxxxxxxxxxxx",false);
  System.out.println("PASS fragmented grants and legacy no-response; all 23 truncated lengths, EOF, invalid magic/token rejected");
  brokenPeer("PCAMA002");brokenPeer("BADMAGIC"+token);System.out.println("PASS real TCP partial-timeout and invalid-magic peers never become online");
  ConnectionHub closed=new ConnectionHub(UUID.randomUUID().toString(),"test",s->{});closed.close();closed.close();
  closed.connect(info());closed.setAudio("missing",true);closed.disableAudio();closed.disconnectAll();
  if(closed.hasTargets()||closed.hasAudioTargets())throw new AssertionError("closed hub retained target");
  System.out.println("PASS close is idempotent; calls after close cannot submit work or retain targets");
  for(int round=0;round<100;round++){
   ConnectionHub hub=new ConnectionHub(UUID.randomUUID().toString(),"race",s->{});CountDownLatch go=new CountDownLatch(1);AtomicReference<Throwable> failure=new AtomicReference<>();
   Thread connect=new Thread(()->{try{go.await();hub.connect(info());}catch(Throwable e){failure.set(e);}});
   Thread close=new Thread(()->{try{go.await();hub.close();}catch(Throwable e){failure.set(e);}});
   connect.start();close.start();go.countDown();connect.join(2000);close.join(2000);hub.close();
   if(connect.isAlive()||close.isAlive()||failure.get()!=null||hub.hasTargets())throw new AssertionError("connect/close race",failure.get());
  }System.out.println("PASS 100 connect/close races without leaked targets or rejected submissions");
  AtomicReference<ConnectionHub> reentrant=new AtomicReference<>();ConnectionHub callback=new ConnectionHub(UUID.randomUUID().toString(),"callback",message->reentrant.get().close());reentrant.set(callback);callback.connect(info());callback.close();if(callback.hasTargets())throw new AssertionError("callback close leaked target");
  System.out.println("PASS listener-triggered close during connect");
 }
}
