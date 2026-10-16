// PCam recorder using Windows Media Foundation APIs (IMFSinkWriter / IMFByteStream / IMFSourceReader).
// No vendored encoder source; system APIs and project attribution are listed in ../SOURCES.md.
#include <windows.h>
#include <mfapi.h>
#include <mfidl.h>
#include <mfreadwrite.h>
#include <mferror.h>
#include <strmif.h>
#include <codecapi.h>
#include <wrl/client.h>
#include <new>
#include <memory>
#include <cstring>
#include <climits>

using Microsoft::WRL::ComPtr;
#define CHECK(x) do { HRESULT hr=(x); if(FAILED(hr))return hr; } while(0)

// Created, written and finalized only by the C# STA recording worker.
struct Recorder {
    ComPtr<IMFSinkWriter> writer;
    ComPtr<IMFByteStream> file;
    DWORD video=0,audio=0;
    LONGLONG videoEnd=0,audioEnd=0;
    bool com=false,mf=false;
    ~Recorder(){writer.Reset();file.Reset();if(mf)MFShutdown();if(com)CoUninitialize();}
};
static HRESULT VideoType(REFGUID subtype,IMFMediaType** result){
    ComPtr<IMFMediaType> type;
    CHECK(MFCreateMediaType(&type));
    CHECK(type->SetGUID(MF_MT_MAJOR_TYPE,MFMediaType_Video));
    CHECK(type->SetGUID(MF_MT_SUBTYPE,subtype));
    CHECK(type->SetUINT32(MF_MT_INTERLACE_MODE,MFVideoInterlace_Progressive));
    CHECK(MFSetAttributeSize(type.Get(),MF_MT_FRAME_SIZE,1280,720));
    CHECK(MFSetAttributeRatio(type.Get(),MF_MT_FRAME_RATE,30,1));
    CHECK(MFSetAttributeRatio(type.Get(),MF_MT_PIXEL_ASPECT_RATIO,1,1));
    if(subtype==MFVideoFormat_H264)CHECK(type->SetUINT32(MF_MT_AVG_BITRATE,5000000));
    else CHECK(type->SetUINT32(MF_MT_DEFAULT_STRIDE,1280*4));
    *result=type.Detach();return S_OK;
}
static HRESULT AudioType(REFGUID subtype,IMFMediaType** result){
    ComPtr<IMFMediaType> type;
    CHECK(MFCreateMediaType(&type));
    CHECK(type->SetGUID(MF_MT_MAJOR_TYPE,MFMediaType_Audio));
    CHECK(type->SetGUID(MF_MT_SUBTYPE,subtype));
    CHECK(type->SetUINT32(MF_MT_AUDIO_NUM_CHANNELS,1));
    CHECK(type->SetUINT32(MF_MT_AUDIO_SAMPLES_PER_SECOND,48000));
    CHECK(type->SetUINT32(MF_MT_AUDIO_BITS_PER_SAMPLE,16));
    CHECK(type->SetUINT32(MF_MT_AUDIO_AVG_BYTES_PER_SECOND,subtype==MFAudioFormat_AAC?12000:96000));
    if(subtype==MFAudioFormat_AAC)CHECK(type->SetUINT32(MF_MT_AAC_PAYLOAD_TYPE,0));
    else CHECK(type->SetUINT32(MF_MT_AUDIO_BLOCK_ALIGNMENT,2));
    *result=type.Detach();return S_OK;
}
static HRESULT Configure(Recorder* r,const wchar_t* path){
    CHECK(CoInitializeEx(nullptr,COINIT_APARTMENTTHREADED));r->com=true;
    CHECK(MFStartup(MF_VERSION));r->mf=true;
    // FAIL_IF_EXIST reserves the destination in one operation; an existence check alone would race.
    CHECK(MFCreateFile(MF_ACCESSMODE_WRITE,MF_OPENMODE_FAIL_IF_EXIST,MF_FILEFLAGS_NONE,path,&r->file));
    ComPtr<IMFAttributes> attrs;
    CHECK(MFCreateAttributes(&attrs,3));
    CHECK(attrs->SetUINT32(MF_SINK_WRITER_DISABLE_THROTTLING,TRUE));
    CHECK(attrs->SetGUID(MF_TRANSCODE_CONTAINERTYPE,MFTranscodeContainerType_FMPEG4));
    CHECK(attrs->SetUINT32(MF_LOW_LATENCY,TRUE));
    CHECK(MFCreateSinkWriterFromURL(path,r->file.Get(),attrs.Get(),&r->writer));
    ComPtr<IMFMediaType> out,in;
    CHECK(VideoType(MFVideoFormat_H264,&out));
    CHECK(r->writer->AddStream(out.Get(),&r->video));
    CHECK(VideoType(MFVideoFormat_RGB32,&in));
    CHECK(r->writer->SetInputMediaType(r->video,in.Get(),nullptr));
    out.Reset();in.Reset();
    CHECK(AudioType(MFAudioFormat_AAC,&out));
    CHECK(r->writer->AddStream(out.Get(),&r->audio));
    CHECK(AudioType(MFAudioFormat_PCM,&in));
    CHECK(r->writer->SetInputMediaType(r->audio,in.Get(),nullptr));
    CHECK(r->writer->BeginWriting());
    ComPtr<IMFAttributes> sinkAttributes;
    CHECK(r->writer->GetServiceForStream(MF_SINK_WRITER_MEDIASINK,GUID_NULL,IID_PPV_ARGS(&sinkAttributes)));
    CHECK(sinkAttributes->SetUINT64(MF_MPEG4SINK_MIN_FRAGMENT_DURATION,10000000));
    ComPtr<ICodecAPI> codec;
    CHECK(r->writer->GetServiceForStream(r->video,GUID_NULL,IID_PPV_ARGS(&codec)));
    VARIANT gop;VariantInit(&gop);gop.vt=VT_UI4;gop.ulVal=30;
    CHECK(codec->SetValue(&CODECAPI_AVEncMPVGOPSize,&gop));
    return S_OK;
}
extern "C" __declspec(dllexport) HRESULT __stdcall RecStart(const wchar_t* path,void** context){
    if(!context)return E_POINTER;
    *context=nullptr;if(!path||!*path)return E_INVALIDARG;
    std::unique_ptr<Recorder> r(new(std::nothrow) Recorder());
    if(!r)return E_OUTOFMEMORY;
    CHECK(Configure(r.get(),path));
    // On failure RAII closes handles. Do not delete by path afterwards: it could have been replaced.
    *context=r.release();return S_OK;
}
extern "C" __declspec(dllexport) HRESULT __stdcall RecWrite(void* context,const BYTE* bytes,int size,LONGLONG time,LONGLONG duration,int audio){
    if(!context||!bytes)return E_POINTER;
    if((audio!=0&&audio!=1)||size!=(audio?960:1280*720*4)||time<0||duration<=0||time>LLONG_MAX-duration)return E_INVALIDARG;
    auto r=static_cast<Recorder*>(context);
    LONGLONG& end=audio?r->audioEnd:r->videoEnd;
    if(time<end)return E_INVALIDARG;
    // 24 kHz phone PCM -> 48 kHz required by Windows AAC; 960 output samples still span 20 ms.
    short upsampled[960];
    if(audio){
        auto input=reinterpret_cast<const short*>(bytes);
        for(int n=0;n<480;n++){
            upsampled[n*2]=input[n];
            upsampled[n*2+1]=static_cast<short>((static_cast<int>(input[n])+input[n<479?n+1:n])/2);
        }
        bytes=reinterpret_cast<const BYTE*>(upsampled);size=1920;
    }
    ComPtr<IMFMediaBuffer> buffer;
    ComPtr<IMFSample> sample;
    CHECK(MFCreateMemoryBuffer(size,&buffer));
    BYTE* target=nullptr;
    CHECK(buffer->Lock(&target,nullptr,nullptr));
    memcpy(target,bytes,size);
    CHECK(buffer->Unlock());
    CHECK(buffer->SetCurrentLength(size));
    CHECK(MFCreateSample(&sample));
    CHECK(sample->AddBuffer(buffer.Get()));
    CHECK(sample->SetSampleTime(time));
    CHECK(sample->SetSampleDuration(duration));
    CHECK(r->writer->WriteSample(audio?r->audio:r->video,sample.Get()));
    end=time+duration;return S_OK;
}
extern "C" __declspec(dllexport) HRESULT __stdcall RecStop(void* context){
    if(!context)return E_POINTER;
    std::unique_ptr<Recorder> r(static_cast<Recorder*>(context));
    return r->writer->Finalize();
}

// Flush the FILE byte stream, not IMFSinkWriter::Flush (which discards pending media samples).
extern "C" __declspec(dllexport) HRESULT __stdcall RecFlush(void* context){
    if(!context)return E_POINTER;
    return static_cast<Recorder*>(context)->file->Flush();
}





static HRESULT ValidateVideo(const wchar_t* path){
    ComPtr<IMFSourceReader> reader;CHECK(MFCreateSourceReaderFromURL(path,nullptr,&reader));
    ComPtr<IMFMediaType> type;CHECK(reader->GetNativeMediaType(MF_SOURCE_READER_FIRST_VIDEO_STREAM,0,&type));
    GUID subtype;CHECK(type->GetGUID(MF_MT_SUBTYPE,&subtype));if(subtype!=MFVideoFormat_H264)return MF_E_INVALIDMEDIATYPE;
    UINT32 width=0,height=0;CHECK(MFGetAttributeSize(type.Get(),MF_MT_FRAME_SIZE,&width,&height));if(width!=1280||height!=720)return MF_E_INVALIDMEDIATYPE;
    CHECK(reader->SetStreamSelection(MF_SOURCE_READER_ALL_STREAMS,FALSE));CHECK(reader->SetStreamSelection(MF_SOURCE_READER_FIRST_VIDEO_STREAM,TRUE));
    type.Reset();CHECK(MFCreateMediaType(&type));CHECK(type->SetGUID(MF_MT_MAJOR_TYPE,MFMediaType_Video));CHECK(type->SetGUID(MF_MT_SUBTYPE,MFVideoFormat_NV12));CHECK(reader->SetCurrentMediaType(MF_SOURCE_READER_FIRST_VIDEO_STREAM,nullptr,type.Get()));
    for(int attempt=0;attempt<16;attempt++){DWORD flags=0;ComPtr<IMFSample> sample;CHECK(reader->ReadSample(MF_SOURCE_READER_FIRST_VIDEO_STREAM,0,nullptr,&flags,nullptr,&sample));if(sample)return S_OK;if(flags&MF_SOURCE_READERF_ENDOFSTREAM)break;}
    return MF_E_INVALID_FILE_FORMAT;
}
extern "C" __declspec(dllexport) HRESULT __stdcall RecValidate(const wchar_t* path){
    if(!path)return E_POINTER;HRESULT co=CoInitializeEx(nullptr,COINIT_MULTITHREADED);if(FAILED(co)&&co!=RPC_E_CHANGED_MODE)return co;
    HRESULT hr=MFStartup(MF_VERSION);if(SUCCEEDED(hr)){hr=ValidateVideo(path);MFShutdown();}if(SUCCEEDED(co))CoUninitialize();return hr;
}

