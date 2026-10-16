// 后台合成叠加画面，预览与 OBS 共用输出，音频继续独立控制。
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
namespace PhoneCam;
public partial class MainWindow : Window
{
    readonly Receiver receiver = new();
    readonly AudioPlayback audio=new();
    readonly ObservableCollection<DeviceEntry> devices = new();
    readonly ConcurrentDictionary<Guid, byte[]> frames = new();
    readonly ConcurrentDictionary<Guid, RateMeter> meters = new();
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    readonly DispatcherTimer statsTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    readonly string computerId = Guid.NewGuid().ToString();
    WebApplication? web;
    Peer? selected;
    byte[]? composedJpeg;
    readonly Composition composition=new();
    readonly FrameComposer composer=new();
    readonly VirtualCamera cameraOutput=new();
    readonly Dictionary<Guid,Peer> known=new();
    readonly Dictionary<Guid,long> recovering=new();
    Dictionary<Guid,byte[]> previousFrames=new();
    int renderedRevision=-1;
    ComposedFrame? composed;
    bool syncing;
    bool closing;
    bool decoding;
    bool phoneSound=true;

    public MainWindow()
    {
        // Measure the sidebar on first opening, then retain normal user resizing.
        MaxHeight=Math.Max(1,SystemParameters.WorkArea.Height-16);
        InitializeComponent();
        InitializeRecordingControls();
        MinHeight=Math.Min(MinHeight,MaxHeight);
        ContentRendered += (_,_) => SizeToContent=SizeToContent.Manual;
        cameraOutput.Status+=message=>{if(!closing)_=Dispatcher.InvokeAsync(()=>CameraHint.Text=message);}; Devices.ItemsSource = devices; PreviewArea.SizeChanged+=(_,_)=>DrawHandles();
        receiver.Diagnostic += Diagnostics.Write;
        receiver.FrameStalled += peer => {frames.TryRemove(peer.Id,out _);};
        receiver.Audio += (peer,pcm) => {audio.Add(peer.Id,pcm);if(Volatile.Read(ref selected)?.Id==peer.Id)Volatile.Read(ref recorder)?.AddAudio(pcm);};
        receiver.AudioState += (peer,on) => {if(!on){audio.Muted(peer.Id);if(Volatile.Read(ref selected)?.Id==peer.Id)Volatile.Read(ref recorder)?.ClearAudio();}};
        audio.Error += message => {if(!closing)_=Dispatcher.InvokeAsync(()=>Status.Text=message);};
        receiver.Frame += (peer, frame) => { frames[peer.Id] = frame; meters.GetOrAdd(peer.Id, _ => new RateMeter()).Add(frame.Length + 4); };
        receiver.PeerChanged += (peer, connected) => {
            if (closing) return;
            _ = Dispatcher.InvokeAsync(() => {
                if (closing) return;
                bool restoreSelection=false;
                if (connected) {
                    var old=known.Values.FirstOrDefault(p=>p.DeviceId==peer.DeviceId && p.Id!=peer.Id);
                    if(old!=null){
                        restoreSelection=(Devices.SelectedItem as DeviceEntry)?.Peer.Id==old.Id;
                        composition.Rebind(old.Id,peer.Id);recovering.Remove(old.Id);known.Remove(old.Id);frames.TryRemove(old.Id,out _);meters.TryRemove(old.Id,out _);
                    }else composition.Connect(peer.Id);
                    known[peer.Id]=peer;recovering.Remove(peer.Id);
                } else {
                    if(!known.ContainsKey(peer.Id))return;
                    composition.Disconnect(peer.Id,true);recovering[peer.Id]=Environment.TickCount64+120000;
                    frames.TryRemove(peer.Id,out _);meters.TryRemove(peer.Id,out _);
                }
                if(!connected && composition.Main==peer.Id){Volatile.Write(ref composedJpeg,null);Preview.Source=null;}
                SyncDevices();if(restoreSelection)Devices.SelectedItem=devices.FirstOrDefault(d=>d.Peer.Id==peer.Id);UpdateCompositionHint();Render();
            });
        };
        timer.Tick += (_, _) => Render();
        statsTimer.Tick += (_, _) => {
            UpdateRecordingTime();
            bool expired=false;
            foreach(var id in recovering.Where(p=>Environment.TickCount64>=p.Value).Select(p=>p.Key).ToArray()){
                recovering.Remove(id);composition.Disconnect(id);if(composition.Main!=id)known.Remove(id);expired=true;
            }
            if(expired){SyncDevices();UpdateCompositionHint();}

            var peer = Volatile.Read(ref selected); var now = RateMeter.Now;
            foreach (var entry in meters) { var rate = entry.Value.Sample(now); if (peer?.Id == entry.Key) Stats.Text = $"接收 {rate.Mbps:F2} Mbps · {rate.Fps:F0} fps"; }
            if (peer == null) Stats.Text = "接收 0.00 Mbps · 0 fps";
        };
        Loaded += async (_, _) => {
            _=cameraOutput.Initialize();
            try {
                _ = RunReceiver();
                RefreshQr();
                var builder = WebApplication.CreateBuilder();
                builder.WebHost.UseUrls("http://127.0.0.1:0");
                web = builder.Build();
                VideoOutput.Map(web, CurrentFrame);
                await web.StartAsync();
                timer.Start(); statsTimer.Start();
            } catch (Exception e) { Status.Text = "启动失败：" + e.Message; receiver.Dispose(); }
        };
        Closed += async (_, _) => { closing = true; timer.Stop(); statsTimer.Stop(); receiver.Dispose(); audio.Dispose(); composer.Dispose();cameraOutput.Dispose(); if (web != null) await web.DisposeAsync(); };
    }
    async Task RunReceiver() { try { await receiver.Run(); } catch (Exception e) { if (!closing) await Dispatcher.InvokeAsync(() => Status.Text = "监听失败：" + e.Message); } }
    void RefreshQr()
    {
        var hosts = Pairing.LocalAddresses();
        if (hosts.Length == 0) { PairingQr.Source = null; Status.Text = "请先连接 Wi-Fi，再点击刷新二维码"; return; }
        PairingQr.Source = Decode(Pairing.Qr(Pairing.Payload(hosts, receiver.Port, receiver.Code, computerId, Environment.MachineName)));
        Status.Text = devices.Count == 0 ? "等待手机扫码" : $"已连接 {devices.Count} 部手机";
    }
    void Reset(object sender, RoutedEventArgs e) { audio.Select(null); receiver.Reset(); frames.Clear(); composition.Reset();known.Clear();recovering.Clear();composed=null;Volatile.Write(ref composedJpeg,null);Preview.Source=null;cameraOutput.Publish(null);recorder?.Publish(null);SyncDevices();UpdateCompositionHint();RefreshQr(); }
    void SelectDevice(object sender, SelectionChangedEventArgs e)=>UpdateSelectedDevice();
    void UpdateSelectedDevice() {
        if(syncing)return;
        var previous=selected?.Id;
        selected=(Devices.SelectedItem as DeviceEntry)?.Peer;
        if(selected!=null && !composition.IsOnline(selected.Id))selected=null;
        if(previous!=selected?.Id){Volatile.Read(ref recorder)?.ClearAudio();audio.Select(SoundSource.SelectedIndex==0 && phoneSound?selected?.Id:null);Stats.Text="接收 0.00 Mbps · 0 fps";}
        UpdateRotationControls();DrawHandles();
    }
    void ApplySoundChoice() {
        if(SoundToggle==null || SoundHint==null)return;
        bool phone=SoundSource.SelectedIndex==0;
        // 只切换电脑本地播放，不控制手机麦克风或关闭音频连接。
        audio.Select(phone && phoneSound?selected?.Id:null);
        SoundToggle.IsEnabled=phone;
        SoundToggle.Content=phoneSound?"静音手机声音":"播放手机声音";
        SoundHint.Text=phone?(phoneSound?"电脑播放当前手机的声音；麦克风由手机控制。":"电脑已静音，手机可独立开启或关闭声音。"):
            "请在会议软件中选择电脑麦克风；PCam 仅停止本地播放。";
    }
    void ChangeSoundSource(object sender,SelectionChangedEventArgs e) {ApplySoundChoice();}
    void ToggleSound(object sender,RoutedEventArgs e) {phoneSound=!phoneSound;ApplySoundChoice();}
    void UpdateRotationControls(){
        if(RotatePicture==null)return;bool ready=selected!=null && composition.IsOnline(selected.Id);
        RotatePicture.IsEnabled=RotationReset.IsEnabled=ready;
        RotationHint.Text=ready?$"本机修正 {composition.Rotation(selected!.Id)}° · 不影响手机及其他电脑":"选择手机后可独立旋转，输出画面同步。";
    }
    void RotateSelected(int delta){if(selected==null)return;composition.Rotate(selected.Id,delta);UpdateRotationControls();Render();}
    void RotateSelectedPicture(object sender,RoutedEventArgs e)=>RotateSelected(90);
    void ResetSelectedRotation(object sender,RoutedEventArgs e){if(selected==null)return;composition.ResetRotation(selected.Id);UpdateRotationControls();Render();}
    byte[]? CurrentFrame()=>Volatile.Read(ref composedJpeg);
    static BitmapImage Decode(byte[] bytes) { using var memory = new MemoryStream(bytes); var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.StreamSource = memory; bitmap.EndInit(); bitmap.Freeze(); return bitmap; }
    async void Render() {
        if(closing || decoding)return;
        var layers=composition.Snapshot();int revision=composition.Revision;
        if(layers.Length==0){Preview.Source=null;composed=null;Volatile.Write(ref composedJpeg,null);cameraOutput.Publish(null);recorder?.Publish(null);DrawHandles();return;}
        var input=new Dictionary<Guid,byte[]>();foreach(var layer in layers)if(layer.Online && frames.TryGetValue(layer.Id,out var bytes))input[layer.Id]=bytes;
        recorder?.Publish(composed?.Image);
        if(revision==renderedRevision && input.Count==previousFrames.Count && input.All(p=>previousFrames.TryGetValue(p.Key,out var old) && ReferenceEquals(old,p.Value)))return;
        decoding=true;
        try {
            var result=await composer.Compose(layers,input);
            if(!closing && revision==composition.Revision){composed=result;Preview.Source=result.Image;Volatile.Write(ref composedJpeg,result.Jpeg);cameraOutput.Publish(result.Image);recorder?.Publish(result.Image);renderedRevision=revision;previousFrames=input;DrawHandles();}
        }catch(Exception){if(!closing)Status.Text="画面合成失败，请检查手机画面后重试";}
        finally{decoding=false;}
    }
}



