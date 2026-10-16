// 服务统一管理摄像变焦，后台保留倍率；不改变视频传输协议。
package cn.phonecam;
import android.Manifest;
import android.app.*;
import android.content.*;
import android.content.pm.PackageManager;
import android.content.pm.ServiceInfo;
import android.graphics.*;
import android.hardware.Camera;
import android.os.*;
import android.view.SurfaceHolder;
import java.io.*;
import java.util.UUID;
import java.util.concurrent.*;
import java.util.concurrent.atomic.AtomicBoolean;

@SuppressWarnings("deprecation")
public final class CameraService extends Service {
    public interface Listener { void changed(String message); }
    public final class LocalBinder extends Binder { public CameraService getService() { return CameraService.this; } }
    private final LocalBinder binder=new LocalBinder();
    private final Handler main=new Handler(Looper.getMainLooper());
    private final ExecutorService worker=Executors.newSingleThreadExecutor();
    private final AtomicBoolean busy=new AtomicBoolean();
    private final RateMeter produced=new RateMeter();
    private final ByteArrayOutputStream jpegBuffer=new ByteArrayOutputStream(128*1024);
    private ConnectionHub hub;
    private AudioCapture audioCapture;
    private int audioGeneration;
    private Listener listener;
    private Runnable zoomListener;
    private ZoomSteps zoomSteps=new ZoomSteps(null,0);
    private int zoomIndex, requestedZoom;
    private boolean zoomEnabled, zoomQueued;
    private final Runnable applyZoomTask=() -> { zoomQueued=false; applyZoom(); };
    private Camera camera;
    private SurfaceHolder previewHolder;
    private BackgroundPreview backgroundPreview;
    private PowerManager.WakeLock wakeLock;
    private android.net.wifi.WifiManager.WifiLock wifiLock;
    private long lastCameraCallback,lastPreviewRetry;
    private int previewRetries;
    private final Runnable healthCheck=new Runnable(){public void run(){
        if(destroyed)return;
        if(camera!=null && hub.hasTargets() && SystemClock.elapsedRealtime()-lastCameraCallback>5000 && SystemClock.elapsedRealtime()-lastPreviewRetry>5000){
            Diagnostics.write("camera callbacks stalled; visible="+visible+" retry="+previewRetries);
            if(previewRetries<3){previewRetries++;lastPreviewRetry=SystemClock.elapsedRealtime();recoverPreview();}
            else {closeCamera();update("摄像头恢复失败，请返回手机应用恢复画面");}
        }
        main.postDelayed(this,2000);
    }};
    private int displayRotation;
    private int cameraId, rotation=90, extraRotation, sensorOrientation=90, rawWidth=640, rawHeight=480;
    private boolean frontFacing, manualFocus, foreground, microphoneForeground;
    private String defaultFocusMode;
    private int focusGeneration;
    private boolean focusing;
    private final Runnable restoreFocusTask=() -> restoreFocus();
    public interface FocusResult { void finished(boolean success); }
    private volatile boolean scanning=true, visible, destroyed;
    private volatile int scanEpoch, rotationRevision;
    private byte[] rotatedYuv;
    private long lastFrame,lastScan,lastFocus;
    private static final String CHANNEL="pcam_camera", STOP="cn.phonecam.STOP";
    private static final int NOTIFICATION=10;
    @Override public void onCreate() {
        super.onCreate();
        Diagnostics.init(this);Diagnostics.write("service created; sdk="+Build.VERSION.SDK_INT+" batteryExempt="+getSystemService(PowerManager.class).isIgnoringBatteryOptimizations(getPackageName()));
        NotificationManager manager=getSystemService(NotificationManager.class);
        manager.createNotificationChannel(new NotificationChannel(CHANNEL,"PCam 摄像传输",NotificationManager.IMPORTANCE_LOW));
        SharedPreferences prefs=getSharedPreferences("phonecam",MODE_PRIVATE);
        int saved=prefs.getInt("pictureRotation",0); extraRotation=saved%90==0?CameraRotation.normalize(saved):0;
        String id=prefs.getString("deviceId",null);
        if(id==null) { id=UUID.randomUUID().toString(); prefs.edit().putString("deviceId",id).apply(); }
        String name=(Build.MANUFACTURER+" "+Build.MODEL).replaceAll("\\p{Cntrl}"," ");
        if(name.length()>80) name=name.substring(0,80);
        hub=new ConnectionHub(id,name,this::update,Diagnostics::write);
        wakeLock=getSystemService(PowerManager.class).newWakeLock(PowerManager.PARTIAL_WAKE_LOCK,"PCam:stream");
        wakeLock.setReferenceCounted(false); selectBackCamera();
        try{android.net.wifi.WifiManager wifi=(android.net.wifi.WifiManager)getApplicationContext().getSystemService(WIFI_SERVICE);if(wifi!=null){wifiLock=wifi.createWifiLock(android.net.wifi.WifiManager.WIFI_MODE_FULL_HIGH_PERF,"PCam:stream");wifiLock.setReferenceCounted(false);}}catch(RuntimeException error){Diagnostics.write("wifi lock unavailable: "+error.getClass().getSimpleName());}
        main.postDelayed(healthCheck,2000);
    }
    @Override public IBinder onBind(Intent intent) { return binder; }
    @Override public int onStartCommand(Intent intent,int flags,int startId) {
        if(intent!=null && STOP.equals(intent.getAction())) { stopSession(); return START_NOT_STICKY; }
        try {
            promote();
            // 若页面在绑定前已退到后台，无连接时及时释放服务。
            main.postDelayed(() -> { if(!visible && !hub.hasTargets()) stopSession(); },2000);
        } catch(RuntimeException e) { stopSession(); }
        // 被系统终止后不在后台自动重新打开摄像头。
        return START_NOT_STICKY;
    }
    private Notification notification() {
        PendingIntent open=PendingIntent.getActivity(this,0,new Intent(this,MainActivity.class).addFlags(Intent.FLAG_ACTIVITY_SINGLE_TOP),PendingIntent.FLAG_UPDATE_CURRENT|PendingIntent.FLAG_IMMUTABLE);
        PendingIntent stop=PendingIntent.getService(this,1,new Intent(this,CameraService.class).setAction(STOP),PendingIntent.FLAG_UPDATE_CURRENT|PendingIntent.FLAG_IMMUTABLE);
        String text=hub.connectedCount()>0?"正在向 "+hub.connectedCount()+" 台电脑传输摄像画面":"摄像头已开启，等待扫码连接";
        if(audioCapture!=null) text+=" · 麦克风已开启";
        return new Notification.Builder(this,CHANNEL).setSmallIcon(android.R.drawable.ic_menu_camera)
            .setContentTitle("PCam 摄像头运行中").setContentText(text).setContentIntent(open)
            .setOngoing(true).setOnlyAlertOnce(true).setCategory(Notification.CATEGORY_SERVICE)
            .addAction(new Notification.Action.Builder(null,"停止传输",stop).build()).build();
    }
    private void promote() {
        if(foreground) return;
        if(Build.VERSION.SDK_INT>=30) startForeground(NOTIFICATION,notification(),ServiceInfo.FOREGROUND_SERVICE_TYPE_CAMERA|(audioCapture!=null?ServiceInfo.FOREGROUND_SERVICE_TYPE_MICROPHONE:0));
        else startForeground(NOTIFICATION,notification());
        foreground=true;
        microphoneForeground=audioCapture!=null;
    }
    private void microphoneType(boolean enabled) {
        if(Build.VERSION.SDK_INT>=30) startForeground(NOTIFICATION,notification(),ServiceInfo.FOREGROUND_SERVICE_TYPE_CAMERA|(enabled?ServiceInfo.FOREGROUND_SERVICE_TYPE_MICROPHONE:0));
        else startForeground(NOTIFICATION,notification());
        foreground=true;
        microphoneForeground=enabled;
    }
    public void setSound(String id,boolean enabled) {
        if(!enabled) {hub.setAudio(id,false);if(!hub.hasAudioTargets())stopAudioCapture();return;}
        if(!visible || destroyed || camera==null)return;
        if(!hub.audioReady(id)){update("声音无法开启，请更新电脑端并保持视频连接");return;}
        if(checkSelfPermission(Manifest.permission.RECORD_AUDIO)!=PackageManager.PERMISSION_GRANTED) {update("声音需要麦克风权限");return;}
        try {
            if(audioCapture==null) {
                // 用户在可见页面授权并点击开启后，才升级为 camera|microphone 前台服务。
                microphoneType(true);int generation=++audioGeneration;
                audioCapture=new AudioCapture(new AudioCapture.Sink() {
                    public void frame(long time,byte[] pcm) {hub.broadcastAudio(time,pcm);}
                    public void failed() {main.post(() -> {
                        if(destroyed || generation!=audioGeneration)return;
                        hub.disableAudio();stopAudioCapture();update("麦克风采集失败，请重新开启声音");
                    });}
                });
            }
            hub.setAudio(id,true);
            if(!hub.hasAudioTargets())stopAudioCapture();
            update("");
        } catch(RuntimeException e) {hub.disableAudio();stopAudioCapture();update("声音无法开启，请检查麦克风权限或是否被其他应用占用");}
    }
    private void stopAudioCapture() {
        audioGeneration++;AudioCapture old=audioCapture;audioCapture=null;if(old!=null)old.close();
        // Also downgrade a failed microphone start, but do not re-register the foreground service on every idle update.
        if(microphoneForeground && foreground && !destroyed) try{microphoneType(false);}catch(RuntimeException error){Diagnostics.write("microphone foreground downgrade failed: "+error.getClass().getSimpleName());}
    }
    public ConnectionHub hub() { return hub; }
    public boolean scanning() { return scanning; }
    public boolean cameraReady() { return camera!=null; }
    public boolean frontFacing() { return frontFacing; }
    public int rawWidth() { return rawWidth; }
    public int rawHeight() { return rawHeight; }
    public int rotation() { return rotation; }
    public int previewRotation(){return CameraRotation.local(rotation,displayRotation);}
    public void setDisplayRotation(int degrees){
        degrees=CameraRotation.normalize(degrees);if(displayRotation==degrees)return;
        if(focusing)restoreFocus();displayRotation=degrees;
        if(camera!=null)try{camera.setDisplayOrientation(CameraRotation.preview(previewRotation(),frontFacing));}
        catch(RuntimeException e){update("预览方向调整失败，请重新打开摄像头");}
    }
    public boolean zoomAvailable() { return camera!=null && visible && !scanning && zoomEnabled; }
    public int zoomIndex() { return zoomIndex; }
    public int zoomTarget() { return requestedZoom; }
    public int zoomMax() { return zoomSteps.maxIndex(); }
    public double zoomRatio() { return zoomSteps.ratio(zoomIndex); }
    public double zoomTargetRatio() { return zoomSteps.ratio(requestedZoom); }
    public double zoomMaxRatio() { return zoomSteps.ratio(zoomSteps.maxIndex()); }
    public boolean zoomSupported() { return zoomSteps.supported(); }
    public void requestZoom(int index) {
        if(!zoomAvailable()) return;
        requestedZoom=zoomSteps.clamp(index);
        // 合并手势事件，限制参数写入频率；不重启预览、不另外裁切 JPEG。
        if(!zoomQueued && requestedZoom!=zoomIndex) { zoomQueued=true; main.postDelayed(applyZoomTask,75); }
    }
    public void requestZoomRatio(double ratio) { requestZoom(zoomSteps.nearest(ratio)); }
    public void flushZoom() { main.removeCallbacks(applyZoomTask); zoomQueued=false; applyZoom(); }
    private void applyZoom() {
        if(!zoomAvailable() || requestedZoom==zoomIndex) return;
        try {
            if(focusing) restoreFocus();
            Camera.Parameters p=camera.getParameters(); p.setZoom(requestedZoom); camera.setParameters(p);
            zoomIndex=zoomSteps.clamp(camera.getParameters().getZoom()); requestedZoom=zoomIndex;
        } catch(RuntimeException e) {
            // 厂商拒绝变焦时保留视频会话，只禁用本次相机的变焦操作。
            zoomEnabled=false; requestedZoom=zoomIndex;
            update("变焦暂时无法使用，可切换镜头后重试");
        }
        if(zoomListener!=null) zoomListener.run();
    }
    private void initializeZoom() {
        zoomSteps=new ZoomSteps(null,0); zoomIndex=requestedZoom=0; zoomEnabled=false;
        try {
            Camera.Parameters p=camera.getParameters();
            if(!p.isZoomSupported()) return;
            zoomSteps=new ZoomSteps(p.getZoomRatios(),p.getMaxZoom());
            if(!zoomSteps.supported()) return;
            // 新打开镜头（包括扫码）从 1.0× 开始；前后台切换不走此处。
            p.setZoom(0); camera.setParameters(p);
            zoomIndex=zoomSteps.clamp(camera.getParameters().getZoom()); requestedZoom=zoomIndex;
            zoomEnabled=true;
        } catch(RuntimeException ignored) { zoomEnabled=false; }
    }
    public double[] sampleRates() {
        long now=System.nanoTime(); double[] traffic=hub.sampleTraffic(now), rate=produced.sample(now);
        return new double[]{traffic[0],rate[1]};
    }
    public void attach(Listener ui,Runnable zoomUi,SurfaceHolder holder) {
        Diagnostics.write("activity attached");
        visible=true; listener=ui; zoomListener=zoomUi;
        if(!hub.hasTargets()) scanning=true;
        try { promote(); setPreview(holder); openCamera(); }
        catch(RuntimeException e) { update("无法启动摄像服务，请重新打开应用"); }
        update("");
    }
    public void detach() {
        Diagnostics.write("activity detached; targets="+hub.hasTargets());
        if(focusing) restoreFocus();
        flushZoom(); // 将手指最后指定的倍率应用后，再解除页面。
        visible=false; listener=null; zoomListener=null; scanning=false; scanEpoch++;
        if(!hub.hasTargets()) { previewHolder=null; stopSession(); }
        else setPreview(null);
    }
    public void setPreview(SurfaceHolder holder) {
        if(previewHolder==holder && camera!=null) return;
        previewHolder=holder;
        if(camera==null) { openCamera(); return; }
        try {
            // 只切换输出 Surface，不释放相机，也不重建 TCP 连接。
            if(focusing) restoreFocus();
            rotationRevision++; camera.setPreviewCallbackWithBuffer(null); camera.stopPreview();
            bindPreviewOutput(); startBufferedPreview(); update("");
        } catch(Exception e) {Diagnostics.write("preview switch failed: "+e.getClass().getSimpleName());recoverPreview();}
    }
    private void recoverPreview(){
        if(camera==null)return;
        try{
            rotationRevision++;camera.setPreviewCallbackWithBuffer(null);camera.stopPreview();
            if(backgroundPreview!=null){backgroundPreview.close();backgroundPreview=null;}
            bindPreviewOutput();startBufferedPreview();Diagnostics.write("preview restarted without reopening camera");
        }catch(Exception error){Diagnostics.write("preview recovery failed: "+error.getClass().getSimpleName());closeCamera();if(visible)openCamera();else update("摄像头恢复失败，请返回手机应用恢复画面");}
    }
    private void bindPreviewOutput() throws Exception {
        if(previewHolder!=null && previewHolder.getSurface().isValid()) camera.setPreviewDisplay(previewHolder);
        else {
            if(backgroundPreview!=null && backgroundPreview.failed()){backgroundPreview.close();backgroundPreview=null;}
            if(backgroundPreview==null) backgroundPreview=new BackgroundPreview();
            camera.setPreviewTexture(backgroundPreview.texture());
        }
    }
    private void startBufferedPreview() {
        lastCameraCallback=SystemClock.elapsedRealtime();
        camera.setPreviewCallbackWithBuffer(this::frame);
        for(int i=0;i<3;i++) camera.addCallbackBuffer(new byte[rawWidth*rawHeight*3/2]);
        camera.startPreview();
    }
    public void beginScan() {
        if(!visible || destroyed) return;
        try { promote(); scanning=true; scanEpoch++; closeCamera(); selectBackCamera(); openCamera(); update(""); }
        catch(RuntimeException e) { update("无法启动摄像服务，请重新打开应用"); }
    }
    public void disconnect(String id) { hub.disconnect(id); }
    public void disconnectAll() { scanning=false; scanEpoch++; hub.disconnectAll(); }
    // 返回 1 表示区域对焦，2 表示普通对焦，0 表示镜头不支持。
    public int focusAt(double x,double y,FocusResult result) {
        if(camera==null || !visible || scanning) return 0;
        try {
            restoreFocus();
            Camera source=camera; Camera.Parameters p=source.getParameters();
            java.util.List<String> modes=p.getSupportedFocusModes();
            boolean single=modes!=null && modes.contains(Camera.Parameters.FOCUS_MODE_AUTO);
            if(!single && !Camera.Parameters.FOCUS_MODE_CONTINUOUS_VIDEO.equals(defaultFocusMode)
                && !Camera.Parameters.FOCUS_MODE_CONTINUOUS_PICTURE.equals(defaultFocusMode)) return 0;
            source.cancelAutoFocus(); if(single) p.setFocusMode(Camera.Parameters.FOCUS_MODE_AUTO);
            boolean area=p.getMaxNumFocusAreas()>0;
            if(area) {
                int[] a=FocusPoint.area(x,y,previewRotation());
                p.setFocusAreas(java.util.Collections.singletonList(new Camera.Area(new Rect(a[0],a[1],a[2],a[3]),1000)));
            }
            source.setParameters(p); focusing=true; int generation=++focusGeneration;
            // 未收到回调也会退出单次对焦，不把镜头永久锁住。
            main.postDelayed(restoreFocusTask,4000);
            source.autoFocus((ok,c) -> main.post(() -> {
                if(destroyed || c!=camera || generation!=focusGeneration) return;
                result.finished(ok);
                main.removeCallbacks(restoreFocusTask); main.postDelayed(restoreFocusTask,1500);
            }));
            return area?1:2;
        } catch(RuntimeException e) { restoreFocus(); update("对焦失败，请重试"); return 0; }
    }
    private void restoreFocus() {
        main.removeCallbacks(restoreFocusTask); focusGeneration++; focusing=false;
        if(camera==null) return;
        try {
            camera.cancelAutoFocus(); Camera.Parameters p=camera.getParameters();
            if(p.getMaxNumFocusAreas()>0) p.setFocusAreas(null);
            if(defaultFocusMode!=null) p.setFocusMode(defaultFocusMode);
            camera.setParameters(p);
        } catch(RuntimeException ignored) { }
    }
    public void rotatePicture() {
        if(camera==null) return;
        if(focusing) restoreFocus();
        int next=CameraRotation.next(extraRotation), output=CameraRotation.output(sensorOrientation,next);
        try {
            camera.setDisplayOrientation(CameraRotation.preview(CameraRotation.local(output,displayRotation),frontFacing));
            extraRotation=next; rotation=output; rotationRevision++;
            getSharedPreferences("phonecam",MODE_PRIVATE).edit().putInt("pictureRotation",extraRotation).apply(); update("");
        } catch(RuntimeException e) { update("旋转失败，请重新打开摄像头后重试"); }
    }
    private void update(String message) {
        main.post(() -> {
            if(destroyed) return;
            if(audioCapture!=null && !hub.hasAudioTargets())stopAudioCapture();
            boolean connected=hub.hasTargets();
            updatePowerLocks(connected);
            if(foreground)try{getSystemService(NotificationManager.class).notify(NOTIFICATION,notification());}catch(RuntimeException error){Diagnostics.write("notification refresh failed: "+error.getClass().getSimpleName());}
            if(listener!=null) listener.changed(message);
            if(!visible && !connected) stopSession();
        });
    }
    private void updatePowerLocks(boolean connected){
        // Vendor/system failures in one lock must not skip the remaining cleanup or escape a button callback.
        try{if(wakeLock!=null){if(connected&&!wakeLock.isHeld())wakeLock.acquire();if(!connected&&wakeLock.isHeld())wakeLock.release();}}
        catch(RuntimeException error){Diagnostics.write("wake lock state failed: "+error.getClass().getSimpleName());}
        try{if(wifiLock!=null){if(connected&&!wifiLock.isHeld())wifiLock.acquire();if(!connected&&wifiLock.isHeld())wifiLock.release();}}
        catch(RuntimeException error){Diagnostics.write("wifi lock state failed: "+error.getClass().getSimpleName());}
    }
    private void stopSession() {
        Diagnostics.write("stop session; visible="+visible+" targets="+hub.hasTargets());
        stopAudioCapture();
        scanning=false; scanEpoch++; closeCamera();
        if(backgroundPreview!=null) { backgroundPreview.close(); backgroundPreview=null; }
        // disconnectAll 会回调 update；仅在确有连接时触发，避免递归停止。
        if(hub.hasTargets()) hub.disconnectAll();
        updatePowerLocks(false);
        foreground=false;microphoneForeground=false;stopForeground(STOP_FOREGROUND_REMOVE); stopSelf();
        if(listener!=null) listener.changed("");
    }
    private void selectBackCamera() {
        try{
            Camera.CameraInfo info=new Camera.CameraInfo();
            for(int i=0;i<Camera.getNumberOfCameras();i++) { Camera.getCameraInfo(i,info); if(info.facing==Camera.CameraInfo.CAMERA_FACING_BACK) { cameraId=i; return; } }
        }catch(RuntimeException error){Diagnostics.write("camera enumeration failed: "+error.getClass().getSimpleName());}
    }
    public void switchCamera() {
        if(destroyed || !visible || scanning)return;
        try{
            Camera.CameraInfo info=new Camera.CameraInfo(); Camera.getCameraInfo(cameraId,info);
            int wanted=info.facing==Camera.CameraInfo.CAMERA_FACING_FRONT?Camera.CameraInfo.CAMERA_FACING_BACK:Camera.CameraInfo.CAMERA_FACING_FRONT;
            for(int i=0;i<Camera.getNumberOfCameras();i++) {
                Camera.getCameraInfo(i,info); if(info.facing==wanted) { closeCamera(); cameraId=i; openCamera(); return; }
            }
            update("这台手机没有另一方向的摄像头");
        }catch(RuntimeException error){Diagnostics.write("camera switch failed: "+error.getClass().getSimpleName());update("切换镜头失败，请重新扫码或打开应用");}
    }
    private void openCamera() {
        if(!foreground || !visible || camera!=null || checkSelfPermission(Manifest.permission.CAMERA)!=PackageManager.PERMISSION_GRANTED) return;
        try {
            camera=Camera.open(cameraId);
            previewRetries=0;Diagnostics.write("camera opened");
            camera.setErrorCallback((error,source) -> {
                if(source!=camera || destroyed) return;
                Diagnostics.write("camera error="+error);closeCamera();if(visible)main.postDelayed(()->openCamera(),1000);update("摄像头被系统中断，请返回手机应用恢复画面");
            });
            Camera.Parameters p=camera.getParameters();
            int[] originalFps=new int[2]; p.getPreviewFpsRange(originalFps);
            Camera.Size best=p.getSupportedPreviewSizes().get(0);
            for(Camera.Size size:p.getSupportedPreviewSizes()) if(Math.abs(size.width*size.height-640*480)<Math.abs(best.width*best.height-640*480)) best=size;
            p.setPreviewSize(best.width,best.height); p.setPreviewFormat(ImageFormat.NV21);
            java.util.List<int[]> fpsRanges=p.getSupportedPreviewFpsRange();
            if(fpsRanges!=null && !fpsRanges.isEmpty()) {
                int[] choice=fpsRanges.get(0); long score=Long.MAX_VALUE;
                for(int[] range:fpsRanges) {
                    long candidate=Math.abs(range[1]-30000)*10L+Math.abs(range[0]-30000);
                    if(candidate<score) { score=candidate; choice=range; }
                }
                p.setPreviewFpsRange(choice[0],choice[1]);
            }
            java.util.List<String> modes=p.getSupportedFocusModes(); manualFocus=false;
            if(modes!=null && modes.contains(Camera.Parameters.FOCUS_MODE_CONTINUOUS_VIDEO)) p.setFocusMode(Camera.Parameters.FOCUS_MODE_CONTINUOUS_VIDEO);
            else if(modes!=null && modes.contains(Camera.Parameters.FOCUS_MODE_CONTINUOUS_PICTURE)) p.setFocusMode(Camera.Parameters.FOCUS_MODE_CONTINUOUS_PICTURE);
            else if(modes!=null && modes.contains(Camera.Parameters.FOCUS_MODE_AUTO)) { p.setFocusMode(Camera.Parameters.FOCUS_MODE_AUTO); manualFocus=true; }
            try { camera.setParameters(p); }
            catch(RuntimeException fpsError) {
                // 厂商若拒绝所选帧率组合，退回其默认帧率，不影响摄像头启动。
                p.setPreviewFpsRange(originalFps[0],originalFps[1]); camera.setParameters(p);
            }
            defaultFocusMode=camera.getParameters().getFocusMode();
            Camera.CameraInfo info=new Camera.CameraInfo(); Camera.getCameraInfo(cameraId,info);
            sensorOrientation=info.orientation; frontFacing=info.facing==Camera.CameraInfo.CAMERA_FACING_FRONT;
            rawWidth=best.width; rawHeight=best.height;
            rotation=CameraRotation.output(sensorOrientation,extraRotation); rotationRevision++;
            camera.setDisplayOrientation(CameraRotation.preview(previewRotation(),frontFacing));
            initializeZoom(); bindPreviewOutput(); startBufferedPreview();
            update("");
        } catch(Exception e) { stopSession(); update("无法打开摄像头，请检查权限后重试"); }
    }
    private void frame(byte[] data,Camera source) {
        if(destroyed || source!=camera) return;
        lastCameraCallback=SystemClock.elapsedRealtime();previewRetries=0;
        long now=android.os.SystemClock.elapsedRealtime();
        boolean scan=visible && scanning && now-lastScan>=250, send=hub.connectedCount()>0 && now-lastFrame>=33;
        if((!scan && !send) || !busy.compareAndSet(false,true)) { returnBuffer(source,data); return; }
        if(scan) lastScan=now; if(send) lastFrame=now;
        if(manualFocus && !focusing && (scanning || hub.connectedCount()>0) && now-lastFocus>4000) {
            lastFocus=now; focusing=true; int generation=++focusGeneration;
            try {
                main.postDelayed(restoreFocusTask,3000);
                source.autoFocus((ok,c) -> main.post(() -> {
                    if(c==camera && generation==focusGeneration) restoreFocus();
                }));
            } catch(RuntimeException ignored) { restoreFocus(); }
        }
        int width=rawWidth,height=rawHeight,angle=rotation,epoch=scanEpoch,revision=rotationRevision;
        byte[] copy=data; // 工作者持有此回调缓冲，完成后归还给相机。
        worker.execute(() -> {
            try {
                if(scan) {
                    try {
                        PairingInfo info=PairingInfo.parse(QrDecoder.decode(copy,width,height));
                        main.post(() -> {
                            if(visible && !destroyed && scanning && epoch==scanEpoch) { scanning=false; scanEpoch++; hub.connect(info); }
                        });
                    } catch(com.google.zxing.ReaderException ignored) { }
                    catch(IllegalArgumentException e) { if(epoch==scanEpoch) update(e.getMessage()); }
                }
                if(send && !destroyed) {
                    byte[] pixels=copy;
                    if(angle!=0) {
                        if(rotatedYuv==null || rotatedYuv.length!=copy.length) rotatedYuv=new byte[copy.length];
                        Nv21.rotate(copy,rotatedYuv,width,height,angle); pixels=rotatedYuv;
                    }
                    int[] shape=CameraRotation.size(width,height,angle); jpegBuffer.reset();
                    new YuvImage(pixels,ImageFormat.NV21,shape[0],shape[1],null).compressToJpeg(new Rect(0,0,shape[0],shape[1]),70,jpegBuffer);
                    byte[] bytes=jpegBuffer.toByteArray();
                    if(!destroyed && revision==rotationRevision && bytes.length<=2*1024*1024) { hub.broadcast(bytes); produced.add(0,1); }
                }
            } catch(Exception e) { update("画面处理失败，请重试"); }
            finally { busy.set(false); main.post(() -> returnBuffer(source,copy)); }
        });
    }
    private void returnBuffer(Camera source,byte[] data) { if(camera==source && !destroyed) try { source.addCallbackBuffer(data); } catch(RuntimeException ignored) {} }
    private void closeCamera() {
        main.removeCallbacks(restoreFocusTask); focusGeneration++; focusing=false; defaultFocusMode=null;
        main.removeCallbacks(applyZoomTask); zoomQueued=false; zoomEnabled=false;
        zoomSteps=new ZoomSteps(null,0); zoomIndex=requestedZoom=0;
        rotationRevision++; Camera old=camera; camera=null;
        if(old!=null) {
            try { old.setErrorCallback(null); old.setPreviewCallbackWithBuffer(null); old.stopPreview(); } catch(RuntimeException ignored) { }
            try { old.release(); } catch(RuntimeException ignored) { }
        }
    }

    @Override public void onDestroy() {
        Diagnostics.write("service destroyed");
        destroyed=true; visible=false; listener=null; zoomListener=null; scanEpoch++;
        stopAudioCapture(); closeCamera(); hub.close(); worker.shutdown(); main.removeCallbacksAndMessages(null);
        if(backgroundPreview!=null) backgroundPreview.close();
        updatePowerLocks(false);
        stopForeground(STOP_FOREGROUND_REMOVE); super.onDestroy();
    }
}
