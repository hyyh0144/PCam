//
// Copyright (C) Microsoft Corporation. All rights reserved.
//
// Based on Microsoft Windows-Camera / VirtualCamera.
// Reference: https://github.com/microsoft/Windows-Camera/blob/c1bd9e099f15c2ecf735216c7dd065cdb02a803f/Samples/VirtualCamera/VirtualCameraMediaSource/dllmain.cpp
// Microsoft sample portions: MIT; see Microsoft-LICENSE.txt.
// PCam changes: No runtime changes; source annotation added.
// Import revision was not recorded; reference revision is documented in ../SOURCES.md.
// 
// dllmain.cpp : Defines the entry point for the DLL application.
#include "pch.h"
#include "VirtualCameraMediaSourceActivate.h"

HINSTANCE   g_hInst;

BOOL APIENTRY DllMain( HMODULE hModule,
                       DWORD  ul_reason_for_call,
                       LPVOID lpReserved
                     )
{
    switch (ul_reason_for_call)
    {
    case DLL_PROCESS_ATTACH:
    case DLL_THREAD_ATTACH:
    case DLL_THREAD_DETACH:
        break;
    case DLL_PROCESS_DETACH:
        g_hInst = hModule;
    }
    return TRUE;
}

bool __stdcall winrt_can_unload_now() noexcept
{
    if (winrt::get_module_lock())
    {
        return false;
    }

    winrt::clear_factory_cache();
    return true;
}

int32_t __stdcall WINRT_CanUnloadNow() noexcept
{
#ifdef _WRL_MODULE_H_
    if (!::Microsoft::WRL::Module<::Microsoft::WRL::InProc>::GetModule().Terminate())
    {
        return 1;
    }
#endif

    return winrt_can_unload_now() ? 0 : 1;
}


HRESULT __stdcall DllGetClassObject(GUID const& clsid, GUID const& iid, void** result)
{
    try
    {
        *result = nullptr;

        if (clsid == __uuidof(winrt::WindowsSample::implementation::VirtualCameraMediaSourceActivate))
        {
            return winrt::make_self<VirtualCameraMediaSourceActivateFactory>()->QueryInterface(iid, result);
        }

#ifdef _WRL_MODULE_H_
        return ::Microsoft::WRL::Module<::Microsoft::WRL::InProc>::GetModule().GetClassObject(clsid, iid, result);
#else
        return winrt::hresult_class_not_available().to_abi();
#endif
    }
    catch (...)
    {
        return winrt::to_hresult();
    }
}


