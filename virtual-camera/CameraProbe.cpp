#include <windows.h>
#include <mfapi.h>
#include <mferror.h>
#include <mfidl.h>
#include <mfreadwrite.h>
#include <wrl/client.h>
#include <iostream>
#include <fstream>
#include <vector>
#include "PcamShared.h"
#pragma comment(lib,"ole32")
#pragma comment(lib,"mf")
#pragma comment(lib,"mfplat")
#pragma comment(lib,"mfuuid")
#pragma comment(lib,"mfreadwrite")
using Microsoft::WRL::ComPtr;
const wchar_t* step=L"initialize";
void Check(HRESULT hr){if(FAILED(hr)){std::wcerr<<step<<L": HRESULT 0x"<<std::hex<<hr<<std::endl;throw hr;}}
int Contracts(const wchar_t* path){
    HMODULE dll=LoadLibraryW(path);if(!dll)Check(HRESULT_FROM_WIN32(GetLastError()));
    using Start=HRESULT(__stdcall*)(LPCWSTR,LPCWSTR,void**);
    using Write=HRESULT(__stdcall*)(void*,const BYTE*,int);
    auto start=(Start)GetProcAddress(dll,"PcStart");
    auto write=(Write)GetProcAddress(dll,"PcWrite");
    if(!start||!write)throw E_FAIL;
    void* context=nullptr;
    if(start(nullptr,L"test",&context)!=E_POINTER || write(nullptr,nullptr,0)!=E_INVALIDARG)throw E_FAIL;
    using Factory=HRESULT(__stdcall*)(REFCLSID,REFIID,void**);
    auto factory=(Factory)GetProcAddress(dll,"DllGetClassObject");
    CLSID id{};Check(CLSIDFromString(PCAM_CLSID,&id));
    {
        ComPtr<IClassFactory> cls;Check(factory(id,IID_PPV_ARGS(&cls)));
        ComPtr<IMFActivate> activate;Check(cls->CreateInstance(nullptr,IID_PPV_ARGS(&activate)));
        ComPtr<IMFMediaSource> first,second;Check(activate->ActivateObject(IID_PPV_ARGS(&first)));
        Check(activate->ActivateObject(IID_PPV_ARGS(&second)));
        ComPtr<IUnknown> a,b;Check(first.As(&a));Check(second.As(&b));if(a.Get()!=b.Get())throw E_FAIL;
        ComPtr<IMFSampleAllocatorControl> control;Check(first.As(&control));
        if(control->SetDefaultAllocator(0,nullptr)!=E_POINTER)throw E_FAIL;
        Check(activate->ShutdownObject());DWORD flags=0;
        if(first->GetCharacteristics(&flags)!=MF_E_SHUTDOWN)throw E_FAIL;
        Check(activate->ShutdownObject());Check(first->Shutdown());
    }
    FreeLibrary(dll);
    std::wcout<<L"PASS: null arguments, cached activation, allocator null guard and idempotent shutdown"<<std::endl;
    return 0;
}

int wmain(int argc,wchar_t** argv){try{
    Check(CoInitializeEx(nullptr,COINIT_MULTITHREADED));Check(MFStartup(MF_VERSION));
    if(argc==3 && wcscmp(argv[1],L"--contracts")==0){int result=Contracts(argv[2]);MFShutdown();CoUninitialize();return result;}
    ComPtr<IMFMediaSource> source;bool direct=argc>=4 && wcscmp(argv[1],L"--direct")==0;
    if(direct){
        HMODULE dll=LoadLibraryW(argv[2]);if(!dll)Check(HRESULT_FROM_WIN32(GetLastError()));
        using Factory=HRESULT(__stdcall*)(REFCLSID,REFIID,void**);
        auto factory=(Factory)GetProcAddress(dll,"DllGetClassObject");if(!factory)throw E_FAIL;
        CLSID id{};Check(CLSIDFromString(PCAM_CLSID,&id));ComPtr<IClassFactory> cls;Check(factory(id,IID_PPV_ARGS(&cls)));
        ComPtr<IMFActivate> activate;Check(cls->CreateInstance(nullptr,IID_PPV_ARGS(&activate)));Check(activate->SetString(PCAM_FRAME_PATH,argv[3]));
        Check(activate->ActivateObject(IID_PPV_ARGS(&source)));
        ComPtr<IMFSampleAllocatorControl> control;Check(source.As(&control));ComPtr<IMFVideoSampleAllocator> allocator;
        Check(MFCreateVideoSampleAllocatorEx(IID_PPV_ARGS(&allocator)));Check(control->SetDefaultAllocator(0,allocator.Get()));
    }else{
    ComPtr<IMFAttributes> attr;Check(MFCreateAttributes(&attr,1));Check(attr->SetGUID(MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE,MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE_VIDCAP_GUID));
    IMFActivate** devices=nullptr;UINT count=0;Check(MFEnumDeviceSources(attr.Get(),&devices,&count));ComPtr<IMFActivate> selected;
    for(UINT i=0;i<count;i++){wchar_t* name=nullptr;UINT len=0;
        if(SUCCEEDED(devices[i]->GetAllocatedString(MF_DEVSOURCE_ATTRIBUTE_FRIENDLY_NAME,&name,&len))){bool match=wcsstr(name,L"PCam")!=nullptr;std::wcout<<(match?L"DEVICE: PCam virtual camera":L"DEVICE: other camera")<<std::endl;if(match)selected=devices[i];CoTaskMemFree(name);}devices[i]->Release();}
    CoTaskMemFree(devices);if(!selected){std::wcerr<<L"PCam camera not enumerated"<<std::endl;return 2;}
    if(argc>1 && wcscmp(argv[1],L"--enumerate")==0)return 0;
    step=L"activate system camera";Check(selected->ActivateObject(IID_PPV_ARGS(&source)));}
    int frames=(!direct && argc>2)?_wtoi(argv[2]):60;
    if(frames<1 || frames>9000)return 4;
    bool nv12=argc>(direct?5:3) && wcscmp(argv[direct?5:3],L"nv12")==0;
    step=L"create source reader";ComPtr<IMFSourceReader> reader;Check(MFCreateSourceReaderFromMediaSource(source.Get(),nullptr,&reader));
    Check(reader->SetStreamSelection(MF_SOURCE_READER_FIRST_VIDEO_STREAM,TRUE));
    ComPtr<IMFMediaType> type;Check(MFCreateMediaType(&type));Check(type->SetGUID(MF_MT_MAJOR_TYPE,MFMediaType_Video));Check(type->SetGUID(MF_MT_SUBTYPE,nv12?MFVideoFormat_NV12:MFVideoFormat_RGB32));
    step=L"select RGB32 type";Check(reader->SetCurrentMediaType(MF_SOURCE_READER_FIRST_VIDEO_STREAM,nullptr,type.Get()));
    Check(reader->GetCurrentMediaType(MF_SOURCE_READER_FIRST_VIDEO_STREAM,&type));UINT w=0,h=0;Check(MFGetAttributeSize(type.Get(),MF_MT_FRAME_SIZE,&w,&h));
    std::wcout<<L"FORMAT: "<<w<<L"x"<<h<<std::endl;if(w!=1280||h!=720)return 3;
    LONGLONG first=0,last=0;int nonblack=0;DWORD length=0;
    for(int i=0;i<frames;i++){
        if(i<2)std::wcout<<L"READ: "<<i<<std::endl;
        step=L"read sample";
        DWORD index=0,flags=0;LONGLONG time=0;ComPtr<IMFSample> sample;
        Check(reader->ReadSample(MF_SOURCE_READER_FIRST_VIDEO_STREAM,0,&index,&flags,&time,&sample));if(!sample){i--;continue;}
        if(i<2)std::wcout<<L"SAMPLE: "<<i<<std::endl;
        if(i==0)first=time;last=time;ComPtr<IMFMediaBuffer> buffer;Check(sample->ConvertToContiguousBuffer(&buffer));BYTE* bytes=nullptr;Check(buffer->Lock(&bytes,nullptr,&length));
        bool color=false;
        if(nv12){for(DWORD k=0;k<w*h && k<length;k++)if(bytes[k]>30){color=true;break;}}
        else{for(DWORD k=0;k+3<length;k+=4)if(bytes[k]>30||bytes[k+1]>30||bytes[k+2]>30){color=true;break;}}
        if(color)nonblack++;
        if((i+1)%300==0)std::wcout<<L"FRAMES: "<<i+1<<std::endl;
        if(i==10 && (direct?argc>=5:argc>=2)){std::ofstream file(direct?argv[4]:argv[1],std::ios::binary);file.write((char*)bytes,length);}
        Check(buffer->Unlock());
    }
    std::wcout<<(direct?L"PASS: direct media source, ":L"PASS: actual system camera, ")<<frames<<L" frames, nonblack="<<nonblack<<L", bytes="<<length<<L", duration="<<(last-first)/10000<<L"ms"<<std::endl;
    reader.Reset();source->Shutdown();source.Reset();MFShutdown();CoUninitialize();return 0;
}catch(...){return 1;}}

