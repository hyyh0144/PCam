param([string]$Sdk = $env:ANDROID_HOME, [string]$Jdk = $env:JAVA_HOME,
      [string]$KeyStore = $env:PCAM_KEYSTORE)
$ErrorActionPreference = 'Stop'
if (!$Sdk -or !$Jdk) { throw '请设置 ANDROID_HOME 和 JAVA_HOME（JDK 17）' }
if (!$KeyStore -or !(Test-Path -LiteralPath $KeyStore -PathType Leaf)) {
    throw '请通过 PCAM_KEYSTORE 或 -KeyStore 指定仓库外的签名文件'
}
if (!$env:PCAM_STORE_PASSWORD) { throw '请设置 PCAM_STORE_PASSWORD 环境变量' }
# apksigner reads passwords from the environment; process arguments contain only variable names.
$keyPasswordVariable = if ($env:PCAM_KEY_PASSWORD) { 'PCAM_KEY_PASSWORD' } else { 'PCAM_STORE_PASSWORD' }
$tools = Join-Path $Sdk 'build-tools/35.0.0'
$platform = Join-Path $Sdk 'platforms/android-35/android.jar'
$stage = Join-Path $PSScriptRoot 'build/direct'
New-Item -ItemType Directory -Force -Path "$stage/classes", "$stage/dex" | Out-Null
function Run([string]$Exe, [string[]]$Arguments) {
    & $Exe @Arguments
    if ($LASTEXITCODE -ne 0) { throw "构建工具失败：$Exe ($LASTEXITCODE)" }
}
$sources = @(Get-ChildItem "$PSScriptRoot/app/src/main/java" -Recurse -Filter '*.java' | ForEach-Object FullName)
$zxing = Join-Path $PSScriptRoot 'app/libs/core-3.5.3.jar'
# Android's Java library signatures differ from desktop JDK 8. --release 8 selects
# desktop boot APIs even when android.jar is on -classpath; use the Android boot API instead.
# SDK lambda stubs are compile-only; D8 desugars lambdas and the stubs are never packaged.
$bootApi="$platform;$tools/core-lambda-stubs.jar"
Run "$Jdk/bin/javac.exe" (@('-encoding','UTF-8','-source','8','-target','8','-bootclasspath',$bootApi,'-classpath',$zxing,'-d',"$stage/classes") + $sources)
Run "$Jdk/bin/jar.exe" @('cf',"$stage/classes.jar",'-C',"$stage/classes",'.')
Run "$Jdk/bin/java.exe" @('-cp',"$tools/lib/d8.jar",'com.android.tools.r8.D8','--min-api','26','--lib',$platform,'--output',"$stage/dex","$stage/classes.jar",$zxing)
& "$PSScriptRoot/../tests/check-android-api.ps1" -Sdk $Sdk -Dex "$stage/dex/classes.dex"
# Gradle 自动填充 namespace；直接构建时生成等价的临时清单。
$manifest = (Get-Content "$PSScriptRoot/app/src/main/AndroidManifest.xml" -Raw).Replace('<manifest ', '<manifest package="cn.phonecam" ')
Set-Content -LiteralPath "$stage/AndroidManifest.xml" -Value $manifest -Encoding utf8
Run "$tools/aapt2.exe" @('compile','--dir',"$PSScriptRoot/app/src/main/res",'-o',"$stage/resources.zip")
Run "$tools/aapt2.exe" @('link','-I',$platform,'--manifest',"$stage/AndroidManifest.xml",'--min-sdk-version','26','--target-sdk-version','35','--version-code','26','--version-name','0.12.6','-o',"$stage/unsigned.apk","$stage/resources.zip")
Run "$Jdk/bin/jar.exe" @('uf',"$stage/unsigned.apk",'-C',"$stage/dex",'classes.dex')
Run "$tools/zipalign.exe" @('-f','4',"$stage/unsigned.apk","$stage/aligned.apk")
$release = Join-Path $PSScriptRoot '../releases'
New-Item -ItemType Directory -Force -Path $release | Out-Null
$apk = "$release/PCam-0.12.6.apk"
Run "$Jdk/bin/java.exe" @('-jar',"$tools/lib/apksigner.jar",'sign','--ks',$KeyStore,'--ks-pass','env:PCAM_STORE_PASSWORD','--key-pass',"env:$keyPasswordVariable",'--out',$apk,"$stage/aligned.apk")
Run "$Jdk/bin/java.exe" @('-jar',"$tools/lib/apksigner.jar",'verify','--verbose',$apk)
Write-Host "APK 已生成：$apk"



