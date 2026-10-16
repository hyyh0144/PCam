// 预览与发送帧共用方向和尺寸计算，方便独立验证。
package cn.phonecam;
public final class CameraRotation {
    private CameraRotation() {}
    public static int normalize(int degrees) {
        if(degrees%90!=0) throw new IllegalArgumentException("旋转角度必须是 90 度的整数倍");
        return ((degrees%360)+360)%360;
    }
    public static int local(int output,int display){return normalize(output-display);}
    public static int next(int extra) { return normalize(extra+90); }
    public static int output(int sensor,int extra) { return normalize(sensor+extra); }
    // Camera 前摄先镜像再旋转；SurfaceView 再做水平翻转，使本地与发送端一致。
    public static int preview(int output,boolean front) { return normalize(front?-output:output); }
    public static int[] size(int rawWidth,int rawHeight,int rotation) {
        return normalize(rotation)%180==0?new int[]{rawWidth,rawHeight}:new int[]{rawHeight,rawWidth};
    }
    public static int[] fit(int rawWidth,int rawHeight,int rotation,int areaWidth,int areaHeight) {
        int[] size=size(rawWidth,rawHeight,rotation);
        double scale=Math.min((double)areaWidth/size[0],(double)areaHeight/size[1]);
        return new int[]{Math.max(1,(int)Math.round(size[0]*scale)),Math.max(1,(int)Math.round(size[1]*scale))};
    }
}
