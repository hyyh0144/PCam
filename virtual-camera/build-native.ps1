param([switch]$ProbeOnly, [switch]$HostedTools)
$ErrorActionPreference='Stop'
if($HostedTools){
    . "$PSScriptRoot/../ci/native-tools.ps1"
}else{
$tools=Join-Path (Split-Path (Split-Path $PSScriptRoot)) 'tools'
$vs=Join-Path $tools 'vs-buildtools'
$compiler=(Get-ChildItem "$tools/portable-msvc/Contents/VC/Tools/MSVC" -Directory | Sort-Object Name -Descending | Select-Object -First 1).FullName
$sdk=Join-Path $tools 'microsoft.windows.sdk.cpp/c'
$sdkLib=Join-Path $tools 'microsoft.windows.sdk.cpp.x64/c'
$sdkVersion=(Get-ChildItem "$sdk/Include" -Directory | Sort-Object Name -Descending | Select-Object -First 1).Name
$env:PATH="$compiler/bin/Hostx64/x64;$env:PATH"
$env:INCLUDE="$compiler/include;$sdk/Include/$sdkVersion/ucrt;$sdk/Include/$sdkVersion/shared;$sdk/Include/$sdkVersion/um;$sdk/Include/$sdkVersion/winrt;$sdk/Include/$sdkVersion/cppwinrt;$tools/wil/include"
$env:LIB="$compiler/lib/x64;$sdkLib/ucrt/x64;$sdkLib/um/x64"
$nativeCompiler=Join-Path $compiler 'bin/Hostx64/x64/cl.exe'
}
New-Item -ItemType Directory -Force -Path "$PSScriptRoot/build" | Out-Null
Push-Location "$PSScriptRoot/build"
try{
    $names=@('dllmain','winrtCommon','SimpleMediaSource','SimpleMediaStream','SimpleFrameGenerator','VirtualCameraMediaSourceActivate','PcamBridge')
    $sources=@($names | ForEach-Object {"$PSScriptRoot/$_.cpp"})
    if(!$ProbeOnly){
        & $nativeCompiler /nologo /std:c++17 /EHsc /O2 /MT /W3 /DUNICODE /D_UNICODE /DWIN32_LEAN_AND_MEAN /DNOMINMAX /LD /I"$PSScriptRoot" @sources /link /Brepro /NODEFAULTLIB:oldnames.lib "/DEF:$PSScriptRoot/VirtualCameraMediaSource.def" /OUT:PCamCamera.dll ole32.lib advapi32.lib mfreadwrite.lib shlwapi.lib
        if($LASTEXITCODE -ne 0){throw 'Native camera build failed'}
    }
    & $nativeCompiler /nologo /std:c++17 /EHsc /O2 /MT /DUNICODE /D_UNICODE /DNOMINMAX "$PSScriptRoot/CameraProbe.cpp" /link /Brepro /NODEFAULTLIB:oldnames.lib /OUT:CameraProbe.exe
    if($LASTEXITCODE -ne 0){throw 'Camera probe build failed'}
}finally{Pop-Location}
