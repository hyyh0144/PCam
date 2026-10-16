param([string]$Jdk=$env:JAVA_HOME)
$ErrorActionPreference='Stop'
$stage=Join-Path $PSScriptRoot 'build/audio-lifecycle'
New-Item -ItemType Directory -Force -Path $stage | Out-Null
$stubs=@(Get-ChildItem -LiteralPath "$PSScriptRoot/android-stubs" -Recurse -Filter '*.java' | ForEach-Object FullName)
& "$Jdk/bin/javac.exe" -encoding UTF-8 --release 8 -d $stage @stubs "$PSScriptRoot/../android/app/src/main/java/cn/phonecam/AudioCapture.java" "$PSScriptRoot/AudioCaptureLifecycleTest.java"
if($LASTEXITCODE -ne 0){throw 'Audio lifecycle test compilation failed'}
& "$Jdk/bin/java.exe" -cp $stage cn.phonecam.AudioCaptureLifecycleTest
if($LASTEXITCODE -ne 0){throw 'Audio lifecycle test failed'}
