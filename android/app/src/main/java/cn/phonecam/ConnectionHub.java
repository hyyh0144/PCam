// 短暂断网保留目标并限时重试；心跳与视频分离，麦克风开关意图独立保存。
package cn.phonecam;
import java.io.*;
import java.net.*;
import java.nio.charset.StandardCharsets;
import java.util.*;
import java.util.concurrent.*;
import java.util.function.Consumer;
public final class ConnectionHub implements AutoCloseable {
    public interface Listener { void changed(String message); }
    public static final int MAX_CONNECTIONS=8;
    // Use the interface's Set-returning keySet signature. Desktop javac otherwise binds
    // ConcurrentHashMap.keySet():KeySetView, which Android 8/9 cannot resolve at runtime.
    private final ConcurrentMap<String,Link> links=new ConcurrentHashMap<>();
    private final ExecutorService audioSenders=Executors.newFixedThreadPool(MAX_CONNECTIONS),senders=Executors.newFixedThreadPool(MAX_CONNECTIONS);
    private final ScheduledExecutorService watchdog=Executors.newSingleThreadScheduledExecutor();
    private final String deviceId;
    private final Listener listener;
    private final Consumer<String> log;
    private final byte[] hello;
    private final RateMeter traffic=new RateMeter();
    private volatile boolean hubClosed;
    private static final long RECOVERY_MS=120000;
    public double[] sampleTraffic(long now){return traffic.sample(now);}
    public ConnectionHub(String id,String name,Listener listener){this(id,name,listener,s -> {});}
    public ConnectionHub(String id,String name,Listener listener,Consumer<String> log){
        this.deviceId=id;this.listener=listener;this.log=log;
        hello=(id+"\n@video2:"+name).getBytes(StandardCharsets.UTF_8);
        if(hello.length>512)throw new IllegalArgumentException("设备名称过长");
        watchdog.scheduleAtFixedRate(() -> {
            if(hubClosed)return;
            long now=System.nanoTime();
            for(Link link:links.values()){
                if(link.writeStarted!=0 && now-link.writeStarted>TimeUnit.SECONDS.toNanos(5)){log.accept("video write stalled; retry transport");link.dropTransport();}
                AudioSender audio=link.audio;
                if(audio!=null && audio.writeStarted!=0 && now-audio.writeStarted>TimeUnit.SECONDS.toNanos(2))audio.close();
                if(link.audioWanted && link.audio==null && link.active && now>=link.nextAudioRetry)startAudio(link);
            }
        },1,1,TimeUnit.SECONDS);
    }
    public synchronized void connect(PairingInfo info){
        if(hubClosed)return;
        if(links.containsKey(info.id)){changed("这台电脑已在连接列表中");return;}
        if(links.size()>=MAX_CONNECTIONS){changed("最多同时连接八台电脑");return;}
        Link link=new Link(info);links.put(info.id,link);senders.execute(link);changed("正在连接 "+info.name+"…");
    }
    public int connectedCount(){int count=0;for(Link link:links.values())if(link.active&&!link.closed)count++;return count;}
    public boolean hasTargets(){return !links.isEmpty();}
    public List<String[]> targets(){
        List<String[]> result=new ArrayList<>();
        for(Link link:links.values()){AudioSender audio=link.audio;result.add(new String[]{link.info.id,link.info.name,link.active?link.remoteIp:(link.everConnected?"正在重连…":link.remoteIp),!link.audioWanted?"off":(audio!=null&&audio.active?"on":"starting")});}
        return result;
    }
    public boolean hasAudioTargets(){for(Link link:links.values())if(link.audioWanted&&!link.closed)return true;return false;}
    public boolean audioReady(String id){Link link=links.get(id);return link!=null&&link.active&&!link.closed&&!link.audioToken.isEmpty();}
    public boolean audioEnabled(String id){Link link=links.get(id);return link!=null&&link.audioWanted;}
    public synchronized void setAudio(String id,boolean enabled){
        if(hubClosed)return;
        Link link=links.get(id);if(link==null||link.closed)return;
        if(enabled&&!audioReady(id)){changed("声音无法开启，请更新电脑端后重新扫码");return;}
        link.audioWanted=enabled;link.audioFailures=0;link.nextAudioRetry=0;
        if(!enabled){AudioSender old=link.audio;link.audio=null;if(old!=null)old.close();}
        else startAudio(link);
        changed("");
    }
    private synchronized void startAudio(Link link){synchronized(link){
        if(hubClosed||link.closed||!link.active||!link.audioWanted||link.audio!=null||link.audioToken.isEmpty())return;
        if(link.audioFailures>=8){link.audioWanted=false;log.accept("audio recovery exhausted");changed("声音恢复失败，请重新开启声音");return;}
        AudioSender audio=new AudioSender(link.remoteIp,link.info.port,link.info.code,deviceId,link.audioToken,count -> traffic.add(count,0),(sender,error) -> {
            synchronized(ConnectionHub.this){
                if(link.audio!=sender)return;
                if(sender.closed){link.audio=null;link.audioFailures++;link.nextAudioRetry=System.nanoTime()+TimeUnit.SECONDS.toNanos(Math.min(8,link.audioFailures));}
                else link.audioFailures=0;
            }
            if(!error.isEmpty())log.accept("audio transport closed; retry pending");
            changed("");
        });
        link.audio=audio;audioSenders.execute(audio);
    }}
    public synchronized void disableAudio(){for(String id:links.keySet())setAudio(id,false);}
    public void broadcastAudio(long time,byte[] pcm){for(Link link:links.values()){AudioSender audio=link.audio;if(audio!=null)audio.offer(time,pcm);}}
    public void broadcast(byte[] jpeg){for(Link link:links.values())if(link.active&&!link.closed){link.frames.poll();link.frames.offer(jpeg);}}
    public synchronized void disconnect(String id){Link link=links.remove(id);if(link!=null)link.stop();changed("已断开电脑");}
    public synchronized void disconnectAll(){for(String id:new ArrayList<>(links.keySet())){Link link=links.remove(id);if(link!=null)link.stop();}changed("已断开所有电脑");}
    @Override public synchronized void close(){
        if(hubClosed)return;
        // Share the submission lock: close and connect/startAudio must never race executor shutdown.
        hubClosed=true;disconnectAll();senders.shutdownNow();audioSenders.shutdownNow();watchdog.shutdownNow();
    }
    private void changed(String message){if(!hubClosed)listener.changed(message);}
    static String readGrant(InputStream input)throws IOException{
        byte[] grant=new byte[24];int received=0;
        try{
            while(received<grant.length){int count=input.read(grant,received,grant.length-received);if(count<0)throw new EOFException("Incomplete video grant");received+=count;}
        }catch(SocketTimeoutException timeout){
            // A legacy peer sends no grant. A partial response is a broken new handshake, not legacy.
            if(received==0)return "";throw new IOException("Incomplete video grant",timeout);
        }
        String result=new String(grant,StandardCharsets.US_ASCII);
        if(!(result.startsWith("PCAMA001")||result.startsWith("PCAMA002"))||!result.substring(8).matches("[0-9A-F]{16}"))throw new IOException("Invalid video grant");
        return result;
    }
    private static final class PairingExpired extends IOException {}
    private final class Link implements Runnable {
        final PairingInfo info;
        final ArrayBlockingQueue<byte[]> frames=new ArrayBlockingQueue<>(1);
        volatile Socket socket;
        volatile String remoteIp="",audioToken="";
        volatile AudioSender audio;
        volatile boolean closed,active,everConnected,audioWanted;
        volatile long writeStarted,nextAudioRetry;
        int audioFailures;
        Link(PairingInfo info){this.info=info;}
        synchronized void dropTransport(){
            active=false;writeStarted=0;audioToken="";frames.clear();
            AudioSender old=audio;audio=null;if(old!=null)old.close();
            Socket s=socket;socket=null;if(s!=null)try{s.close();}catch(IOException ignored){}
        }
        void stop(){closed=true;audioWanted=false;dropTransport();}
        @Override public void run(){
            String result=info.name+" 已断开";long failedSince=0;int attempt=0;
            try {
                while(!closed){
                    try {
                        DataOutputStream output=null;boolean heartbeat=false;
                        for(String host:info.hosts){
                            if(closed)return;
                            Socket candidate=new Socket();socket=candidate;
                            try {
                                if(closed){candidate.close();return;}
                                candidate.setSendBufferSize(128*1024);candidate.setKeepAlive(true);candidate.setTcpNoDelay(true);candidate.setSoTimeout(2000);
                                candidate.connect(new InetSocketAddress(host,info.port),1500);
                                DataOutputStream stream=new DataOutputStream(candidate.getOutputStream());DataInputStream input=new DataInputStream(candidate.getInputStream());
                                stream.write(info.code.getBytes(StandardCharsets.US_ASCII));stream.flush();int accepted=input.read();
                                if(accepted==0)throw new PairingExpired();if(accepted!=1)throw new EOFException();
                                stream.writeShort(hello.length);stream.write(hello);stream.flush();
                                candidate.setSoTimeout(1000);String grant=readGrant(input);
                                heartbeat=grant.startsWith("PCAMA002");audioToken=grant.isEmpty()?"":grant.substring(8);
                                candidate.setSoTimeout(0);remoteIp=candidate.getInetAddress().getHostAddress();output=stream;
                                Thread monitor=new Thread(() -> {try{input.read();}catch(IOException ignored){} synchronized(Link.this){if(socket==candidate)dropTransport();}},"PCam-video-close");monitor.setDaemon(true);monitor.start();
                                break;
                            }catch(PairingExpired expired){candidate.close();throw expired;}
                            catch(IOException error){candidate.close();}
                        }
                        if(output==null)throw new IOException("connect failed");
                        if(closed)return;
                        // 监视线程可能已经观察到远端关闭，不能重新标记为在线。
                        synchronized(this){if(socket==null||socket.isClosed())throw new EOFException();active=true;}
                        everConnected=true;attempt=0;log.accept("video connected; heartbeat="+heartbeat);changed("已连接 "+info.name);
                        startAudio(this);long sentAt=System.nanoTime();
                        while(!closed&&active){
                            byte[] jpeg=frames.poll(500,TimeUnit.MILLISECONDS);
                            if(jpeg==null&&(!heartbeat||System.nanoTime()-sentAt<TimeUnit.SECONDS.toNanos(2)))continue;
                            writeStarted=System.nanoTime();output.writeInt(jpeg==null?0:jpeg.length);if(jpeg!=null)output.write(jpeg);output.flush();
                            traffic.add(jpeg==null?4:jpeg.length+4,jpeg==null?0:1);writeStarted=0;sentAt=System.nanoTime();failedSince=0;
                        }
                        if(!closed)throw new EOFException();
                    }catch(PairingExpired expired){result="二维码已失效，请重新扫码";log.accept("pairing revoked; no retry");break;}
                    catch(Exception error){
                        if(closed||error instanceof InterruptedException)break;
                        dropTransport();log.accept("video transport failed: "+error.getClass().getSimpleName());
                        if(!everConnected){result="无法连接 "+info.name+"，请重新扫码";break;}
                        if(failedSince==0)failedSince=System.nanoTime();
                        if(TimeUnit.NANOSECONDS.toMillis(System.nanoTime()-failedSince)>=RECOVERY_MS){result="连接恢复超时，请重新扫码";break;}
                        changed("正在恢复 "+info.name+" 的连接…");
                        int delay=Math.min(8,1<<Math.min(attempt++,3));
                        for(int i=0;i<delay*10&&!closed;i++)Thread.sleep(100);
                    }
                }
            }catch(InterruptedException ignored){Thread.currentThread().interrupt();}
            finally{stop();links.remove(info.id,this);log.accept("video target removed");changed(result);}
        }
    }
}

