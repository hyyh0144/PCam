param([string]$Sdk=$env:ANDROID_HOME,[Parameter(Mandatory=$true)][string]$Dex)
$ErrorActionPreference='Stop'
# Inspect the shipped DEX, not just desktop JVM tests: the old source passed those tests.
$dump=& "$Sdk/build-tools/35.0.0/dexdump.exe" -d $Dex
if($LASTEXITCODE -ne 0){throw 'Cannot inspect Android DEX'}
if($dump -match 'ConcurrentHashMap;\.keySet:\(\)Ljava/util/concurrent/ConcurrentHashMap\$KeySetView;'){
    throw 'Android 8/9 incompatible ConcurrentHashMap.keySet():KeySetView invocation in DEX'
}
if($dump -match "Class descriptor.*'Landroid/"){
    throw 'Android SDK/test stub class definitions must not be packaged in the APK'
}
Write-Host 'PASS DEX: no Android 8/9 incompatible ConcurrentHashMap.keySet():KeySetView invocation'
