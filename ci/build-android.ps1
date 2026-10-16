$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot
if(!$env:ANDROID_HOME){$env:ANDROID_HOME=$env:ANDROID_SDK_ROOT}
if(!$env:ANDROID_HOME -or !$env:JAVA_HOME){throw '缺少Android SDK或JDK'}
$sdkManager=Join-Path $env:ANDROID_HOME 'cmdline-tools/latest/bin/sdkmanager.bat'
if(!(Test-Path -LiteralPath "$env:ANDROID_HOME/platforms/android-35/android.jar") -or
   !(Test-Path -LiteralPath "$env:ANDROID_HOME/build-tools/35.0.0/aapt2.exe")){
    & $sdkManager 'platforms;android-35' 'build-tools;35.0.0'
    if($LASTEXITCODE -ne 0){throw 'Android SDK组件安装失败'}
}
& "$root/tests/run-disconnect-test.ps1" -Jdk $env:JAVA_HOME
& "$root/tests/run-audio-lifecycle-test.ps1" -Jdk $env:JAVA_HOME
if(!$env:PCAM_KEYSTORE_BASE64 -or !$env:PCAM_STORE_PASSWORD){throw '请先配置PCAM签名Secrets，不会生成临时签名'}
$key=Join-Path $env:RUNNER_TEMP 'pcam-release.p12'
try {
    [IO.File]::WriteAllBytes($key,[Convert]::FromBase64String($env:PCAM_KEYSTORE_BASE64))
    $env:PCAM_KEYSTORE_BASE64=$null
    $env:PCAM_KEYSTORE=$key
    & "$root/android/build-direct.ps1" -Sdk $env:ANDROID_HOME -Jdk $env:JAVA_HOME
    $gradle=Get-Content -LiteralPath "$root/android/app/build.gradle" -Raw
    $version=[regex]::Match($gradle,"versionName\s+'([^']+)'").Groups[1].Value
    if(!$version){throw '缺少Android版本号'}
    $apk=Join-Path $root "releases/PCam-$version.apk"
    $cert=@(& "$env:JAVA_HOME/bin/java.exe" -jar "$env:ANDROID_HOME/build-tools/35.0.0/lib/apksigner.jar" verify --print-certs $apk)
    if($LASTEXITCODE -ne 0){throw 'APK签名验证失败'}
    $match=[regex]::Match(($cert -join "`n"),'Signer #1 certificate SHA-256 digest:\s*([0-9a-fA-F]+)')
    # The public fingerprint pins this workflow to the official release key.
    if(!$match.Success -or $match.Groups[1].Value.ToUpperInvariant() -ne '00CE154CECFA7943C25EAE35AE8819B84CEF5DC3630F2041C792F9B02A2C4916'){
        throw 'APK证书不是PCam发布证书'
    }
    "$((Get-FileHash -LiteralPath $apk -Algorithm SHA256).Hash)  $([IO.Path]::GetFileName($apk))" |
        Set-Content -LiteralPath "$root/releases/SHA256SUMS-android.txt" -Encoding utf8
} finally {
    if(Test-Path -LiteralPath $key){Remove-Item -LiteralPath $key -Force}
    $env:PCAM_KEYSTORE=$null;$env:PCAM_KEYSTORE_BASE64=$null
    $env:PCAM_STORE_PASSWORD=$null;$env:PCAM_KEY_PASSWORD=$null
}
