// 验证四方向、横竖尺寸、前摄镜像补偿和预览等比适配。
import cn.phonecam.CameraRotation;
import java.util.Arrays;
public final class CameraRotationTest {
    static void require(boolean ok,String message) { if(!ok) throw new AssertionError(message); }
    static int[][] clockwise(int[][] input) {
        int h=input.length,w=input[0].length; int[][] result=new int[w][h];
        for(int y=0;y<h;y++) for(int x=0;x<w;x++) result[x][h-y-1]=input[y][x];
        return result;
    }
    static int[][] rotate(int[][] input,int angle) { for(int i=0;i<angle/90;i++) input=clockwise(input); return input; }
    static int[][] mirror(int[][] input) {
        int h=input.length,w=input[0].length; int[][] result=new int[h][w];
        for(int y=0;y<h;y++) for(int x=0;x<w;x++) result[y][w-x-1]=input[y][x]; return result;
    }
    public static void main(String[] args) {
        int extra=0;
        int[] expected={90,180,270,0};
        for(int angle:expected) { extra=CameraRotation.next(extra); require(extra==angle,"按钮循环顺序错误"); }
        int[][] labelled={{1,2,3},{4,5,6}};
        require(Arrays.deepEquals(rotate(labelled,90),new int[][]{{4,1},{5,2},{6,3}}),"顺时针基准错误");
        for(int sensor:new int[]{0,90,180,270}) for(int offset:new int[]{0,90,180,270}) {
            int output=CameraRotation.output(sensor,offset);
            int[][] remote=rotate(labelled,output);
            int[][] front=mirror(rotate(mirror(labelled),CameraRotation.preview(output,true)));
            require(Arrays.deepEquals(front,remote),"前摄本地与远端方向不一致");
            require(Arrays.deepEquals(rotate(labelled,CameraRotation.preview(output,false)),remote),"后摄方向不一致");
            int[] shape=CameraRotation.size(3,2,output);
            require(shape[0]==remote[0].length && shape[1]==remote.length,"宽高交换错误");
            for(int[] area:new int[][]{{360,500},{500,200},{230,310}}) {
                int[] fit=CameraRotation.fit(640,480,output,area[0],area[1]);
                require(fit[0]<=area[0] && fit[1]<=area[1],"预览超出容器");
                int[] actual=CameraRotation.size(640,480,output);
                require(Math.abs(fit[0]*actual[1]-fit[1]*actual[0])<=640,"画面被拉伸");
            }
        }
        require(Arrays.equals(CameraRotation.size(640,480,CameraRotation.output(90,90)),new int[]{640,480}),"横拍不是横向帧");
        for(int output:new int[]{0,90,180,270})for(int display:new int[]{0,90,180,270}){
            int local=CameraRotation.local(output,display);
            require(CameraRotation.normalize(local+display)==output,"界面旋转改变了发送方向");
            require(Arrays.deepEquals(mirror(rotate(mirror(labelled),CameraRotation.preview(local,true))),rotate(labelled,local)),"横屏前摄镜像错误");
        }
        require(CameraRotation.normalize(-90)==270 && CameraRotation.normalize(450)==90,"角度规范化错误");
        try { CameraRotation.normalize(45); throw new AssertionError("允许了非直角旋转"); } catch(IllegalArgumentException expectedError) {}
        System.out.println("PASS: 4-step rotation, 16 sensor/offset combinations, front/back consistency, landscape dimensions, aspect-fit bounds");
    }
}
