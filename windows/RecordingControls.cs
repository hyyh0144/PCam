using System.ComponentModel;
using System.IO;
using System.Windows;
using Microsoft.Win32;
namespace PhoneCam;
public partial class MainWindow
{
    VideoRecorder? recorder;
    string recordingFolder="",recordingFile="";
    bool recordingBusy,recordingCloseRequested,recordingCloseAllowed;
    void InitializeRecordingControls(){
        try{recordingFolder=RecordingSettings.Load();UpdateRecordingFolder();}catch(Exception e){RecordHint.Text="无法读取下载目录，请选择保存位置";Diagnostics.Write("recording folder: "+e.Message);}
        Closing+=RecordingClosing;
        Loaded+=async (_,_)=>await RecoverRecordings();
    }
    async Task RecoverRecordings(){
        try{
            var result=await Task.Run(()=>RecordingRecovery.Recover());
            if(closing||recorder!=null||recordingBusy)return;
            if(result.Recovered.Count>0||result.Preserved>0){RecordHint.Text=$"已恢复 {result.Recovered.Count} 个录像"+(result.Preserved>0?$" · {result.Preserved} 项临时数据已保留":"");RecordHint.ToolTip=string.Join(Environment.NewLine,result.Recovered.Take(10));}
        }catch(Exception e){if(!closing&&recorder==null)RecordHint.Text="录像恢复未完成，临时数据已保留";Diagnostics.Write("recording recovery: "+e.Message);}
    }
    void UpdateRecordingFolder(){RecordFolder.Text="保存到："+recordingFolder;RecordFolder.ToolTip=recordingFolder;}
    void ChooseRecordingFolder(object sender,RoutedEventArgs e){
        if(recordingBusy||recorder!=null||recordingCloseRequested)return;
        var dialog=new OpenFolderDialog{Title="选择视频录制保存位置",Multiselect=false};if(Directory.Exists(recordingFolder))dialog.InitialDirectory=recordingFolder;
        if(dialog.ShowDialog(this)!=true)return;recordingFolder=dialog.FolderName;UpdateRecordingFolder();
        try{RecordingSettings.Save(recordingFolder);RecordHint.Text="未录制 · 声音随手机开关";}catch(Exception error){RecordHint.Text="本次位置已设置，未能记住设置";Diagnostics.Write("recording settings: "+error.Message);}
    }
    async void ToggleRecording(object sender,RoutedEventArgs e){
        if(recordingBusy||recordingCloseRequested)return;if(recorder!=null){await FinishRecording();return;}
        if(composed==null||!composition.Snapshot().Any(l=>l.Online&&frames.ContainsKey(l.Id))){RecordHint.Text="请先连接手机并显示画面";return;}
        if(string.IsNullOrWhiteSpace(recordingFolder)){RecordHint.Text="请先选择保存位置";return;}
        recordingBusy=true;RecordToggle.IsEnabled=RecordLocation.IsEnabled=false;RecordHint.Text="正在开始录制…";
        var current=new VideoRecorder();recorder=current;
        _=ObserveRecording(current);
        try{
            recordingFile=Path.Combine(recordingFolder,"PCam_"+DateTime.Now.ToString("yyyyMMdd_HHmmss_fff")+"_"+Guid.NewGuid().ToString("N")[..6]+".mp4");
            await current.Start(recordingFile,composed.Image);RecordToggle.Content="停止录制";RecordHint.Text="录制中 00:00 · 声音随手机开关";RecordHint.ToolTip=recordingFile;
        }catch(Exception error){recorder=null;RecordingIdle();RecordHint.Text="无法开始录制："+error.Message;Diagnostics.Write("recording start: "+error);}
        finally{recordingBusy=false;RecordToggle.IsEnabled=!recordingCloseRequested;}
    }
    void RecordingIdle(){RecordToggle.Content="视频录制";RecordToggle.IsEnabled=RecordLocation.IsEnabled=!recordingCloseRequested;}
    async Task ObserveRecording(VideoRecorder current){
        try{await current.Completion;}
        catch(Exception error){
            // Start/stop owns errors while it is awaiting this same worker. Only asynchronous failures need a separate UI update.
            if(recorder!=current||recordingBusy||closing)return;
            recorder=null;RecordingIdle();RecordHint.Text="录制失败："+error.Message;Diagnostics.Write("recording: "+error);
        }
    }
    async Task FinishRecording(){
        var current=recorder;if(current==null)return;recordingBusy=true;RecordToggle.IsEnabled=RecordLocation.IsEnabled=false;RecordHint.Text="正在保存视频…";
        try{await current.Stop();RecordHint.Text="已保存："+Path.GetFileName(recordingFile);RecordHint.ToolTip=recordingFile;}
        catch(Exception error){RecordHint.Text="录制保存失败："+error.Message;Diagnostics.Write("recording stop: "+error);}
        finally{if(recorder==current)recorder=null;recordingBusy=false;RecordingIdle();}
    }
    async void RecordingClosing(object? sender,CancelEventArgs e){
        if(recordingCloseAllowed||recorder==null)return;e.Cancel=true;if(recordingCloseRequested)return;recordingCloseRequested=true;
        await FinishRecording();recordingCloseAllowed=true;Close();
    }
    void UpdateRecordingTime(){if(recorder!=null&&!recordingBusy){RecordHint.Text=recorder.StorageWarning!=null?"整合失败，临时录像仍在保存":"录制中 "+recorder.Elapsed.ToString(@"hh\:mm\:ss")+" · 每秒暂存 / 每分钟整合";}}
}




