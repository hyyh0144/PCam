#include <windows.h>
#include <vector>
#include <cstdio>
#include <climits>
using Start=HRESULT(__stdcall*)(const wchar_t*,void**);
using Write=HRESULT(__stdcall*)(void*,const BYTE*,int,LONGLONG,LONGLONG,int);
using Stop=HRESULT(__stdcall*)(void*);
using Flush=HRESULT(__stdcall*)(void*);
int wmain(int argc,wchar_t** argv){
 if(argc!=3)return 2;HMODULE dll=LoadLibraryExW(argv[1],nullptr,LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR|LOAD_LIBRARY_SEARCH_SYSTEM32);if(!dll)return 3;
 auto start=(Start)GetProcAddress(dll,"RecStart");auto write=(Write)GetProcAddress(dll,"RecWrite");auto stop=(Stop)GetProcAddress(dll,"RecStop");auto flush=(Flush)GetProcAddress(dll,"RecFlush");if(!start||!write||!stop||!flush)return 4;
 void* context=(void*)1;
 if(start(L"",&context)!=E_INVALIDARG||context!=nullptr||start(argv[2],nullptr)!=E_POINTER||stop(nullptr)!=E_POINTER||flush(nullptr)!=E_POINTER)return 5;
 if(FAILED(start(argv[2],&context)))return 6;
 std::vector<BYTE> frame(1280*720*4);BYTE pcm[960]={};
 bool ok=write(context,nullptr,1,0,1,0)==E_POINTER && write(context,frame.data(),1,0,1,0)==E_INVALIDARG &&
  write(context,frame.data(),(int)frame.size(),0,1,2)==E_INVALIDARG &&write(context,pcm,960,-1,1,1)==E_INVALIDARG&&
  write(context,pcm,960,LLONG_MAX,2,1)==E_INVALIDARG&&SUCCEEDED(write(context,frame.data(),(int)frame.size(),0,333333,0))&&
  write(context,frame.data(),(int)frame.size(),0,333333,0)==E_INVALIDARG&&SUCCEEDED(write(context,pcm,960,0,200000,1))&&
  write(context,pcm,960,0,200000,1)==E_INVALIDARG;
 if(FAILED(flush(context)))ok=false;
 HRESULT hr=stop(context);FreeLibrary(dll);if(!ok||FAILED(hr))return 7;
 printf("PASS native null/size/flag/time overflow and timestamp regression checks\n");return 0;
}

