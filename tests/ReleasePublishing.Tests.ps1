$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$publish = Join-Path $root 'ci/publish-release.ps1'
$keys = @('GITHUB_REPOSITORY', 'GITHUB_SHA', 'GITHUB_REF', 'GITHUB_RUN_ID', 'RUNNER_TEMP', 'GITHUB_STEP_SUMMARY')
$saved = @{}
foreach ($key in $keys) { $saved[$key] = [Environment]::GetEnvironmentVariable($key) }
$temp = Join-Path ([IO.Path]::GetTempPath()) ('pcam-release-tests-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temp | Out-Null
$env:GITHUB_REPOSITORY = 'hyyh0144/PCam'
$env:GITHUB_SHA = 'a' * 40
$env:GITHUB_REF = 'refs/heads/main'
$env:GITHUB_RUN_ID = '1'
$env:RUNNER_TEMP = $temp
$env:GITHUB_STEP_SUMMARY = $null
[xml]$project = Get-Content -LiteralPath "$root/windows/PhoneCam.csproj" -Raw
$win = @($project.Project.PropertyGroup.Version | Where-Object { $_ })[0]
$android = [regex]::Match((Get-Content -LiteralPath "$root/android/app/build.gradle" -Raw), "versionName\s+'([^']+)'").Groups[1].Value
$expected = @("PCam-$win.exe", "PCam-$android.apk", 'SHA256SUMS-windows.txt', 'SHA256SUMS-android.txt')
$global:PCamReleaseTestState = $null

# This command shadows gh for the test scope; no GitHub requests or real uploads occur.
function gh {
    $a = @($args)
    $global:LASTEXITCODE = 0
    $command = "$($a[0]) $($a[1])"
    switch ($command) {
        'api repos/hyyh0144/PCam/releases/tags/' { throw 'Missing tag argument.' }
        'run download' {
            $destination = $a[[array]::IndexOf($a, '--dir') + 1]
            $platform = $a[[array]::IndexOf($a, '--name') + 1]
            New-Item -ItemType Directory -Path $destination -Force | Out-Null
            $index = if ($platform -eq 'PCam-Windows') { 0 } else { 1 }
            $file = Join-Path $destination $expected[$index]
            [IO.File]::WriteAllText($file, 'test installer')
            $hash = (Get-FileHash -LiteralPath $file).Hash
            if ($global:PCamReleaseTestState.Corrupt -and $index -eq 1) { $hash = '0' * 64 }
            "$hash  $($expected[$index])" | Set-Content -LiteralPath (Join-Path $destination $expected[$index + 2]) -Encoding utf8
        }
        'release create' { $global:PCamReleaseTestState.Created++; $global:PCamReleaseTestState.Release = @{draft=$true;assets=@();target_commitish=$env:GITHUB_SHA;html_url='https://example.invalid/release'} }
        'release upload' {
            $global:PCamReleaseTestState.Uploaded++
            $global:PCamReleaseTestState.Release.assets = @($a | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | ForEach-Object {
                $file = Get-Item -LiteralPath $_
                @{name=$file.Name;size=$file.Length}
            })
            if ($global:PCamReleaseTestState.Incomplete) { $global:PCamReleaseTestState.Release.assets = @($global:PCamReleaseTestState.Release.assets | Select-Object -First 3) }
        }
        'release edit' { $global:PCamReleaseTestState.Published++; $global:PCamReleaseTestState.Release.draft=$false }
        default {
            if ($a[0] -eq 'api' -and $a[1] -like '*/commits/*') { return $global:PCamReleaseTestState.Commit }
            if ($a[0] -eq 'api' -and $a[1] -like '*/releases?per_page=*') {
                if ($global:PCamReleaseTestState.Release) { return ($global:PCamReleaseTestState.Release | ConvertTo-Json -Depth 5) }
                return
            }
            throw "Unexpected mocked command: $command"
        }
    }
}
function ResetFake {
    $global:PCamReleaseTestState = @{Created=0;Uploaded=0;Published=0;Release=$null;Commit=$env:GITHUB_SHA;Corrupt=$false;Incomplete=$false}
}
function ExpectFailure([string]$Message) {
    $failed = $false
    try { & $publish | Out-Null } catch { $failed = $true }
    if (!$failed -or $global:PCamReleaseTestState.Published -ne 0) { throw "Unsafe publication: $Message" }
}
try {
    ResetFake
    & $publish | Out-Null
    if ($global:PCamReleaseTestState.Published -ne 1 -or $global:PCamReleaseTestState.Release.assets.Count -ne 4) { throw 'Fresh release failed.' }
    $global:PCamReleaseTestState.Published = 0
    $global:PCamReleaseTestState.Uploaded = 0
    & $publish | Out-Null
    if ($global:PCamReleaseTestState.Published -or $global:PCamReleaseTestState.Uploaded) { throw 'Published files were overwritten on rerun.' }
    $global:PCamReleaseTestState.Commit = 'b' * 40
    ExpectFailure 'same version, different commit'
    ResetFake
    $global:PCamReleaseTestState.Corrupt = $true
    ExpectFailure 'bad checksum'
    if ($global:PCamReleaseTestState.Created) { throw 'Bad artifacts created a release.' }
    ResetFake
    $global:PCamReleaseTestState.Incomplete = $true
    ExpectFailure 'missing uploaded asset'
    $global:PCamReleaseTestState.Incomplete = $false
    & $publish | Out-Null
    if ($global:PCamReleaseTestState.Published -ne 1) { throw 'Draft retry failed.' }
    ResetFake
    $env:GITHUB_REF = 'refs/heads/other'
    ExpectFailure 'non-main branch'
    $global:LASTEXITCODE = 0
    Write-Output 'Release publication tests passed: complete upload, idempotence, version conflict, checksum, incomplete upload and branch guard.'
} finally {
    foreach ($key in $keys) { [Environment]::SetEnvironmentVariable($key, $saved[$key]) }
}
