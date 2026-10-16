// 验证 NV21 旋转同时保持亮度和 VU 对，覆盖矩形与四个方向。
import cn.phonecam.*;
import java.util.Arrays;
public final class StreamingTest {
    static void check(boolean ok,String text) { if(!ok) throw new AssertionError(text); }
    static byte[] reference90(byte[] src,int width,int height) {
        byte[] dst=new byte[src.length];
        int at=0;
        for(int x=0;x<width;x++) for(int y=height-1;y>=0;y--) dst[at++]=src[y*width+x];
        int base=width*height;
        for(int x=0;x<width;x+=2) for(int y=height/2-1;y>=0;y--) { dst[at++]=src[base+y*width+x]; dst[at++]=src[base+y*width+x+1]; }
        return dst;
    }
    public static void main(String[] args) {
        for(int[] shape:new int[][]{{4,2},{2,6},{8,4},{640,480}}) {
            int w=shape[0],h=shape[1]; byte[] src=new byte[w*h*3/2];
            for(int i=0;i<src.length;i++) src[i]=(byte)(i*17+3);
            byte[] expected=src.clone(); int rw=w,rh=h;
            for(int angle:new int[]{0,90,180,270}) {
                byte[] actual=new byte[src.length]; Nv21.rotate(src,actual,w,h,angle);
                check(Arrays.equals(actual,expected),"亮度/色度旋转不一致："+w+"x"+h+" "+angle);
                expected=reference90(expected,rw,rh); int temp=rw; rw=rh; rh=temp;
            }
            check(Arrays.equals(expected,src),"四次旋转未复原");
        }
        RateMeter meter=new RateMeter(0); meter.add(500_000,30);
        double[] rate=meter.sample(2_000_000_000L); check(rate[0]==2 && rate[1]==15,"码率/帧率时间换算错误");
        rate=meter.sample(3_000_000_000L); check(rate[0]==0 && rate[1]==0,"空闲统计未归零");
        byte[] src=new byte[640*480*3/2],dst=new byte[src.length];
        for(int i=0;i<100;i++) Nv21.rotate(src,dst,640,480,90);
        long start=System.nanoTime(); for(int i=0;i<300;i++) Nv21.rotate(src,dst,640,480,90);
        System.out.printf(java.util.Locale.ROOT,"PASS: NV21 Y/VU rotations, rate samples; desktop JVM rotation %.3f ms/frame (not a phone FPS claim)%n",(System.nanoTime()-start)/1e6/300);
    }
}
