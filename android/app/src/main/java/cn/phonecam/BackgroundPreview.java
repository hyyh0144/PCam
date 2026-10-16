// 后台摄像预览接收器；独立 EGL 线程持续消费帧，避免 Surface 销毁或缓冲堵塞。
package cn.phonecam;
import android.graphics.SurfaceTexture;
import android.opengl.*;
import android.os.Handler;
import android.os.HandlerThread;
import java.util.concurrent.*;

final class BackgroundPreview implements AutoCloseable {
    private final HandlerThread thread=new HandlerThread("PCam-preview");
    private final Handler handler;
    private SurfaceTexture texture;
    private EGLDisplay display=EGL14.EGL_NO_DISPLAY;
    private EGLContext context=EGL14.EGL_NO_CONTEXT;
    private EGLSurface surface=EGL14.EGL_NO_SURFACE;
    private int textureId;
    private volatile boolean closed;
    private volatile boolean failed;
    boolean failed(){return failed;}
    BackgroundPreview() throws Exception {
        thread.start(); handler=new Handler(thread.getLooper());
        FutureTask<Void> init=new FutureTask<>(() -> { create(); return null; });
        handler.post(init);
        try { init.get(3,TimeUnit.SECONDS); }
        catch(Exception e) { close(); throw e; }
    }
    SurfaceTexture texture() { return texture; }
    private void create() {
        display=EGL14.eglGetDisplay(EGL14.EGL_DEFAULT_DISPLAY);
        int[] version=new int[2];
        if(!EGL14.eglInitialize(display,version,0,version,1)) throw new IllegalStateException("EGL init");
        int[] attrs={EGL14.EGL_RENDERABLE_TYPE,EGL14.EGL_OPENGL_ES2_BIT,EGL14.EGL_SURFACE_TYPE,EGL14.EGL_PBUFFER_BIT,
            EGL14.EGL_RED_SIZE,8,EGL14.EGL_GREEN_SIZE,8,EGL14.EGL_BLUE_SIZE,8,EGL14.EGL_NONE};
        EGLConfig[] configs=new EGLConfig[1]; int[] count=new int[1];
        if(!EGL14.eglChooseConfig(display,attrs,0,configs,0,1,count,0) || count[0]==0) throw new IllegalStateException("EGL config");
        context=EGL14.eglCreateContext(display,configs[0],EGL14.EGL_NO_CONTEXT,new int[]{EGL14.EGL_CONTEXT_CLIENT_VERSION,2,EGL14.EGL_NONE},0);
        surface=EGL14.eglCreatePbufferSurface(display,configs[0],new int[]{EGL14.EGL_WIDTH,1,EGL14.EGL_HEIGHT,1,EGL14.EGL_NONE},0);
        if(!EGL14.eglMakeCurrent(display,surface,surface,context)) throw new IllegalStateException("EGL current");
        int[] ids=new int[1]; GLES20.glGenTextures(1,ids,0); textureId=ids[0];
        GLES20.glBindTexture(GLES11Ext.GL_TEXTURE_EXTERNAL_OES,textureId);
        GLES20.glTexParameteri(GLES11Ext.GL_TEXTURE_EXTERNAL_OES,GLES20.GL_TEXTURE_MIN_FILTER,GLES20.GL_NEAREST);
        GLES20.glTexParameteri(GLES11Ext.GL_TEXTURE_EXTERNAL_OES,GLES20.GL_TEXTURE_MAG_FILTER,GLES20.GL_LINEAR);
        GLES20.glTexParameteri(GLES11Ext.GL_TEXTURE_EXTERNAL_OES,GLES20.GL_TEXTURE_WRAP_S,GLES20.GL_CLAMP_TO_EDGE);
        GLES20.glTexParameteri(GLES11Ext.GL_TEXTURE_EXTERNAL_OES,GLES20.GL_TEXTURE_WRAP_T,GLES20.GL_CLAMP_TO_EDGE);
        texture=new SurfaceTexture(textureId);
        texture.setOnFrameAvailableListener(t -> {
            if(!closed && !failed)try {t.updateTexImage();}catch(RuntimeException error){failed=true;Diagnostics.write("background texture failed: "+error.getClass().getSimpleName());}
        },handler);
    }
    @Override public void close() {
        if(closed) return; closed=true;
        handler.post(() -> {
            // Failed vendor cleanup must still reach the remaining releases and terminate the EGL thread.
            try{
                cleanup(() -> {if(texture!=null)texture.setOnFrameAvailableListener(null);});
                cleanup(() -> {if(texture!=null)texture.release();});texture=null;
                cleanup(() -> {if(textureId!=0)GLES20.glDeleteTextures(1,new int[]{textureId},0);});
                if(display!=EGL14.EGL_NO_DISPLAY){
                    cleanup(() -> EGL14.eglMakeCurrent(display,EGL14.EGL_NO_SURFACE,EGL14.EGL_NO_SURFACE,EGL14.EGL_NO_CONTEXT));
                    cleanup(() -> {if(surface!=EGL14.EGL_NO_SURFACE)EGL14.eglDestroySurface(display,surface);});
                    cleanup(() -> {if(context!=EGL14.EGL_NO_CONTEXT)EGL14.eglDestroyContext(display,context);});
                    cleanup(() -> EGL14.eglTerminate(display));cleanup(EGL14::eglReleaseThread);
                }
            }finally{thread.quitSafely();}
        });
    }
    private static void cleanup(Runnable action){try{action.run();}catch(RuntimeException error){Diagnostics.write("background preview release failed: "+error.getClass().getSimpleName());}}
}
