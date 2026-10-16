param([string]$Jdk=$env:JAVA_HOME)
$ErrorActionPreference='Stop'
$stage=Join-Path $PSScriptRoot 'build/disconnect'
$src=Join-Path $PSScriptRoot '../android/app/src/main/java/cn/phonecam'
New-Item -ItemType Directory -Force -Path $stage | Out-Null
# Desktop JVM tests exercise transport only; check-android-api.ps1 separately checks the shipped DEX.
& "$Jdk/bin/javac.exe" -encoding UTF-8 --release 8 -d $stage "$src/ConnectionHub.java" "$src/AudioSender.java" "$src/PairingInfo.java" "$src/RateMeter.java" "$PSScriptRoot/DisconnectStressTest.java" "$PSScriptRoot/ConnectionBoundaryTest.java"
if($LASTEXITCODE -ne 0){throw 'Disconnect test compilation failed'}
& "$Jdk/bin/java.exe" -cp $stage DisconnectStressTest
if($LASTEXITCODE -ne 0){throw 'Disconnect test failed'}
& "$Jdk/bin/java.exe" -cp $stage cn.phonecam.ConnectionBoundaryTest
if($LASTEXITCODE -ne 0){throw 'Connection boundary test failed'}
