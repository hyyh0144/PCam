. "$PSScriptRoot/build-env.ps1"
$ErrorActionPreference='Stop'
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
& "$PSScriptRoot/recorder/build-native.ps1"
& "$PSScriptRoot/virtual-camera/build-native.ps1"
if(!(Test-Path -LiteralPath "$PSScriptRoot/virtual-camera/build/PCamCamera.dll")){throw 'Missing native virtual camera component'}
$dotnet=Join-Path (Split-Path $PSScriptRoot) 'tools/dotnet/dotnet.exe'
& $dotnet publish "$PSScriptRoot/windows/PhoneCam.csproj" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o "$PSScriptRoot/releases/windows"
if($LASTEXITCODE -ne 0){throw 'Windows publish failed'}
Copy-Item -LiteralPath "$PSScriptRoot/releases/windows/PCam.exe" -Destination "$PSScriptRoot/releases/PCam-0.15.1.exe" -Force











