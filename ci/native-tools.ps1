$ErrorActionPreference='Stop'
if($env:PCAM_HOSTED_NATIVE_READY -eq '1'){
    $nativeCompiler=(Get-Command cl.exe -ErrorAction Stop).Source
    return
}
$vswhere=Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
if(!(Test-Path -LiteralPath $vswhere)){throw '需要安装含MSVC的Visual Studio Build Tools'}
$vs=& $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if(!$vs){throw '找不到MSVC工具链'}
$devcmd=Join-Path $vs 'Common7/Tools/VsDevCmd.bat'
# Import the vendor toolchain environment into this process; no machine settings are changed.
$command='"{0}" -no_logo -arch=x64 -host_arch=x64 >nul && set' -f $devcmd
$toolEnvironment=& $env:ComSpec /d /s /c $command
if($LASTEXITCODE -ne 0){throw 'Visual Studio环境初始化失败'}
foreach($line in $toolEnvironment){
    if($line -match '^([^=]+)=(.*)$'){[Environment]::SetEnvironmentVariable($matches[1],$matches[2],'Process')}
}
$sdkVersion=$env:WindowsSDKVersion.TrimEnd('\')
$sdkInclude=Join-Path $env:WindowsSdkDir "Include/$sdkVersion"
if(!(Test-Path -LiteralPath "$sdkInclude/um/mfvirtualcamera.h")){throw '需要Windows 11 SDK的mfvirtualcamera.h'}
if(!(Test-Path -LiteralPath "$sdkInclude/cppwinrt/winrt/Windows.ApplicationModel.h")){throw '需要SDK附带的C++/WinRT头文件'}
$work=Join-Path $env:RUNNER_TEMP 'PCam-native'
$wilCommit='b6ec76a2d9a609897f25a7fa0a0bdf4238e94e35' # WIL v1.0.250325.1
$wilInclude=Join-Path $work "wil-$wilCommit/include"
if(!(Test-Path -LiteralPath "$wilInclude/wil/result.h")){
    New-Item -ItemType Directory -Path $work -Force | Out-Null
    $zip=Join-Path $work 'wil.zip'
    Invoke-WebRequest "https://github.com/microsoft/wil/archive/$wilCommit.zip" -OutFile $zip
    if((Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash -ne '51D603B207B207EBC8F464144F60593C33D1EF0FC69CB5959697351AEA18ACE8'){
        throw 'WIL下载摘要不匹配'
    }
    Expand-Archive -LiteralPath $zip -DestinationPath $work -Force
}
$env:INCLUDE="$wilInclude;$sdkInclude/cppwinrt;$env:INCLUDE"
$nativeCompiler=(Get-Command cl.exe -ErrorAction Stop).Source
$env:PCAM_HOSTED_NATIVE_READY='1'
