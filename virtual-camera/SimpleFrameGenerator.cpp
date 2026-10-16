//
// Copyright (C) Microsoft Corporation. All rights reserved.
//
// Based on Microsoft Windows-Camera / VirtualCamera.
// Reference: https://github.com/microsoft/Windows-Camera/blob/c1bd9e099f15c2ecf735216c7dd065cdb02a803f/Samples/VirtualCamera/VirtualCameraMediaSource/SimpleFrameGenerator.cpp
// Microsoft sample portions: MIT; see Microsoft-LICENSE.txt.
// PCam changes: Replaced synthetic test frames with PCam shared-memory frames; added bounds/stale-frame checks and reusable buffers. Color-conversion routines retain the sample implementation.
// Import revision was not recorded; reference revision is documented in ../SOURCES.md.
#include "pch.h"

HRESULT SimpleFrameGenerator::Initialize(_In_ IMFMediaType* pMediaType)
{
    RETURN_HR_IF_NULL(E_INVALIDARG, pMediaType);

    RETURN_IF_FAILED(pMediaType->GetGUID(MF_MT_SUBTYPE, &m_subType));
    if (m_subType != MFVideoFormat_RGB32 && m_subType != MFVideoFormat_NV12)
    {
        RETURN_HR_MSG(MF_E_UNSUPPORTED_FORMAT, "Unsupported format: %s", winrt::to_hstring(m_subType).data());
    }
    RETURN_IF_FAILED(MFGetAttributeSize(pMediaType, MF_MT_FRAME_SIZE, &m_width, &m_height));
    RETURN_HR_IF(MF_E_INVALIDMEDIATYPE,m_width!=PCAM_WIDTH || m_height!=PCAM_HEIGHT);
    try{m_lastFrame.resize(PCAM_STRIDE*PCAM_HEIGHT);m_rgbFrame.resize(PCAM_STRIDE*PCAM_HEIGHT);}
    catch(const std::bad_alloc&){return E_OUTOFMEMORY;}

    return S_OK;
}

// Output is top-down RGB32 or NV12 with a positive pitch supplied by MF.
// Fixed dimensions are validated before a buffer is touched.
HRESULT SimpleFrameGenerator::CreateFrame(
    _Inout_updates_bytes_(len) BYTE* pBuf,
    _In_ DWORD len,
    _In_ LONG pitch,
    _In_ ULONG rgbMask)
{
    if (m_subType == MFVideoFormat_RGB32)
    {
        DEBUG_MSG(L"RGB32 frames %s\n", winrt::to_hstring(MFVideoFormat_RGB32).data());

        RETURN_IF_FAILED(_CreateRGB32Frame(pBuf, len, pitch, m_width, m_height, rgbMask));
    }
    else if(m_subType == MFVideoFormat_NV12)
    {
        DEBUG_MSG(L"NV12 frames %s \n", winrt::to_hstring(MFVideoFormat_NV12).data());

        DWORD frameBuffLen = PCAM_STRIDE * PCAM_HEIGHT;
        BYTE* rgb=m_rgbFrame.data();

        RETURN_IF_FAILED(_CreateRGB32Frame(rgb, frameBuffLen, m_width * 4, m_width, m_height, rgbMask));
        RETURN_IF_FAILED(RGB32ToNV12Frame(rgb, frameBuffLen, m_width * 4, m_width, m_height, pBuf, len, pitch));
    }
    else
    {
        return MF_E_UNSUPPORTED_FORMAT;
    }

    return S_OK;
}

//////////////////////////////////////////////////
// private

HRESULT SimpleFrameGenerator::_CreateRGB32Frame(
    _Inout_updates_bytes_(len) BYTE* pBuf,
    _In_ DWORD len,
    _In_ LONG pitch,
    _In_ DWORD width,
    _In_ DWORD height,
    _In_ ULONG rgbMask )
{
    RETURN_HR_IF_NULL(E_INVALIDARG, pBuf);
    if (width!=PCAM_WIDTH || height!=PCAM_HEIGHT || pitch<(LONG)PCAM_STRIDE ||
        static_cast<ULONGLONG>(pitch)*(height-1)+PCAM_STRIDE>len)
    {
        return HRESULT_FROM_WIN32(ERROR_INSUFFICIENT_BUFFER);
    }

    ZeroMemory(pBuf,len);
    if(!m_view && !m_path.empty()){
        m_file=CreateFileW(m_path.c_str(),GENERIC_READ,FILE_SHARE_READ|FILE_SHARE_WRITE|FILE_SHARE_DELETE,nullptr,OPEN_EXISTING,FILE_ATTRIBUTE_NORMAL,nullptr);
        if(m_file!=INVALID_HANDLE_VALUE){LARGE_INTEGER size{};
            if(GetFileSizeEx(m_file,&size) && size.QuadPart>=sizeof(PcamFrames)){
                m_mapping=CreateFileMappingW(m_file,nullptr,PAGE_READONLY,0,0,nullptr);
                if(m_mapping)m_view=(const PcamFrames*)MapViewOfFile(m_mapping,FILE_MAP_READ,0,0,sizeof(PcamFrames));
            }
            if(!m_view){if(m_mapping){CloseHandle(m_mapping);m_mapping=nullptr;}CloseHandle(m_file);m_file=INVALID_HANDLE_VALUE;}
        }
    }
    if(!m_view || width!=PCAM_WIDTH || height!=PCAM_HEIGHT || pitch<(LONG)PCAM_STRIDE)return S_OK;
    for(int attempt=0;attempt<3;attempt++){
        LONG sequence=m_view->sequence;MemoryBarrier();if(sequence&1)continue;
        auto time=m_view->tick;int slot=m_view->slot;
        if(m_view->magic!=PCAM_MAGIC || slot<0 || slot>1 || time==0 || GetTickCount64()-time>2000)return S_OK;
        auto source=m_view->pixels[slot];
        for(UINT32 y=0;y<height;y++)memcpy(pBuf+y*pitch,source+y*PCAM_STRIDE,PCAM_STRIDE);
        MemoryBarrier();if(sequence==m_view->sequence){
            for(UINT32 y=0;y<height;y++)memcpy(m_lastFrame.data()+y*PCAM_STRIDE,pBuf+y*pitch,PCAM_STRIDE);
            m_lastTick=time;return S_OK;
        }
    }
    ZeroMemory(pBuf,len);
    // A concurrent publisher write may invalidate a snapshot. Retain the last
    // complete frame briefly rather than introduce a one-frame black flash.
    if(m_lastTick && GetTickCount64()-m_lastTick<=2000){
        for(UINT32 y=0;y<height;y++)memcpy(pBuf+y*pitch,m_lastFrame.data()+y*PCAM_STRIDE,PCAM_STRIDE);
    }
    return S_OK;
}

//////////////////////////////////////////////////
// pixelFormatConverter

void SimpleFrameGenerator::RGB24ToYUY2(int R, int G, int B, BYTE* pY, BYTE* pU, BYTE* pV)
{
    *pY = ((66 * R + 129 * G + 25 * B + 128) >> 8) + 16;
    *pU = ((-38 * R - 74 * G + 112 * B + 128) >> 8) + 128;
    *pV = ((112 * R - 94 * G - 18 * B + 128) >> 8) + 128;
}

void SimpleFrameGenerator::RGB24ToY(int R, int G, int B, BYTE* pY)
{
    *pY = ((66 * R + 129 * G + 25 * B + 128) >> 8) + 16;
}

void SimpleFrameGenerator::RGB32ToNV12(BYTE RGB1[8], BYTE RGB2[8], BYTE* pY1, BYTE* pY2, BYTE* pUV)
{
    RGB24ToYUY2(RGB1[2], RGB1[1], RGB1[0], pY1, pUV, pUV + 1);
    RGB24ToY(RGB1[6], RGB1[5], RGB1[4], pY1 + 1);
    RGB24ToYUY2(RGB2[2], RGB2[1], RGB2[0], pY2, pUV, pUV + 1);
    RGB24ToY(RGB2[6], RGB2[5], RGB2[4], pY2 + 1);
};

//////////////////////////////////////////////////
// FrameFormatConverter

HRESULT SimpleFrameGenerator::RGB32ToNV12Frame(_Inout_updates_bytes_(len) BYTE* pbBuff, ULONG cbBuff, long stride, UINT width, UINT height, BYTE* pbBuffOut, ULONG cbBuffOut, long strideOut)
{
    do
    {
        RETURN_HR_IF(E_INVALIDARG,width<2 || height<2 || (width&1) || (height&1) || stride<0 || strideOut<0);
        RETURN_HR_IF(E_INVALIDARG,static_cast<ULONGLONG>(width)*4>static_cast<ULONG>(stride) || width>static_cast<ULONG>(strideOut));
        RETURN_HR_IF(E_UNEXPECTED,static_cast<ULONGLONG>(stride)*(height-1)+static_cast<ULONGLONG>(width)*4>cbBuff);
        RETURN_HR_IF(E_UNEXPECTED,static_cast<ULONGLONG>(strideOut)*(height+height/2)>cbBuffOut);
        RETURN_HR_IF_NULL(E_INVALIDARG, pbBuff);

        RETURN_HR_IF_NULL(E_INVALIDARG, pbBuffOut);
        for (DWORD h = 0; h < height - 1; h += 2)
        {
            BYTE* pRGB1 = h * stride + pbBuff;
            BYTE* pRGB2 = (h + 1) * stride + pbBuff;
            BYTE* pY1 = h * strideOut + pbBuffOut;
            BYTE* pY2 = (h + 1) * strideOut + pbBuffOut;
            BYTE* pUV = (h / 2 + height) * strideOut + pbBuffOut;

            for (DWORD w = 0; w < width; w += 2)
            {
                RGB32ToNV12(pRGB1, pRGB2, pY1, pY2, pUV);
                pRGB1 += 8;
                pRGB2 += 8;
                pY1 += 2;
                pY2 += 2;
                pUV += 2;
            }
        }
    } while (FALSE);

    return S_OK;
}
