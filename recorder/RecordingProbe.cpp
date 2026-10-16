#include <windows.h>
#include <mfapi.h>
#include <mfidl.h>
#include <mfreadwrite.h>
#include <wrl/client.h>
#include <cstdio>
#include <cmath>
using Microsoft::WRL::ComPtr;
#define OK(x) do{HRESULT h=(x);if(FAILED(h)){printf("FAIL line %d hr=%08lx\n",__LINE__,h);return 1;}}while(0)
int wmain(int argc,wchar_t** argv){if(argc!=2)return 2;OK(CoInitializeEx(nullptr,COINIT_MULTITHREADED));OK(MFStartup(MF_VERSION));ComPtr<IMFSourceReader> reader;OK(MFCreateSourceReaderFromURL(argv[1],nullptr,&reader));
PROPVARIANT duration;PropVariantInit(&duration);if(SUCCEEDED(reader->GetPresentationAttribute(MF_SOURCE_READER_MEDIASOURCE,MF_PD_DURATION,&duration))&&duration.vt==VT_UI8)printf("reported duration %.3fs\n",duration.uhVal.QuadPart/1e7);PropVariantClear(&duration);
ComPtr<IMFMediaType> native;OK(reader->GetNativeMediaType(MF_SOURCE_READER_FIRST_VIDEO_STREAM,0,&native));GUID subtype;OK(native->GetGUID(MF_MT_SUBTYPE,&subtype));if(subtype!=MFVideoFormat_H264)return 3;native.Reset();OK(reader->GetNativeMediaType(MF_SOURCE_READER_FIRST_AUDIO_STREAM,0,&native));OK(native->GetGUID(MF_MT_SUBTYPE,&subtype));if(subtype!=MFAudioFormat_AAC)return 4;
OK(reader->SetStreamSelection(MF_SOURCE_READER_ALL_STREAMS,FALSE));OK(reader->SetStreamSelection(MF_SOURCE_READER_FIRST_VIDEO_STREAM,TRUE));ComPtr<IMFMediaType> type;OK(MFCreateMediaType(&type));OK(type->SetGUID(MF_MT_MAJOR_TYPE,MFMediaType_Video));OK(type->SetGUID(MF_MT_SUBTYPE,MFVideoFormat_NV12));OK(reader->SetCurrentMediaType(MF_SOURCE_READER_FIRST_VIDEO_STREAM,nullptr,type.Get()));
long frames=0;LONGLONG last=0;while(true){DWORD flags=0;LONGLONG time=0;ComPtr<IMFSample> sample;OK(reader->ReadSample(MF_SOURCE_READER_FIRST_VIDEO_STREAM,0,nullptr,&flags,&time,&sample));if(flags&MF_SOURCE_READERF_ENDOFSTREAM)break;if(!sample)continue;if(frames==0){ComPtr<IMFMediaBuffer> b;OK(sample->ConvertToContiguousBuffer(&b));BYTE* data;OK(b->Lock(&data,nullptr,nullptr));int tl=data[100*1280+300],tr=data[100*1280+980],bl=data[600*1280+300],br=data[600*1280+980];printf("decoded corners Y=%d,%d,%d,%d\n",tl,tr,bl,br);OK(b->Unlock());if(!(tl>bl+20&&tr>tl+40&&br>tr+20))return 5;}frames++;last=time;}
printf("video frames=%ld end=%.3fs\n",frames,last/1e7);if(frames<80||last<28000000)return 6;
reader.Reset();OK(MFCreateSourceReaderFromURL(argv[1],nullptr,&reader));OK(reader->SetStreamSelection(MF_SOURCE_READER_ALL_STREAMS,FALSE));OK(reader->SetStreamSelection(MF_SOURCE_READER_FIRST_AUDIO_STREAM,TRUE));type.Reset();OK(MFCreateMediaType(&type));OK(type->SetGUID(MF_MT_MAJOR_TYPE,MFMediaType_Audio));OK(type->SetGUID(MF_MT_SUBTYPE,MFAudioFormat_PCM));OK(reader->SetCurrentMediaType(MF_SOURCE_READER_FIRST_AUDIO_STREAM,nullptr,type.Get()));double sums[3]={};long counts[3]={};while(true){DWORD flags=0;LONGLONG time=0;ComPtr<IMFSample> sample;OK(reader->ReadSample(MF_SOURCE_READER_FIRST_AUDIO_STREAM,0,nullptr,&flags,&time,&sample));if(flags&MF_SOURCE_READERF_ENDOFSTREAM)break;if(!sample)continue;double seconds=time/1e7;int bucket=seconds<0.8?0:seconds>1.2&&seconds<1.8?1:seconds>2.4?2:-1;if(bucket<0)continue;ComPtr<IMFMediaBuffer> b;OK(sample->ConvertToContiguousBuffer(&b));BYTE* data;DWORD size;OK(b->Lock(&data,nullptr,&size));auto pcm=(short*)data;for(DWORD n=0;n<size/2;n++){sums[bucket]+=(double)pcm[n]*pcm[n];counts[bucket]++;}OK(b->Unlock());}double rms[3];for(int i=0;i<3;i++){if(!counts[i])return 7;rms[i]=sqrt(sums[i]/counts[i]);}printf("audio RMS off/on/off=%.1f,%.1f,%.1f\n",rms[0],rms[1],rms[2]);if(rms[0]>100||rms[1]<1000||rms[2]>100)return 8;printf("PASS H264/AAC, duration, orientation and mobile audio toggle\n");reader.Reset();type.Reset();native.Reset();MFShutdown();CoUninitialize();return 0;}



