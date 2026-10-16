$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot
function CheckExit {if($LASTEXITCODE -ne 0){throw "构建工具退出码：$LASTEXITCODE"}}
& "$root/recorder/build-native.ps1" -HostedTools
& "$root/virtual-camera/build-native.ps1" -HostedTools
$nuget=Join-Path $PSScriptRoot 'NuGet.Config'
& dotnet restore "$root/tests/ProtocolTests.csproj" --configfile $nuget
CheckExit
& dotnet run --project "$root/tests/ProtocolTests.csproj" -c Release --no-restore
CheckExit
& dotnet restore "$root/windows/PhoneCam.csproj" -r win-x64 --configfile $nuget -p:SelfContained=true
CheckExit
& dotnet publish "$root/windows/PhoneCam.csproj" -c Release -r win-x64 --self-contained true --no-restore -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o "$root/releases/windows"
CheckExit
[xml]$project=Get-Content -LiteralPath "$root/windows/PhoneCam.csproj" -Raw
$version=@($project.Project.PropertyGroup.Version | Where-Object {$_})[0]
if(!$version){throw '缺少Windows版本号'}
$file=Join-Path $root "releases/PCam-$version.exe"
Copy-Item -LiteralPath "$root/releases/windows/PCam.exe" -Destination $file -Force
"$((Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash)  $([IO.Path]::GetFileName($file))" |
    Set-Content -LiteralPath "$root/releases/SHA256SUMS-windows.txt" -Encoding utf8
