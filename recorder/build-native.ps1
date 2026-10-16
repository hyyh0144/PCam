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
    & $nativeCompiler /nologo /std:c++17 /EHsc /O2 /MT /W3 /DUNICODE /D_UNICODE /DNOMINMAX /LD "$PSScriptRoot/Recorder.cpp" /link /Brepro /NODEFAULTLIB:oldnames.lib /OUT:PCamRecorder.dll ole32.lib oleaut32.lib mfreadwrite.lib mfplat.lib mfuuid.lib
    if($LASTEXITCODE -ne 0){throw 'Recorder build failed'}
    & $nativeCompiler /nologo /std:c++17 /EHsc /O2 /MT /DUNICODE /D_UNICODE /DNOMINMAX "$PSScriptRoot/RecordingProbe.cpp" /link /NODEFAULTLIB:oldnames.lib /OUT:RecordingProbe.exe ole32.lib oleaut32.lib mfreadwrite.lib mfplat.lib mfuuid.lib
    if($LASTEXITCODE -ne 0){throw 'Recording probe build failed'}
    & $nativeCompiler /nologo /std:c++17 /EHsc /O2 /MT /DUNICODE /D_UNICODE /DNOMINMAX "$PSScriptRoot/RecordingContracts.cpp" /link /NODEFAULTLIB:oldnames.lib /OUT:RecordingContracts.exe
    if($LASTEXITCODE -ne 0){throw 'Recording contracts build failed'}
}finally{Pop-Location}



