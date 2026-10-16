// 依据设备上报的非等距变焦档位映射手势倍率，不假设固定最大倍数。
package cn.phonecam;
import java.util.List;

public final class ZoomSteps {
    private final int[] ratios;
    public ZoomSteps(List<Integer> reported,int maxIndex) {
        int[] valid=null;
        if(reported!=null && maxIndex>0 && maxIndex<10000 && reported.size()==maxIndex+1) {
            valid=new int[reported.size()];
            for(int i=0;i<valid.length;i++) {
                Integer value=reported.get(i);
                if(value==null || value<100 || (i==0 && value!=100) || (i>0 && value<valid[i-1])) { valid=null; break; }
                valid[i]=value;
            }
        }
        ratios=valid==null?new int[]{100}:valid;
    }
    public int maxIndex() { return ratios.length-1; }
    public boolean supported() { return ratios[ratios.length-1]>100; }
    public int clamp(int index) { return Math.max(0,Math.min(maxIndex(),index)); }
    public double ratio(int index) { return ratios[clamp(index)]/100.0; }
    public int nearest(double ratio) {
        if(Double.isNaN(ratio) || ratio<=1) return 0;
        double target=ratio*100; int best=0;
        for(int i=1;i<ratios.length;i++) if(Math.abs(ratios[i]-target)<Math.abs(ratios[best]-target)) best=i;
        return ratio>=ratio(maxIndex())?maxIndex():best;
    }
}
