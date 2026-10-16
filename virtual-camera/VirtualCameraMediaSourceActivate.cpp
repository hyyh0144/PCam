//
// Copyright (C) Microsoft Corporation. All rights reserved.
//
// Based on Microsoft Windows-Camera / VirtualCamera.
// Reference: https://github.com/microsoft/Windows-Camera/blob/c1bd9e099f15c2ecf735216c7dd065cdb02a803f/Samples/VirtualCamera/VirtualCameraMediaSource/VirtualCameraMediaSourceActivate.cpp
// Microsoft sample portions: MIT; see Microsoft-LICENSE.txt.
// PCam changes: Retains the synthetic media source only; adds activation serialization.
// Import revision was not recorded; reference revision is documented in ../SOURCES.md.
#include "pch.h"

namespace winrt::WindowsSample::implementation
{
    // IMFActivate
    IFACEMETHODIMP VirtualCameraMediaSourceActivate::ActivateObject(REFIID riid, void** ppv)
    {
        winrt::slim_lock_guard lock(m_activationLock);
        RETURN_HR_IF_NULL(E_POINTER, ppv);
        *ppv = nullptr;

        // IMFActivate reuses its activated object until ShutdownObject/DetachObject.
        if(!m_spSimpleMediaSrc){
            auto source=winrt::make_self<winrt::WindowsSample::implementation::SimpleMediaSource>();
            RETURN_IF_FAILED(source->Initialize(this));
            m_spSimpleMediaSrc=std::move(source);
        }
        RETURN_IF_FAILED(m_spSimpleMediaSrc->QueryInterface(riid, ppv));
        return S_OK;
    }

    IFACEMETHODIMP VirtualCameraMediaSourceActivate::ShutdownObject()
    {
        winrt::slim_lock_guard lock(m_activationLock);
        if(m_spSimpleMediaSrc){m_spSimpleMediaSrc->Shutdown();m_spSimpleMediaSrc=nullptr;}
        return S_OK;
    }

    IFACEMETHODIMP VirtualCameraMediaSourceActivate::DetachObject()
    {
        winrt::slim_lock_guard lock(m_activationLock);
        m_spSimpleMediaSrc = nullptr;

        return S_OK;
    }

    // IMFAttributes (inherits by IMFActivate)
    IFACEMETHODIMP VirtualCameraMediaSourceActivate::GetItem(_In_ REFGUID guidKey, _Inout_opt_  PROPVARIANT* pValue)
    {
        return m_spActivateAttributes->GetItem(guidKey, pValue);
    }

    IFACEMETHODIMP VirtualCameraMediaSourceActivate::GetItemType(_In_ REFGUID guidKey, _Out_ MF_ATTRIBUTE_TYPE* pType)
    {
        return m_spActivateAttributes->GetItemType(guidKey, pType);
    }

    IFACEMETHODIMP VirtualCameraMediaSourceActivate::CompareItem(_In_ REFGUID guidKey, _In_ REFPROPVARIANT Value, _Out_ BOOL* pbResult)
    {
        return m_spActivateAttributes->CompareItem(guidKey, Value, pbResult);
    }

    IFACEMETHODIMP VirtualCameraMediaSourceActivate::Compare(_In_ IMFAttributes* pTheirs, _In_ MF_ATTRIBUTES_MATCH_TYPE MatchType, _Out_ BOOL* pbResult)
    {
        return m_spActivateAttributes->Compare(pTheirs, MatchType, pbResult);
    }

    IFACEMETHODIMP VirtualCameraMediaSourceActivate::GetUINT32(_In_ REFGUID guidKey, _Out_ UINT32* punValue)
    {
        return m_spActivateAttributes->GetUINT32(guidKey, punValue);
    }

    IFACEMETHODIMP VirtualCameraMediaSourceActivate::GetUINT64(_In_ REFGUID guidKey, _Out_ UINT64* punValue)
    {
        return m_spActivateAttributes->GetUINT64(guidKey, punValue);
    }

    IFACEMETHODIMP VirtualCameraMediaSourceActivate::GetDouble(_In_ REFGUID guidKey, _Out_ double* pfValue)
    {
        return m_spActivateAttributes->GetDouble(guidKey, pfValue);
    }
    IFACEMETHODIMP VirtualCameraMediaSourceActivate::GetGUID(_In_ REFGUID guidKey, _Out_ GUID* pguidValue)
    {
        return m_spActivateAttributes->GetGUID(guidKey, pguidValue);
    }

    IFACEMETHODIMP VirtualCameraMediaSourceActivate::GetStringLength(_In_ REFGUID guidKey, _Out_ UINT32* pcchLength)
    {
        return m_spActivateAttributes->GetStringLength(guidKey, pcchLength);
    }

    IFACEMETHODIMP VirtualCameraMediaSourceActivate::GetString(_In_ REFGUID guidKey, _Out_writes_(cchBufSize) LPWSTR pwszValue, _In_ UINT32 cchBufSize, _Inout_opt_ UINT32* pcchLength)
    {
        return m_spActivateAttributes->GetString(guidKey, pwszValue, cchBufSize, pcchLength);
    }

    IFACEMETHODIMP VirtualCameraMediaSourceActivate::GetAllocatedString(_In_ REFGUID guidKey, _Out_writes_(*pcchLength + 1) LPWSTR* ppwszValue, _Inout_  UINT32* pcchLength)
    {
        return m_spActivateAttributes->GetAllocatedString(guidKey, ppwszValue, pcchLength);
    }

    IFACEMETHODIMP VirtualCameraMediaSourceActivate::GetBlobSize(_In_ REFGUID guidKey, _Out_ UINT32* pcbBlobSize)
    {
        return m_spActivateAttributes->GetBlobSize(guidKey, pcbBlobSize);
    }

    IFACEMETHODIMP VirtualCameraMediaSourceActivate::GetBlob(_In_ REFGUID  guidKey, _Out_writes_(cbBufSize) UINT8* pBuf, UINT32 cbBufSize, _Inout_  UINT32* pcbBlobSize)
    {
        return m_spActivateAttributes->GetBlob(guidKey, pBuf, cbBufSize, pcbBlobSize);
    }

    IFACEMETHODIMP VirtualCameraMediaSourceActivate::GetAllocatedBlob(__RPC__in REFGUID guidKey, __RPC__deref_out_ecount_full_opt(*pcbSize) UINT8** ppBuf, __RPC__out UINT32* pcbSize)
    {
        return m_spActivateAttributes->GetAllocatedBlob(guidKey, ppBuf, pcbSize);
    }

    IFACEMETHODIMP VirtualCameraMediaSourceActivate::GetUnknown(__RPC__in REFGUID guidKey, __RPC__in REFIID riid, __RPC__deref_out_opt LPVOID* ppv)
    {
        return m_spActivateAttributes->GetUnknown(guidKey, riid, ppv);
    }

    IFACEMETHODIMP VirtualCameraMediaSourceActivate::SetItem(_In_ REFGUID guidKey, _In_ REFPROPVARIANT Value)
    {
        return m_spActivateAttributes->SetItem(guidKey, Value);
    }

    IFACEMETHODIMP VirtualCameraMediaSourceActivate::DeleteItem(_In_ REFGUID guidKey)
    {
        return m_spActivateAttributes->DeleteItem(guidKey);
    }

    IFACEMETHODIMP VirtualCameraMediaSourceActivate::DeleteAllItems()
    {
        return m_spActivateAttributes->DeleteAllItems();
    }

    IFACEMETHODIMP VirtualCameraMediaSourceActivate::SetUINT32(_In_ REFGUID guidKey, _In_ UINT32  unValue)
    {
        return m_spActivateAttributes->SetUINT32(guidKey, unValue);
    }

    IFACEMETHODIMP VirtualCameraMediaSourceActivate::SetUINT64(_In_ REFGUID guidKey, _In_ UINT64  unValue)
    {
        return m_spActivateAttributes->SetUINT64(guidKey, unValue);
    }

    IFACEMETHODIMP VirtualCameraMediaSourceActivate::SetDouble(_In_ REFGUID guidKey, _In_ double  fValue)
    {
        return m_spActivateAttributes->SetDouble(guidKey, fValue);
    }

    IFACEMETHODIMP VirtualCameraMediaSourceActivate::SetGUID(_In_ REFGUID guidKey, _In_ REFGUID guidValue)
    {
        return m_spActivateAttributes->SetGUID(guidKey, guidValue);
    }

    IFACEMETHODIMP VirtualCameraMediaSourceActivate::SetString(_In_ REFGUID guidKey, _In_ LPCWSTR wszValue)
    {
        return m_spActivateAttributes->SetString(guidKey, wszValue);
    }

    IFACEMETHODIMP VirtualCameraMediaSourceActivate::SetBlob(_In_ REFGUID guidKey, _In_reads_(cbBufSize) const UINT8* pBuf, UINT32 cbBufSize)
    {
        return m_spActivateAttributes->SetBlob(guidKey, pBuf, cbBufSize);
    }

    IFACEMETHODIMP VirtualCameraMediaSourceActivate::SetUnknown(_In_ REFGUID guidKey, _In_ IUnknown* pUnknown)
    {
        return m_spActivateAttributes->SetUnknown(guidKey, pUnknown);
    }

    IFACEMETHODIMP VirtualCameraMediaSourceActivate::LockStore()
    {
        return m_spActivateAttributes->LockStore();
    }

    IFACEMETHODIMP VirtualCameraMediaSourceActivate::UnlockStore()
    {
        return m_spActivateAttributes->UnlockStore();
    }

    IFACEMETHODIMP VirtualCameraMediaSourceActivate::GetCount(_Out_ UINT32* pcItems)
    {
        return m_spActivateAttributes->GetCount(pcItems);
    }

    IFACEMETHODIMP VirtualCameraMediaSourceActivate::GetItemByIndex(UINT32 unIndex, _Out_ GUID* pguidKey, _Inout_ PROPVARIANT* pValue)
    {
        return m_spActivateAttributes->GetItemByIndex(unIndex, pguidKey, pValue);
    }

    IFACEMETHODIMP VirtualCameraMediaSourceActivate::CopyAllItems(_In_ IMFAttributes* pDest)
    {
        return m_spActivateAttributes->CopyAllItems(pDest);
    }

    // non interface method
    HRESULT VirtualCameraMediaSourceActivate::Initialize()
    {
        RETURN_IF_FAILED(MFCreateAttributes(&m_spActivateAttributes, 3));
        RETURN_IF_FAILED(m_spActivateAttributes->SetUINT32(MF_VIRTUALCAMERA_PROVIDE_ASSOCIATED_CAMERA_SOURCES, 1));
        return S_OK;
    }
}
