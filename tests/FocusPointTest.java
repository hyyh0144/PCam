// 用正向旋转作为独立参考，验证点击对焦的逆坐标映射。
import cn.phonecam.FocusPoint;
public final class FocusPointTest {
    static void check(boolean ok,String message) { if(!ok) throw new AssertionError(message); }
    public static void main(String[] args) {
        for(int rotation:new int[]{0,90,180,270}) {
            for(double u:new double[]{.2,.35,.7,.8}) for(double v:new double[]{.2,.4,.6,.8}) {
                double x=u,y=v;
                for(int i=0;i<rotation/90;i++) { double old=x; x=1-y; y=old; }
                int[] a=FocusPoint.area(x,y,rotation);
                check(Math.abs((a[0]+a[2])/2.0-(u*2000-1000))<=1,"horizontal position");
                check(Math.abs((a[1]+a[3])/2.0-(v*2000-1000))<=1,"vertical position");
            }
            for(double x:new double[]{0,1}) for(double y:new double[]{0,1}) {
                int[] a=FocusPoint.area(x,y,rotation);
                check(a[0]>=-1000 && a[1]>=-1000 && a[2]<=1000 && a[3]<=1000,"valid camera bounds");
                check(a[2]-a[0]==200 && a[3]-a[1]==200,"edge area size");
            }
        }
        System.out.println("PASS: 64 inverse-rotation points and 16 edge focus areas");
    }
}
