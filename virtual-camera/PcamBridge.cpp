// PCam integration: publishes frames through the Windows Media Foundation virtual-camera API.
// Uses WIL / C++/WinRT SDK headers; imported sample files are listed separately in ../SOURCES.md.
#include "pch.h"
#include <memory>
#pragma comment(lib,"advapi32")
struct Publisher{
    wil::com_ptr_nothrow<IMFVirtualCamera> camera;
    HANDLE file=INVALID_HANDLE_VALUE,mapping=nullptr,mutex=nullptr;
    PcamFrames* view=nullptr;bool mf=false,com=false,owner=false;
    ~Publisher(){
        // Invalidate and wipe the mapped pixels before stopping the camera. A file
        // left by an abnormal exit is stale after two seconds, never replayed.
        if(view){InterlockedIncrement(&view->sequence);view->tick=0;
            SecureZeroMemory(view->pixels,sizeof(view->pixels));InterlockedIncrement(&view->sequence);}
        if(camera){camera->Shutdown();camera.reset();}
        if(view)UnmapViewOfFile(view);if(mapping)CloseHandle(mapping);if(file!=INVALID_HANDLE_VALUE)CloseHandle(file);
        if(mutex){if(owner)ReleaseMutex(mutex);CloseHandle(mutex);}if(mf)MFShutdown();if(com)CoUninitialize();
    }
};
extern "C" HRESULT __stdcall PcStart(LPCWSTR path,LPCWSTR sid,void** result){
 try{
    if(!path || !sid || !result)return E_POINTER;*result=nullptr;
    auto p=std::make_unique<Publisher>();auto hr=CoInitializeEx(nullptr,COINIT_APARTMENTTHREADED);
    if(FAILED(hr) && hr!=RPC_E_CHANGED_MODE)return hr;p->com=SUCCEEDED(hr);
    std::wstring name=L"Local\\PCam.VirtualCamera.Publisher.";name+=sid;
    p->mutex=CreateMutexW(nullptr,TRUE,name.c_str());if(!p->mutex)return HRESULT_FROM_WIN32(GetLastError());
    if(GetLastError()==ERROR_ALREADY_EXISTS)return HRESULT_FROM_WIN32(ERROR_BUSY);p->owner=true;
    // The reader must share our write access, but we do not share write/delete
    // access with other processes. This prevents concurrent truncation of a live mapping.
    p->file=CreateFileW(path,GENERIC_READ|GENERIC_WRITE,FILE_SHARE_READ,nullptr,OPEN_ALWAYS,
        FILE_ATTRIBUTE_TEMPORARY|FILE_FLAG_OPEN_REPARSE_POINT,nullptr);
    if(p->file==INVALID_HANDLE_VALUE)return HRESULT_FROM_WIN32(GetLastError());
    BY_HANDLE_FILE_INFORMATION info{};
    if(!GetFileInformationByHandle(p->file,&info))return HRESULT_FROM_WIN32(GetLastError());
    if((info.dwFileAttributes&FILE_ATTRIBUTE_REPARSE_POINT) || info.nNumberOfLinks!=1)return E_ACCESSDENIED;
    LARGE_INTEGER size{};size.QuadPart=sizeof(PcamFrames);
    if(!SetFilePointerEx(p->file,size,nullptr,FILE_BEGIN)||!SetEndOfFile(p->file))return HRESULT_FROM_WIN32(GetLastError());
    p->mapping=CreateFileMappingW(p->file,nullptr,PAGE_READWRITE,0,0,nullptr);if(!p->mapping)return HRESULT_FROM_WIN32(GetLastError());
    p->view=(PcamFrames*)MapViewOfFile(p->mapping,FILE_MAP_WRITE|FILE_MAP_READ,0,0,sizeof(PcamFrames));
    if(!p->view)return HRESULT_FROM_WIN32(GetLastError());ZeroMemory(p->view,sizeof(PcamFrames));p->view->magic=PCAM_MAGIC;
    RETURN_IF_FAILED(MFStartup(MF_VERSION));p->mf=true;
    RETURN_IF_FAILED(MFCreateVirtualCamera(MFVirtualCameraType_SoftwareCameraSource,MFVirtualCameraLifetime_Session,MFVirtualCameraAccess_CurrentUser,L"PCam",PCAM_CLSID,nullptr,0,&p->camera));
    RETURN_IF_FAILED(p->camera->SetString(PCAM_FRAME_PATH,path));
    RETURN_IF_FAILED(p->camera->Start(nullptr));*result=p.release();return S_OK;
 }catch(const std::bad_alloc&){return E_OUTOFMEMORY;}catch(...){return E_FAIL;}
}

extern "C" HRESULT __stdcall PcWrite(void* handle,const BYTE* pixels,int size){
    auto p=(Publisher*)handle;if(!p || !p->view || !pixels || size!=PCAM_STRIDE*PCAM_HEIGHT)return E_INVALIDARG;
    auto v=p->view;int slot=1-v->slot;InterlockedIncrement(&v->sequence);
    memcpy(v->pixels[slot],pixels,size);v->tick=GetTickCount64();v->slot=slot;InterlockedIncrement(&v->sequence);return S_OK;
}
extern "C" void __stdcall PcStop(void* handle){delete (Publisher*)handle;}
extern "C" HRESULT __stdcall PcRegister(){
    wchar_t path[32768]{};HMODULE module=nullptr;
    if(!GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS|GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,(LPCWSTR)&PcRegister,&module))return HRESULT_FROM_WIN32(GetLastError());
    if(!GetModuleFileNameW(module,path,32768))return HRESULT_FROM_WIN32(GetLastError());
    std::wstring key=L"SOFTWARE\\Classes\\CLSID\\";key+=PCAM_CLSID;key+=L"\\InprocServer32";
    HKEY reg=nullptr;auto code=RegCreateKeyExW(HKEY_LOCAL_MACHINE,key.c_str(),0,nullptr,0,KEY_SET_VALUE,nullptr,&reg,nullptr);
    if(code!=ERROR_SUCCESS)return HRESULT_FROM_WIN32(code);
    code=RegSetValueExW(reg,nullptr,0,REG_SZ,(BYTE*)path,(DWORD)((wcslen(path)+1)*sizeof(wchar_t)));
    if(code==ERROR_SUCCESS)code=RegSetValueExW(reg,L"ThreadingModel",0,REG_SZ,(const BYTE*)L"Both",10);
    RegCloseKey(reg);return HRESULT_FROM_WIN32(code);
}
extern "C" HRESULT __stdcall PcUnregister(){
    std::wstring key=L"SOFTWARE\\Classes\\CLSID\\";key+=PCAM_CLSID;
    auto code=RegDeleteTreeW(HKEY_LOCAL_MACHINE,key.c_str());return code==ERROR_FILE_NOT_FOUND?S_OK:HRESULT_FROM_WIN32(code);
}
