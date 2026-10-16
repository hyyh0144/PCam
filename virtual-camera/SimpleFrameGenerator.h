//
// Copyright (C) Microsoft Corporation. All rights reserved.
//
// Based on Microsoft Windows-Camera / VirtualCamera.
// Reference: https://github.com/microsoft/Windows-Camera/blob/c1bd9e099f15c2ecf735216c7dd065cdb02a803f/Samples/VirtualCamera/VirtualCameraMediaSource/SimpleFrameGenerator.h
// Microsoft sample portions: MIT; see Microsoft-LICENSE.txt.
// PCam changes: Added frame-path, mapping handles and cached-frame storage.
// Import revision was not recorded; reference revision is documented in ../SOURCES.md.

#pragma once
#ifndef SIMPLE_FRAME_GENERATOR_H
#define SIMPLE_FRAME_GENERATOR_H

class SimpleFrameGenerator
{
public:
    SimpleFrameGenerator() = default;
    ~SimpleFrameGenerator(){if(m_view)UnmapViewOfFile(m_view);if(m_mapping)CloseHandle(m_mapping);if(m_file!=INVALID_HANDLE_VALUE)CloseHandle(m_file);}
    void SetPath(std::wstring path){m_path=std::move(path);}

    HRESULT Initialize(_In_ IMFMediaType* pMediaType);

    HRESULT CreateFrame(
        _Inout_updates_bytes_(len) BYTE* pBuf,
        _In_ DWORD len,
        _In_ LONG pitch,
        _In_ ULONG rgbMask);

    // pixel format converter
    static void RGB24ToYUY2(int R, int G, int B, BYTE* pY, BYTE* pU, BYTE* pV);
    static void RGB24ToY(int R, int G, int B, BYTE* pY);
    static void RGB32ToNV12(BYTE RGB1[8], BYTE RGB2[8], BYTE* pY1, BYTE* pY2, BYTE* pUV);

    static HRESULT RGB32ToNV12Frame(_Inout_updates_bytes_(len) BYTE* pbBuff, ULONG cbBuff, long stride, UINT width, UINT height, BYTE* pbBuffOut, ULONG cbBuffOut, long strideOut);

private:
    std::vector<BYTE> m_lastFrame, m_rgbFrame;
    ULONGLONG m_lastTick=0;
    std::wstring m_path;
    HANDLE m_file=INVALID_HANDLE_VALUE,m_mapping=nullptr;
    const PcamFrames* m_view=nullptr; 
    HRESULT _CreateRGB32Frame(
        _Inout_updates_bytes_(len) BYTE* pBuf,
        _In_ DWORD len,
        _In_ LONG pitch,
        _In_ DWORD width,
        _In_ DWORD height,
        _In_ ULONG rgbMask);

    UINT32 m_width = 0;
    UINT32 m_height = 0;
    GUID m_subType = GUID_NULL;

};

#endif

