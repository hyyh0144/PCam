// 在原始亮度和 VU 色度平面上旋转，避免 JPEG 解码和二次压缩。
package cn.phonecam;
public final class Nv21 {
    private Nv21() {}
    public static void rotate(byte[] source,byte[] target,int width,int height,int degrees) {
        int angle=CameraRotation.normalize(degrees), pixels=width*height;
        if(width<=0 || height<=0 || width%2!=0 || height%2!=0 || source==target || source.length<pixels*3/2 || target.length<pixels*3/2) throw new IllegalArgumentException("NV21 参数非法");
        if(angle==0) { System.arraycopy(source,0,target,0,pixels*3/2); return; }
        rotatePlane(source,target,0,width,height,1,angle);
        rotatePlane(source,target,pixels,width/2,height/2,2,angle);
    }
    private static void rotatePlane(byte[] source,byte[] target,int start,int w,int h,int stride,int angle) {
        // 色度以 VU 对为一个单元旋转，不能分别旋转字节，否则颜色会错位。
        int outputWidth=angle==180?w:h;
        for(int y=0;y<h;y++) {
            int input=start+y*w*stride;
            int output=angle==90?start+(h-1-y)*stride:angle==180?start+((h-1-y)*w+w-1)*stride:start+((w-1)*h+y)*stride;
            int step=angle==90?outputWidth*stride:angle==180?-stride:-outputWidth*stride;
            for(int x=0;x<w;x++,input+=stride,output+=step) {
                target[output]=source[input]; if(stride==2) target[output+1]=source[input+1];
            }
        }
    }
}
