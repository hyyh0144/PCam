param([string]$Dotnet='dotnet',[string]$Jdk=$env:JAVA_HOME)
$ErrorActionPreference='Stop'
if (!$Jdk) { throw '请指定 JDK 17' }
& $Dotnet build "$PSScriptRoot/ProtocolTests.csproj" -c Release
if ($LASTEXITCODE -ne 0) { throw 'C# 测试编译失败' }
$stage=Join-Path $PSScriptRoot ('build/cross-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $stage | Out-Null
$zxing=(Resolve-Path "$PSScriptRoot/../android/app/libs/core-3.5.3.jar").Path
$source=(Resolve-Path "$PSScriptRoot/../android/app/src/main/java/cn/phonecam").Path
& "$Jdk/bin/javac.exe" -encoding UTF-8 --release 8 -cp $zxing -d $stage "$source/PairingInfo.java" "$source/QrDecoder.java" "$source/ConnectionHub.java" "$source/AudioSender.java" "$source/RateMeter.java" "$PSScriptRoot/CrossPlatformTest.java"
if ($LASTEXITCODE -ne 0) { throw 'Java 测试编译失败' }
$dll=(Resolve-Path "$PSScriptRoot/bin/Release/net8.0/ProtocolTests.dll").Path
$process=Start-Process -FilePath $Dotnet -ArgumentList @('"'+$dll+'"','--fixture','"'+$stage+'"') -WindowStyle Hidden -PassThru -RedirectStandardOutput "$stage/server.log" -RedirectStandardError "$stage/error.log"
$null=$process.Handle
try {
    for($i=0;$i -lt 100 -and !(Test-Path "$stage/ready");$i++) {
        if ($process.HasExited) { throw (Get-Content "$stage/error.log" -Raw) }
        Start-Sleep -Milliseconds 100
    }
    if (!(Test-Path "$stage/ready")) { throw 'C# 二维码服务器启动超时' }
    & "$Jdk/bin/java.exe" '-Dfile.encoding=UTF-8' -cp "$stage;$zxing" CrossPlatformTest $stage
    if ($LASTEXITCODE -ne 0) { throw '跨语言测试失败' }
    if (!$process.WaitForExit(5000)) { throw 'C# 测试结束超时' }
    if ($process.ExitCode -ne 0) { throw (Get-Content "$stage/error.log" -Raw) }
    Get-Content "$stage/server.log"
} finally { if (!$process.HasExited) { Stop-Process -Id $process.Id } }
