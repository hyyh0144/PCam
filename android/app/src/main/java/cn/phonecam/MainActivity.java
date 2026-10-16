// 双指变焦、滑条与单步调节；控件不进入会议画面。
package cn.phonecam;
import android.Manifest;
import android.app.Activity;
import android.os.Bundle;
import android.content.pm.PackageManager;
import android.graphics.*;
import android.view.*;
import android.widget.*;

@SuppressWarnings("deprecation")
public class MainActivity extends Activity implements SurfaceHolder.Callback {
    private LinearLayout body, controlPanel, controls, actions, zoomRow;
    private TextView backgroundHint;
    private ScrollView controlScroll;
    private SurfaceView preview;
    private FrameLayout previewArea;
    private TextView summary, stats, targetsTitle;
    private Button scanButton, switchButton, rotateButton;
    private LinearLayout targetRows;
    private ScrollView targetScroll;
    private SeekBar zoomBar;
    private Button zoomMinus, zoomPlus, zoomReset;
    private boolean zoomDragging;
    private double pinchRatio;
    private ScaleGestureDetector scaleDetector;
    private View focusOverlay;
    private float touchX,touchY,focusX,focusY;
    private long touchTime;
    private boolean moved,multiple;
    private int focusColor,focusToken;
    private final android.graphics.Paint focusPaint=new android.graphics.Paint(3);
    private CameraService service;
    private ConnectionHub hub;
    private String pendingAudioTarget;
    private android.app.AlertDialog backgroundGuide;
    private boolean resumed, destroyed, bound, surfaceReady;
    private final android.os.Handler statsHandler=new android.os.Handler(android.os.Looper.getMainLooper());
    private final Runnable statsTick=new Runnable() {
        @Override public void run() {
            if(!resumed || destroyed) return;
            if(service!=null) {
                double[] values=service.sampleRates();
                stats.setText(String.format(java.util.Locale.ROOT,"上行合计 %.2f Mbps · 生成 %.0f fps",values[0],values[1]));
            }
            statsHandler.postDelayed(this,1000);
        }
    };
    private final android.content.ServiceConnection connection=new android.content.ServiceConnection() {
        @Override public void onServiceConnected(android.content.ComponentName name,android.os.IBinder binder) {
            if(!resumed) return;
            service=((CameraService.LocalBinder)binder).getService(); hub=service.hub();
            service.attach(MainActivity.this::update,MainActivity.this::updateZoomControls,surfaceReady?preview.getHolder():null);
            service.sampleRates(); update(""); completeAudioPermission();
        }
        @Override public void onServiceDisconnected(android.content.ComponentName name) {
            service=null; hub=null; updateRotateButton(); updateZoomControls();
            Toast.makeText(MainActivity.this,"摄像服务已停止，请返回应用重新扫码",Toast.LENGTH_LONG).show();
        }
    };

    @Override public void onCreate(Bundle state) {
        super.onCreate(state);
        setRequestedOrientation(android.content.pm.ActivityInfo.SCREEN_ORIENTATION_FULL_SENSOR);
        getWindow().addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON);
        LinearLayout layout=new LinearLayout(this); controlPanel=layout; layout.setOrientation(LinearLayout.VERTICAL);
        // 系统安全区由外层处理，避免 fitsSystemWindows 覆盖内容边距。
        FrameLayout safeArea=new FrameLayout(this); safeArea.setFitsSystemWindows(true);
        safeArea.setBackgroundColor(0xFFF8FAFC);
        layout.setPadding(3,0,3,0); layout.setLayoutDirection(View.LAYOUT_DIRECTION_LTR);
        body=new LinearLayout(this);body.setLayoutDirection(View.LAYOUT_DIRECTION_LTR);
        controlScroll=new ScrollView(this);controlScroll.setFillViewport(false);
        controlScroll.addView(layout,new ScrollView.LayoutParams(-1,-2));
        body.addView(controlScroll,new LinearLayout.LayoutParams(-1,-2));
        safeArea.addView(body,new FrameLayout.LayoutParams(-1,-1));
        layout.setBackgroundColor(0xFFF8FAFC);
        TextView title=new TextView(this); title.setText("PCam-让手机成为电脑的眼睛"); title.setTextSize(20); title.setTextColor(0xFF152238); layout.addView(title);
        summary=new TextView(this); summary.setText("手机和电脑连接同一 Wi-Fi"); summary.setTextSize(12); layout.addView(summary);
        // 后台设置入口只在控制区显示，不进入传输画面。
        backgroundHint=new TextView(this);
        backgroundHint.setText("后台运行：锁定 PCam，并允许后台运行 ›");
        backgroundHint.setTextSize(12); backgroundHint.setTextColor(0xFF1676C8);
        backgroundHint.setPadding(0,dp(3),0,dp(3));
        backgroundHint.setOnClickListener(v -> showBackgroundGuide()); layout.addView(backgroundHint);
        // 两栏固定占用同一块高度，多电脑只在右侧滚动，留出摄像画面空间。
        controls=new LinearLayout(this); controls.setOrientation(LinearLayout.HORIZONTAL);
        LinearLayout.LayoutParams controlsSize=new LinearLayout.LayoutParams(-1,dp(216)); controlsSize.setMargins(0,dp(12),0,dp(8));
        layout.addView(controls,controlsSize);
        actions=new LinearLayout(this); actions.setOrientation(LinearLayout.VERTICAL);
        controls.addView(actions,new LinearLayout.LayoutParams(dp(100),-1));
        scanButton=actionButton("扫码连接",true); actions.addView(scanButton,new LinearLayout.LayoutParams(-1,dp(48)));
        scanButton.setOnClickListener(v -> beginScan());
        switchButton=actionButton("切换镜头",false);
        LinearLayout.LayoutParams switchSize=new LinearLayout.LayoutParams(-1,dp(48)); switchSize.topMargin=dp(8); actions.addView(switchButton,switchSize);
        switchButton.setOnClickListener(v -> switchCamera());
        Button disconnect=actionButton("全部断开",false);
        LinearLayout.LayoutParams disconnectSize=new LinearLayout.LayoutParams(-1,dp(48)); disconnectSize.topMargin=dp(8); actions.addView(disconnect,disconnectSize);
        disconnect.setOnClickListener(v -> { if(service!=null) service.disconnectAll(); });
        rotateButton=actionButton("旋转画面",false);
        LinearLayout.LayoutParams rotateSize=new LinearLayout.LayoutParams(-1,dp(48)); rotateSize.topMargin=dp(8);
        actions.addView(rotateButton,rotateSize); rotateButton.setOnClickListener(v -> rotatePicture());
        LinearLayout computers=new LinearLayout(this); computers.setOrientation(LinearLayout.VERTICAL);
        computers.setPadding(dp(8),dp(8),dp(8),dp(4)); computers.setBackground(roundBackground(0xFFEEF2F6));
        LinearLayout.LayoutParams computersSize=new LinearLayout.LayoutParams(0,-1,1); computersSize.leftMargin=dp(10); controls.addView(computers,computersSize);
        targetsTitle=new TextView(this); targetsTitle.setTextSize(13); targetsTitle.setTextColor(0xFF334155); computers.addView(targetsTitle);
        targetRows=new LinearLayout(this); targetRows.setOrientation(LinearLayout.VERTICAL);
        targetScroll=new ScrollView(this); targetScroll.setFillViewport(true); targetScroll.addView(targetRows);
        computers.addView(targetScroll,new LinearLayout.LayoutParams(-1,0,1));
        stats=new TextView(this); stats.setText("上行合计 0.00 Mbps · 生成 0 fps"); stats.setTextSize(12); stats.setTextColor(0xFF505968);
        stats.setPadding(0,dp(4),0,dp(8)); layout.addView(stats);
        // 操作条在预览之外，发送给电脑的仍是相机原始变焦后的画面。
        zoomRow=new LinearLayout(this); zoomRow.setGravity(Gravity.CENTER_VERTICAL);
        zoomMinus=actionButton("－",false); zoomMinus.setContentDescription("缩小画面一档");
        zoomPlus=actionButton("＋",false); zoomPlus.setContentDescription("放大画面一档");
        zoomReset=actionButton("1.0×",false); zoomReset.setTextSize(12); zoomReset.setContentDescription("恢复一倍画面");
        zoomBar=new SeekBar(this); zoomBar.setContentDescription("画面变焦");
        zoomRow.addView(zoomMinus,new LinearLayout.LayoutParams(dp(48),dp(48)));
        zoomRow.addView(zoomBar,new LinearLayout.LayoutParams(0,dp(48),1));
        zoomRow.addView(zoomPlus,new LinearLayout.LayoutParams(dp(48),dp(48)));
        LinearLayout.LayoutParams resetSize=new LinearLayout.LayoutParams(dp(76),dp(48)); resetSize.leftMargin=dp(6); zoomRow.addView(zoomReset,resetSize);
        LinearLayout.LayoutParams zoomSize=new LinearLayout.LayoutParams(-1,dp(48)); zoomSize.bottomMargin=dp(6); layout.addView(zoomRow,zoomSize);
        zoomMinus.setOnClickListener(v -> { if(service!=null) { service.requestZoom(service.zoomTarget()-1); updateZoomControls(); } });
        zoomPlus.setOnClickListener(v -> { if(service!=null) { service.requestZoom(service.zoomTarget()+1); updateZoomControls(); } });
        zoomReset.setOnClickListener(v -> { if(service!=null) { service.requestZoom(0); service.flushZoom(); updateZoomControls(); } });
        zoomBar.setOnSeekBarChangeListener(new SeekBar.OnSeekBarChangeListener() {
            public void onProgressChanged(SeekBar bar,int value,boolean fromUser) { if(fromUser && service!=null) service.requestZoom(value); }
            public void onStartTrackingTouch(SeekBar bar) { zoomDragging=true; }
            public void onStopTrackingTouch(SeekBar bar) { zoomDragging=false; if(service!=null) service.flushZoom(); updateZoomControls(); }
        });
        previewArea=new FrameLayout(this) {
            @Override protected void onMeasure(int widthSpec,int heightSpec){
                int width=View.MeasureSpec.getSize(widthSpec),height=View.MeasureSpec.getSize(heightSpec);
                if(service!=null && preview!=null && width>0 && height>0){
                    int[] fitted=CameraRotation.fit(service.rawWidth(),service.rawHeight(),service.previewRotation(),width,height);
                    FrameLayout.LayoutParams lp=(FrameLayout.LayoutParams)preview.getLayoutParams();
                    lp.width=fitted[0];lp.height=fitted[1];
                    lp.gravity=body.getOrientation()==LinearLayout.VERTICAL?(Gravity.TOP|Gravity.CENTER_HORIZONTAL):Gravity.CENTER;
                }
                super.onMeasure(widthSpec,heightSpec);
            }
        }; previewArea.setBackgroundColor(Color.BLACK);
        body.addView(previewArea,new LinearLayout.LayoutParams(-1,0,1));
        preview=new SurfaceView(this); previewArea.addView(preview,new FrameLayout.LayoutParams(-1,-1,Gravity.CENTER)); preview.getHolder().addCallback(this);
        scaleDetector=new ScaleGestureDetector(this,new ScaleGestureDetector.SimpleOnScaleGestureListener() {
            @Override public boolean onScaleBegin(ScaleGestureDetector detector) {
                if(service==null || !service.zoomAvailable()) return false;
                clearFocusIndicator(); pinchRatio=service.zoomTargetRatio(); return true;
            }
            @Override public boolean onScale(ScaleGestureDetector detector) {
                if(service==null || !service.zoomAvailable()) return false;
                // 累积倍率，避免设备档位稀疏时小幅手势反复被舍入掉。
                pinchRatio=Math.max(1,Math.min(service.zoomMaxRatio(),pinchRatio*detector.getScaleFactor()));
                service.requestZoomRatio(pinchRatio); return true;
            }
            @Override public void onScaleEnd(ScaleGestureDetector detector) { if(service!=null) service.flushZoom(); updateZoomControls(); }
        });
        // 覆盖层使用屏幕方向坐标，不受前摄 SurfaceView 水平翻转影响。
        focusOverlay=new View(this) {
            @Override protected void onDraw(Canvas canvas) {
                super.onDraw(canvas);
                if(focusColor==0) return;
                focusPaint.setColor(focusColor); focusPaint.setStyle(Paint.Style.STROKE); focusPaint.setStrokeWidth(dp(2));
                float half=dp(28); canvas.drawRect(focusX-half,focusY-half,focusX+half,focusY+half,focusPaint);
            }
        };
        previewArea.addView(focusOverlay,new FrameLayout.LayoutParams(-1,-1));
        focusOverlay.setOnTouchListener((v,event) -> {
            scaleDetector.onTouchEvent(event);
            int action=event.getActionMasked();
            if(action==MotionEvent.ACTION_DOWN) {
                touchX=event.getX(); touchY=event.getY(); touchTime=event.getEventTime(); moved=false; multiple=false;
            }
            if(event.getPointerCount()>1 || action==MotionEvent.ACTION_POINTER_DOWN) multiple=true;
            if(Math.hypot(event.getX()-touchX,event.getY()-touchY)>ViewConfiguration.get(this).getScaledTouchSlop()) moved=true;
            if(action==MotionEvent.ACTION_UP) {
                v.performClick();
                if(!moved && !multiple && event.getEventTime()-touchTime<500) tapFocus(event.getX(),event.getY());
            }
            if(action==MotionEvent.ACTION_CANCEL) multiple=true;
            return true;
        });
        previewArea.addOnLayoutChangeListener((v,l,t,r,b,ol,ot,or,ob) -> { if(r-l!=or-ol || b-t!=ob-ot) previewArea.post(this::fitPreview); });
        safeArea.addOnLayoutChangeListener((v,l,t,r,b,ol,ot,or,ob)->{if(r-l!=or-ol || b-t!=ob-ot)arrange(r-l,b-t);});
        controlPanel.addOnLayoutChangeListener((v,l,t,r,b,ol,ot,or,ob)->{if(body.getOrientation()==LinearLayout.HORIZONTAL)fitLandscapeZoom();else fitPortraitControls();});
        setContentView(safeArea);
        updateRotateButton(); updateZoomControls();
    }

    private void arrange(int width,int height){
        if(width<=0 || height<=0)return;boolean landscape=width>height;
        body.setOrientation(landscape?LinearLayout.HORIZONTAL:LinearLayout.VERTICAL);
        compactControls(landscape);
        controlScroll.setLayoutParams(landscape?new LinearLayout.LayoutParams(Math.min(dp(340),width*45/100),-1):new LinearLayout.LayoutParams(-1,Math.min(dp(460),height*58/100)));
        previewArea.setLayoutParams(landscape?new LinearLayout.LayoutParams(0,-1,1):new LinearLayout.LayoutParams(-1,0,1));
        controlPanel.post(()->{if(body.getOrientation()==LinearLayout.HORIZONTAL)fitLandscapeZoom();else fitPortraitControls();});
        clearFocusIndicator();fitPreview();
    }
    private void compactControls(boolean landscape){
        int gap=dp(landscape?5:8);
        for(int i=1;i<actions.getChildCount();i++){
            View button=actions.getChildAt(i);LinearLayout.LayoutParams lp=(LinearLayout.LayoutParams)button.getLayoutParams();
            lp.topMargin=gap;button.setLayoutParams(lp);
        }
        LinearLayout.LayoutParams cp=(LinearLayout.LayoutParams)controls.getLayoutParams();
        cp.height=dp(48)*4+gap*3;cp.topMargin=dp(landscape?5:12);cp.bottomMargin=dp(landscape?4:8);controls.setLayoutParams(cp);
        backgroundHint.setPadding(0,dp(landscape?1:3),0,dp(landscape?1:3));
        stats.setPadding(0,dp(landscape?2:4),0,dp(landscape?4:8));
        setZoomSize(48,landscape);
    }
    private void setZoomSize(int size,boolean landscape){
        LinearLayout.LayoutParams row=(LinearLayout.LayoutParams)zoomRow.getLayoutParams();row.height=dp(size);row.bottomMargin=dp(landscape?3:6);zoomRow.setLayoutParams(row);
        for(int i=0;i<zoomRow.getChildCount();i++){
            View child=zoomRow.getChildAt(i);LinearLayout.LayoutParams lp=(LinearLayout.LayoutParams)child.getLayoutParams();lp.height=dp(size);
            if(child==zoomMinus || child==zoomPlus)lp.width=dp(size);
            if(child==zoomReset){lp.width=dp(size<48?72:76);lp.leftMargin=dp(landscape?2:6);}
            child.setLayoutParams(lp);
        }
    }
    private void fitPortraitControls(){
        // 控制区只占实际内容高度，不为它保留多余白色空间；超高内容仍可滚动。
        int content=controlPanel.getHeight(),available=body.getHeight();
        if(content<=0 || available<=0)return;
        int height=Math.min(content,Math.min(dp(460),available*58/100));
        LinearLayout.LayoutParams lp=(LinearLayout.LayoutParams)controlScroll.getLayoutParams();
        if(lp.height!=height){lp.height=height;controlScroll.setLayoutParams(lp);}
    }
    private void fitLandscapeZoom(){
        // 先缩减留白；实际测量仍超高时，变焦栏最多再缩小 8dp，极小窗口保留滚动。
        int overflow=controlPanel.getHeight()-controlScroll.getHeight();
        if(overflow<=0 || controlScroll.getHeight()<=0)return;
        int current=((LinearLayout.LayoutParams)zoomRow.getLayoutParams()).height;
        int minimum=dp(40);if(current<=minimum)return;
        int reduced=Math.max(minimum,current-overflow);
        int size=Math.max(40,Math.min(47,Math.round(reduced/getResources().getDisplayMetrics().density)));
        setZoomSize(size,true);
    }
    @Override public void onConfigurationChanged(android.content.res.Configuration config){
        super.onConfigurationChanged(config);clearFocusIndicator();fitPreview();
        body.post(()->arrange(body.getWidth(),body.getHeight()));
    }
    private void tapFocus(float x,float y) {
        if(service==null || service.scanning()) return;
        float px=x-preview.getLeft(),py=y-preview.getTop();
        if(px<0 || py<0 || px>=preview.getWidth() || py>=preview.getHeight()) return;
        clearFocusIndicator(); int token=++focusToken;
        int mode=service.focusAt(px/preview.getWidth(),py/preview.getHeight(),ok -> {
            if(!resumed || token!=focusToken) return;
            focusColor=ok?0xFF35C979:0xFFFF6655; focusOverlay.invalidate();
        });
        if(mode==0) { Toast.makeText(this,"当前镜头无法执行点击对焦",Toast.LENGTH_SHORT).show(); return; }
        if(mode==2) Toast.makeText(this,"当前镜头仅支持普通自动对焦",Toast.LENGTH_SHORT).show();
        focusX=x; focusY=y; focusColor=0xFFFFCC44; focusOverlay.invalidate();
        statsHandler.postDelayed(() -> { if(token==focusToken) clearFocusIndicator(); },3500);
    }
    private void clearFocusIndicator() { focusToken++; focusColor=0; if(focusOverlay!=null) focusOverlay.invalidate(); }
    private int dp(int value) { return (int)(getResources().getDisplayMetrics().density*value); }
    private android.graphics.drawable.GradientDrawable roundBackground(int color) {
        android.graphics.drawable.GradientDrawable background=new android.graphics.drawable.GradientDrawable();
        background.setColor(color); background.setCornerRadius(dp(10)); return background;
    }
    private Button actionButton(String label,boolean primary) {
        Button button=new Button(this); button.setText(label); button.setTextSize(14); button.setAllCaps(false);
        button.setMinWidth(0); button.setMinimumWidth(0); button.setMinHeight(0); button.setMinimumHeight(0);
        button.setPadding(dp(4),0,dp(4),0); button.setSingleLine(true);
        button.setTextColor(primary?Color.WHITE:0xFF26364A);
        button.setBackground(new android.graphics.drawable.RippleDrawable(
            android.content.res.ColorStateList.valueOf(0x22000000),roundBackground(primary?0xFF1675D1:0xFFE5EAF0),null));
        return button;
    }
    private void updateRotateButton() {
        rotateButton.setText("旋转画面");
        boolean ready=service!=null && service.cameraReady();
        rotateButton.setContentDescription("画面顺时针旋转九十度");
        rotateButton.setEnabled(ready); rotateButton.setAlpha(ready?1f:0.45f);
    }
    private void updateZoomControls() {
        if(zoomBar==null) return;
        boolean ready=service!=null && service.zoomAvailable();
        int index=service==null?0:service.zoomTarget(),max=service==null?0:service.zoomMax();
        zoomBar.setMax(Math.max(1,max));
        if(!zoomDragging) zoomBar.setProgress(index);
        zoomBar.setEnabled(ready);
        zoomMinus.setEnabled(ready && index>0); zoomPlus.setEnabled(ready && index<max);
        zoomReset.setEnabled(ready);
        String label=String.format(java.util.Locale.ROOT,"%.2f",service==null?1:service.zoomRatio()).replaceAll("0$","")+"×";
        if(service!=null && service.cameraReady() && !service.zoomSupported()) label="不支持";
        zoomReset.setText(label); zoomReset.setContentDescription("当前 "+label+"，点击恢复一倍画面");
        zoomMinus.setAlpha(zoomMinus.isEnabled()?1f:0.4f); zoomPlus.setAlpha(zoomPlus.isEnabled()?1f:0.4f);
        zoomReset.setAlpha(ready?1f:0.4f);
    }
    private void rotatePicture() { clearFocusIndicator(); if(service!=null) service.rotatePicture(); }
    private void fitPreview() {
        int width=previewArea.getWidth(),height=previewArea.getHeight();
        if(width<=0 || height<=0) return;
        if(service==null) return;
        service.setDisplayRotation(getWindowManager().getDefaultDisplay().getRotation()*90);
        preview.setScaleX(service.frontFacing()?-1f:1f);
        int[] fitted=CameraRotation.fit(service.rawWidth(),service.rawHeight(),service.previewRotation(),width,height);
        FrameLayout.LayoutParams frame=(FrameLayout.LayoutParams)preview.getLayoutParams();
        int gravity=body.getOrientation()==LinearLayout.VERTICAL?(Gravity.TOP|Gravity.CENTER_HORIZONTAL):Gravity.CENTER;
        if(frame.width!=fitted[0] || frame.height!=fitted[1] || frame.gravity!=gravity) {
            frame.width=fitted[0]; frame.height=fitted[1]; frame.gravity=gravity; preview.setLayoutParams(frame);
        }

    }
    private void update(String message) {
        runOnUiThread(() -> {
            if(destroyed || !resumed || hub==null || service==null) return;
            boolean scanning=service.scanning(); fitPreview(); updateRotateButton(); updateZoomControls();
            // 常规连接状态只在电脑列表显示；错误仍以短暂提示告知用户。
            if(message.contains("失败") || message.contains("无法") || message.contains("中断") || message.contains("权限") || message.contains("最多") || message.contains("没有另一"))
                Toast.makeText(this,message,Toast.LENGTH_LONG).show();
            targetsTitle.setText("已连接电脑 · "+hub.connectedCount());
            scanButton.setText(scanning?"正在扫码":(hub.hasTargets()?"添加电脑":"扫码连接"));
            switchButton.setEnabled(!scanning); switchButton.setAlpha(scanning?0.45f:1f);
            targetRows.removeAllViews();
            for(String[] target:hub.targets()) {
                LinearLayout row=new LinearLayout(this); row.setGravity(Gravity.CENTER_VERTICAL);
                TextView name=new TextView(this); name.setText(target[1]); name.setTextSize(13); name.setTextColor(0xFF26364A);
                name.setMaxLines(1); name.setEllipsize(android.text.TextUtils.TruncateAt.END);
                // 长名称可长按查看完整文字，名称本身不会触发断开。
                name.setOnLongClickListener(v -> { Toast.makeText(this,target[1],Toast.LENGTH_LONG).show(); return true; });
                // 两行信息合计 48dp，与右侧断开按钮同高并垂直居中。
                LinearLayout details=new LinearLayout(this); details.setOrientation(LinearLayout.VERTICAL); details.setGravity(Gravity.CENTER_VERTICAL);
                details.addView(name,new LinearLayout.LayoutParams(-1,-2));
                TextView ip=new TextView(this); ip.setText(target[2].isEmpty()?"正在连接…":target[2]); ip.setTextSize(12); ip.setTextColor(0xFF64748B);
                ip.setSingleLine(true); ip.setEllipsize(android.text.TextUtils.TruncateAt.END);
                details.addView(ip,new LinearLayout.LayoutParams(-1,-2));
                row.addView(details,new LinearLayout.LayoutParams(0,dp(48),1));
                // 声音开关与单台断开按钮放在同一行。
                String soundState=target.length>3?target[3]:"off";
                Button sound=actionButton(soundState.equals("off")?"开启\n声音":(soundState.equals("on")?"关闭\n声音":"取消\n声音"),!soundState.equals("off"));
                sound.setSingleLine(false);sound.setMaxLines(2);sound.setTextSize(11);
                sound.setContentDescription((soundState.equals("off")?"开启":"关闭")+target[1]+"的声音");
                LinearLayout.LayoutParams soundSize=new LinearLayout.LayoutParams(dp(52),dp(48));soundSize.leftMargin=dp(4);row.addView(sound,soundSize);
                sound.setOnClickListener(v -> toggleSound(target[0]));
                Button remove=actionButton("断开",false); remove.setTextSize(12);
                remove.setContentDescription("断开电脑："+target[1]);
                LinearLayout.LayoutParams removeSize=new LinearLayout.LayoutParams(dp(48),dp(48)); removeSize.leftMargin=dp(4); row.addView(remove,removeSize);
                remove.setOnClickListener(v -> { if(service!=null) service.disconnect(target[0]); });
                LinearLayout.LayoutParams rowSize=new LinearLayout.LayoutParams(-1,-2); rowSize.topMargin=dp(4); targetRows.addView(row,rowSize);
            }
            if(!hub.hasTargets()) {
                TextView empty=new TextView(this); empty.setText("暂无连接\n请扫码添加电脑"); empty.setTextSize(13); empty.setTextColor(0xFF64748B);
                empty.setPadding(0,dp(16),0,0); targetRows.addView(empty);
            }
        });
    }
    private void toggleSound(String id) {
        if(service==null || hub==null)return;
        if(hub.audioEnabled(id)){service.setSound(id,false);return;}
        if(checkSelfPermission(Manifest.permission.RECORD_AUDIO)!=PackageManager.PERMISSION_GRANTED) {
            pendingAudioTarget=id;requestPermissions(new String[]{Manifest.permission.RECORD_AUDIO},3);
        } else service.setSound(id,true);
    }
    private void completeAudioPermission() {
        if(!resumed || service==null || pendingAudioTarget==null)return;
        if(checkSelfPermission(Manifest.permission.RECORD_AUDIO)!=PackageManager.PERMISSION_GRANTED)return;
        String target=pendingAudioTarget;pendingAudioTarget=null;service.setSound(target,true);
    }
    private void beginScan() {
        clearFocusIndicator();
        if(service!=null) service.beginScan(); else ensureService();
    }
    // 启动引导只自动展示一次；连接和统计刷新不再触发电池授权。
    @Override public void onWindowFocusChanged(boolean focused){
        super.onWindowFocusChanged(focused);
        if(focused && resumed && !getPreferences(MODE_PRIVATE).getBoolean("backgroundGuideSeen",false))showBackgroundGuide();
    }
    private void showBackgroundGuide(){
        if(isFinishing() || destroyed || (backgroundGuide!=null && backgroundGuide.isShowing()))return;
        getPreferences(MODE_PRIVATE).edit().putBoolean("backgroundGuideSeen",true).apply();
        boolean allowed=getSystemService(android.os.PowerManager.class).isIgnoringBatteryOptimizations(getPackageName());
        backgroundGuide=new android.app.AlertDialog.Builder(this)
            .setTitle("息屏继续传输")
            .setMessage("1. 在最近任务中锁定 PCam，避免被清理。\n2. 在系统应用电池设置中允许 PCam 后台运行，选择不限制。\n3. 允许忽略电池优化，减少息屏中断。\n\n"+
                (allowed?"电池优化：已允许，无需重复授权。":"电池优化：尚未允许，可点击下方按钮设置。")+
                "\n\n不同手机设置名称可能不同。锁定任务和后台权限需要你手动设置，不能保证系统永不中断。\n此提示只自动显示一次，之后可点击主页后台运行提示查看。")
            .setPositiveButton(allowed?"知道了":"允许后台运行",(dialog,which) -> {if(!allowed)requestBackgroundAccess();})
            .setNegativeButton("稍后",null).create();
        backgroundGuide.show();
    }
    private void requestBackgroundAccess(){
        if(getSystemService(android.os.PowerManager.class).isIgnoringBatteryOptimizations(getPackageName()))return;
        try{startActivity(new android.content.Intent(android.provider.Settings.ACTION_REQUEST_IGNORE_BATTERY_OPTIMIZATIONS,android.net.Uri.parse("package:"+getPackageName())));}
        catch(RuntimeException error){
            Diagnostics.write("background permission dialog unavailable: "+error.getClass().getSimpleName());
            Toast.makeText(this,"请在系统应用设置中允许 PCam 后台运行，并关闭电池限制",Toast.LENGTH_LONG).show();
        }
    }
    private void switchCamera() { clearFocusIndicator(); if(service!=null) service.switchCamera(); }
    private void ensureService() {
        if(!resumed || bound) return;
        if(checkSelfPermission(Manifest.permission.CAMERA)!=PackageManager.PERMISSION_GRANTED) {
            requestPermissions(new String[]{Manifest.permission.CAMERA},1); return;
        }
        try {
            android.content.Intent intent=new android.content.Intent(this,CameraService.class);
            startForegroundService(intent);
            bound=bindService(intent,connection,BIND_AUTO_CREATE);
            if(!bound) throw new IllegalStateException("bind failed");
        } catch(RuntimeException e) {
            Toast.makeText(this,"无法启动摄像服务，请保持应用在前台后重试",Toast.LENGTH_LONG).show();
            return;
        }
        // 通知权限不影响前台服务启动；允许后可从通知栏直接停止传输。
        if(android.os.Build.VERSION.SDK_INT>=33 && checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS)!=PackageManager.PERMISSION_GRANTED
            && !getPreferences(MODE_PRIVATE).getBoolean("notificationAsked",false)) {
            getPreferences(MODE_PRIVATE).edit().putBoolean("notificationAsked",true).apply();
            requestPermissions(new String[]{Manifest.permission.POST_NOTIFICATIONS},2);
        }
    }
    @Override public void surfaceCreated(SurfaceHolder h) {
        surfaceReady=true; if(service!=null && resumed) service.setPreview(h);
    }
    @Override public void surfaceChanged(SurfaceHolder h,int f,int w,int t) { previewArea.post(this::fitPreview); }
    @Override public void surfaceDestroyed(SurfaceHolder h) {
        surfaceReady=false; if(service!=null) service.setPreview(null);
    }
    @Override public void onRequestPermissionsResult(int r,String[] p,int[] results) {
        super.onRequestPermissionsResult(r,p,results);
        if(r==3) {
            if(results.length>0 && results[0]==PackageManager.PERMISSION_GRANTED) completeAudioPermission();
            else {pendingAudioTarget=null;Toast.makeText(this,"未开启声音，视频继续传输",Toast.LENGTH_SHORT).show();}
        }
        if(r==1) {
            if(results.length>0 && results[0]==PackageManager.PERMISSION_GRANTED) ensureService();
            else Toast.makeText(this,"扫码和传输需要摄像头权限",Toast.LENGTH_LONG).show();
        }
    }
    @Override protected void onResume() {
        super.onResume(); resumed=true; ensureService();
        statsHandler.removeCallbacks(statsTick); statsHandler.postDelayed(statsTick,1000);
    }
    @Override protected void onPause() {
        clearFocusIndicator();
        resumed=false; zoomDragging=false; statsHandler.removeCallbacks(statsTick);
        // 仅解除界面引用，已连接时服务继续采集和传输。
        if(service!=null) service.detach();
        if(bound) { unbindService(connection); bound=false; }
        service=null; hub=null; super.onPause();
    }
    @Override protected void onDestroy() { destroyed=true; if(backgroundGuide!=null)backgroundGuide.dismiss(); super.onDestroy(); }
}
