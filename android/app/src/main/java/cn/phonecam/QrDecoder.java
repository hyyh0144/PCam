// QR decoding uses ZXing core 3.5.3 (Apache-2.0), https://github.com/zxing/zxing
// The decoder is linked from android/app/libs/core-3.5.3.jar; see THIRD-PARTY.md at the repository root.
// 直接识别预览的亮度平面，扫描不需要相册权限或外部扫码 APP。
package cn.phonecam;
import com.google.zxing.*;
import com.google.zxing.common.HybridBinarizer;
import com.google.zxing.qrcode.QRCodeReader;
import java.util.Collections;
public final class QrDecoder {
    public static String decode(byte[] nv21,int width,int height) throws ReaderException {
        PlanarYUVLuminanceSource source=new PlanarYUVLuminanceSource(nv21,width,height,0,0,width,height,false);
        return new QRCodeReader().decode(new BinaryBitmap(new HybridBinarizer(source)),Collections.singletonMap(DecodeHintType.TRY_HARDER,Boolean.TRUE)).getText();
    }
}
