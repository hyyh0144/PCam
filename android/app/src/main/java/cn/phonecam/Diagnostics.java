// 滚动诊断日志；不记录配对码、视频或声音内容。
package cn.phonecam;
import android.content.Context;
import android.util.Log;
import java.io.*;
final class Diagnostics {
    private static File directory;
    static synchronized void init(Context context){File root=context.getExternalFilesDir(null);directory=new File(root==null?context.getFilesDir():root,"logs");directory.mkdirs();}
    static synchronized void write(String message){
        Log.i("PCam",message);
        if(directory==null)return;
        try{
            File file=new File(directory,"connection.log");
            if(file.length()>256*1024){File old=new File(directory,"connection.old.log");if(old.exists())old.delete();file.renameTo(old);}
            try(FileWriter output=new FileWriter(file,true)){output.write(new java.util.Date()+" "+message+"\n");}
        }catch(IOException|SecurityException ignored){} // Diagnostic storage must not become a second failure path.
    }
}
