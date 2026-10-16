// Audio playback: NAudio 2.2.1 (MIT), https://github.com/naudio/NAudio
// Library dependency; see ../THIRD-PARTY.md and ../licenses/NAudio-LICENSE.txt.
// 播放当前选中手机的 PCM，切换、静音或断开时立即清空旧音频。
using NAudio.Wave;
namespace PhoneCam;
public sealed class AudioPlayback : IDisposable {
    readonly object gate=new();
    Guid? selected;
    WaveOutEvent? output;
    BufferedWaveProvider? buffer;
    bool disposed,failed;
    public event Action<string>? Error;
    public void Select(Guid? id){lock(gate){selected=id;failed=false;Clear();}}
    public void Muted(Guid id){lock(gate)if(selected==id)Clear();}
    public void Add(Guid id,byte[] pcm) {
        lock(gate) {
            if(disposed || failed || selected!=id)return;
            try {
                if(output==null) {
                    buffer=new BufferedWaveProvider(new WaveFormat(24000,16,1)){BufferDuration=TimeSpan.FromMilliseconds(200),DiscardOnBufferOverflow=true};
                    output=new WaveOutEvent{DesiredLatency=80,NumberOfBuffers=3};output.Init(buffer);output.Play();
                }
                if(buffer!.BufferedDuration.TotalMilliseconds>120)buffer.ClearBuffer();
                buffer.AddSamples(pcm,0,pcm.Length);
            } catch(Exception) {Clear();failed=true;Error?.Invoke("声音播放失败，请检查电脑音频输出设备");}
        }
    }
    void Clear(){output?.Stop();output?.Dispose();output=null;buffer=null;}
    public void Dispose(){lock(gate){disposed=true;Clear();}}
}
