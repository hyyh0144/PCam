// 把已去镜像的显示坐标逆旋转回相机坐标；黑边不参与映射。
package cn.phonecam;
public final class FocusPoint {
    public static int[] area(double x,double y,int rotation) {
        double u=x,v=y;
        switch(CameraRotation.normalize(rotation)) {
            case 90: u=y; v=1-x; break;
            case 180: u=1-x; v=1-y; break;
            case 270: u=1-y; v=x; break;
        }
        int cx=(int)Math.round(Math.max(0,Math.min(1,u))*2000-1000);
        int cy=(int)Math.round(Math.max(0,Math.min(1,v))*2000-1000);
        int left=Math.max(-1000,Math.min(800,cx-100)),top=Math.max(-1000,Math.min(800,cy-100));
        return new int[]{left,top,left+200,top+200};
    }
}
