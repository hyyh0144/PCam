// PCam shared-frame protocol; used by the publisher and the adapted Microsoft sample media source.
#pragma once
#include <windows.h>
#include <algorithm>
inline constexpr UINT PCAM_WIDTH=1280,PCAM_HEIGHT=720,PCAM_STRIDE=PCAM_WIDTH*4;
inline constexpr ULONG PCAM_MAGIC=0x5043414D;
inline constexpr GUID PCAM_FRAME_PATH={0xd34f56b1,0xb375,0x45ac,{0x89,0x2e,0x5c,0x1b,0x35,0xb5,0x6b,0x90}};
inline constexpr wchar_t PCAM_CLSID[]=L"{E596E156-A65E-489A-A4FB-F2F4B0D23E01}";
struct PcamFrames{
    volatile LONG sequence;LONG slot;ULONG magic;ULONG reserved;ULONGLONG tick;BYTE padding[40];
    BYTE pixels[2][PCAM_STRIDE*PCAM_HEIGHT];
};
static_assert(offsetof(PcamFrames,pixels)==64);
