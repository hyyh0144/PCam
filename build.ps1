param([switch]$Test)
. "$PSScriptRoot/build-env.ps1"
$ErrorActionPreference='Stop'
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
$toolRoot=Join-Path (Split-Path $PSScriptRoot) 'tools'
$dotnet=Join-Path $toolRoot 'dotnet/dotnet.exe'
$jdk=(Get-ChildItem (Join-Path $toolRoot 'jdk') -Directory | Select-Object -First 1).FullName
$sdk=Join-Path $toolRoot 'android-sdk'
$env:JAVA_HOME=$jdk
$env:ANDROID_HOME=$sdk
$env:DOTNET_ROOT=Join-Path $toolRoot 'dotnet'
$env:PATH="$env:DOTNET_ROOT;$env:PATH"
function CheckExit {if($LASTEXITCODE -ne 0){throw "步骤失败，退出码 $LASTEXITCODE"}}
& "$PSScriptRoot/recorder/build-native.ps1"
& "$PSScriptRoot/virtual-camera/build-native.ps1";CheckExit
if($Test){
    & $dotnet run --project "$PSScriptRoot/tests/ProtocolTests.csproj" -c Release;CheckExit
    & $dotnet run --project "$PSScriptRoot/tests/CompositionTests/CompositionTests.csproj" -c Release -- "$PSScriptRoot/tests/build/visual";CheckExit
    & $dotnet run --project "$PSScriptRoot/tests/RecordingTests/RecordingTests.csproj" -c Release;CheckExit
    $recordingFile=Get-Content -LiteralPath "$PSScriptRoot/tests/build/recording/latest.txt"
    & $dotnet run --project "$PSScriptRoot/tests/ReviewTests/ReviewTests.csproj" -c Release -- $recordingFile;CheckExit
    & "$PSScriptRoot/recorder/build/RecordingProbe.exe" $recordingFile;CheckExit
    & "$PSScriptRoot/recorder/build/RecordingContracts.exe" "$PSScriptRoot/recorder/build/PCamRecorder.dll" "$PSScriptRoot/recorder/build/contracts-$([guid]::NewGuid().ToString('N')).mp4";CheckExit
    & $dotnet run --project "$PSScriptRoot/tests/RecordingUi/RecordingUi.csproj" -c Release;CheckExit
    & $dotnet run --project "$PSScriptRoot/tests/DurableRecordingTests/DurableRecordingTests.csproj" -c Release;CheckExit
    $recoveredFile=Get-Content -LiteralPath "$PSScriptRoot/tests/build/durable/latest.txt"
    & "$PSScriptRoot/recorder/build/RecordingProbe.exe" $recoveredFile;CheckExit
    $classes=Join-Path $PSScriptRoot 'tests/build/direction'
    New-Item -ItemType Directory -Force -Path $classes | Out-Null
    $src=Join-Path $PSScriptRoot 'android/app/src/main/java/cn/phonecam'
    & "$jdk/bin/javac.exe" -encoding UTF-8 --release 8 -d $classes "$src/CameraRotation.java" "$src/Nv21.java" "$src/RateMeter.java" "$src/FocusPoint.java" "$src/ZoomSteps.java" "$PSScriptRoot/tests/CameraRotationTest.java" "$PSScriptRoot/tests/StreamingTest.java" "$PSScriptRoot/tests/FocusPointTest.java" "$PSScriptRoot/tests/ZoomStepsTest.java";CheckExit
    foreach($name in @('CameraRotationTest','StreamingTest','FocusPointTest','ZoomStepsTest')){& "$jdk/bin/java.exe" -cp $classes $name;CheckExit}
    & "$PSScriptRoot/tests/run-cross-platform.ps1" -Dotnet $dotnet -Jdk $jdk
    & "$PSScriptRoot/tests/run-audio-integration.ps1" -Dotnet $dotnet -Jdk $jdk
    & "$PSScriptRoot/tests/run-recovery-integration.ps1" -Dotnet $dotnet -Jdk $jdk
    & "$PSScriptRoot/tests/run-disconnect-test.ps1" -Jdk $jdk
    & "$PSScriptRoot/tests/run-audio-lifecycle-test.ps1" -Jdk $jdk
}
& "$PSScriptRoot/android/build-direct.ps1" -Sdk $sdk -Jdk $jdk
& $dotnet publish "$PSScriptRoot/windows/PhoneCam.csproj" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o "$PSScriptRoot/releases/windows";CheckExit
Copy-Item -LiteralPath "$PSScriptRoot/releases/windows/PCam.exe" -Destination "$PSScriptRoot/releases/PCam-0.15.2.exe" -Force
Write-Host "编译工程：$PSScriptRoot"
Write-Host "成品目录：$PSScriptRoot/releases"














