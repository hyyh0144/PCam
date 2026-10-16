// 以设备非等距倍率表验证档位边界、就近映射与异常能力数据。
import cn.phonecam.ZoomSteps;
import java.util.*;
public final class ZoomStepsTest {
    private static void check(boolean ok,String message) { if(!ok) throw new AssertionError(message); }
    public static void main(String[] args) {
        ZoomSteps z=new ZoomSteps(Arrays.asList(100,112,150,225,400),4);
        check(z.supported() && z.maxIndex()==4,"device range");
        check(z.ratio(0)==1 && z.ratio(4)==4,"actual ratios");
        check(z.nearest(1.13)==1 && z.nearest(2)==3 && z.nearest(3.5)==4,"nonuniform nearest");
        check(z.nearest(1.875)==2,"ties keep lower step");
        check(z.clamp(-100)==0 && z.clamp(Integer.MAX_VALUE)==4,"index clamps");
        check(z.nearest(0.5)==0 && z.nearest(100)==4,"ratio limits");
        check(z.nearest(Double.NaN)==0 && z.nearest(Double.NEGATIVE_INFINITY)==0 && z.nearest(Double.POSITIVE_INFINITY)==4,"invalid/extreme gestures");
        int previous=-1;
        for(int i=0;i<=500;i++) {
            double ratio=i/100.0; int index=z.nearest(ratio);
            check(index>=previous && index>=0 && index<=4,"monotonic bounded mapping"); previous=index;
            double distance=Math.abs(z.ratio(index)-ratio);
            for(int j=0;j<=4;j++) check(distance<=Math.abs(z.ratio(j)-ratio)+1e-9,"nearest optimality");
        }
        check(!new ZoomSteps(null,0).supported(),"unsupported camera");
        check(!new ZoomSteps(Arrays.asList(100),0).supported(),"one fixed step");
        check(!new ZoomSteps(Arrays.asList(100,200),2).supported(),"incomplete table");
        check(!new ZoomSteps(Arrays.asList(50,200),1).supported(),"invalid minimum");
        check(!new ZoomSteps(Arrays.asList(100,300,200),2).supported(),"decreasing table");
        check(!new ZoomSteps(Arrays.asList(100,null),1).supported(),"missing ratio");
        check(!new ZoomSteps(Arrays.asList(100,100),1).supported(),"no real zoom");
        ZoomSteps duplicate=new ZoomSteps(Arrays.asList(100,150,150,200),3);
        check(duplicate.nearest(1.5)==1 && duplicate.supported(),"duplicate ratio safety");
        // 多次轻微张开后仍能跨过设备档位，不将每次小手势单独舍入。
        double accumulated=1;
        for(int i=0;i<10;i++) accumulated*=1.01;
        check(z.nearest(accumulated)==1,"small gestures accumulate");
        System.out.println("PASS: nonuniform zoom ratios, nearest mapping, bounds, malformed capabilities, cumulative gestures");
    }
}
