//
// Copyright (C) Microsoft Corporation. All rights reserved.
//
// Based on Microsoft Windows-Camera / VirtualCamera.
// Reference: https://github.com/microsoft/Windows-Camera/blob/c1bd9e099f15c2ecf735216c7dd065cdb02a803f/Samples/VirtualCamera/VirtualCameraMediaSource/winrtCommon.cpp
// Microsoft sample portions: MIT; see Microsoft-LICENSE.txt.
// PCam changes: No runtime changes; source annotation added.
// Import revision was not recorded; reference revision is documented in ../SOURCES.md.

#include "pch.h"

WINRT_EXPORT namespace winrt
{
    template<> bool is_guid_of<IMFMediaSourceEx>(guid const& id) noexcept
    {
        return is_guid_of<IMFMediaSourceEx, IMFMediaSource, IMFMediaEventGenerator>(id);
    }

    template<> bool is_guid_of<IMFMediaStream2>(guid const& id) noexcept
    {
        return is_guid_of<IMFMediaStream2, IMFMediaStream, IMFMediaEventGenerator>(id);
    }

    template<> bool is_guid_of<IMFActivate>(guid const& id) noexcept
    {
        return is_guid_of<IMFActivate, IMFAttributes>(id);
    }
}
