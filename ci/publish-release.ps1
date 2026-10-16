$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$repo = $env:GITHUB_REPOSITORY
$sha = $env:GITHUB_SHA
if ($repo -ne 'hyyh0144/PCam' -or $env:GITHUB_REF -ne 'refs/heads/main' -or
    $sha -notmatch '^[0-9a-fA-F]{40}$' -or $env:GITHUB_RUN_ID -notmatch '^\d+$') {
    throw 'Only the official main branch can publish releases.'
}
function InvokeGh([string[]]$Arguments) {
    $result = @(& gh @Arguments)
    if ($LASTEXITCODE -ne 0) { throw "GitHub CLI failed: $($Arguments[0])" }
    return $result
}
[xml]$project = Get-Content -LiteralPath "$root/windows/PhoneCam.csproj" -Raw
$windowsVersion = @($project.Project.PropertyGroup.Version | Where-Object { $_ })[0]
$gradle = Get-Content -LiteralPath "$root/android/app/build.gradle" -Raw
$androidVersion = [regex]::Match($gradle, "versionName\s+'([^']+)'").Groups[1].Value
foreach ($version in @($windowsVersion, $androidVersion)) {
    if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'Invalid release version.' }
}
$tag = "v$windowsVersion-android-v$androidVersion"
$names = @("PCam-$windowsVersion.exe", "PCam-$androidVersion.apk", 'SHA256SUMS-windows.txt', 'SHA256SUMS-android.txt')
function GetRelease {
    # The authenticated releases list includes drafts, whose tags may not exist yet.
    $query = '.[] | select(.tag_name == "' + $tag + '")'
    $json = (InvokeGh -Arguments @('api', "repos/$repo/releases?per_page=100", '--paginate', '--jq', $query)) -join "`n"
    if ($json) { return ($json | ConvertFrom-Json) }
    return $null
}
$existing = GetRelease
if ($existing) {
    $tagCommit = if ($existing.draft) { $existing.target_commitish } else {
        (InvokeGh -Arguments @('api', "repos/$repo/commits/$tag", '--jq', '.sha')) -join ''
    }
    # A version identifies one source commit. A rerun must never replace a published installer.
    if ($tagCommit -ne $sha) { throw 'This version already belongs to another commit. Increase the version first.' }
    if (!$existing.draft) {
        foreach ($name in $names) {
            if ($name -notin @($existing.assets.name)) { throw "Published release is missing $name; it will not be overwritten." }
        }
        Write-Output "Already published: $($existing.html_url)"
        return
    }
}

$work = Join-Path $env:RUNNER_TEMP ('pcam-publish-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $work | Out-Null
InvokeGh -Arguments @('run', 'download', $env:GITHUB_RUN_ID, '--repo', $repo, '--name', 'PCam-Windows', '--dir', "$work/windows") | Out-Null
InvokeGh -Arguments @('run', 'download', $env:GITHUB_RUN_ID, '--repo', $repo, '--name', 'PCam-Android', '--dir', "$work/android") | Out-Null
$files = @("$work/windows/$($names[0])", "$work/android/$($names[1])", "$work/windows/$($names[2])", "$work/android/$($names[3])")
for ($i = 0; $i -lt 2; $i++) {
    $line = (Get-Content -LiteralPath $files[$i + 2] -Raw).Trim()
    $match = [regex]::Match($line, '^([0-9a-fA-F]{64})\s{2}([^\r\n]+)$')
    if (!$match.Success -or $match.Groups[2].Value -ne $names[$i] -or
        (Get-FileHash -LiteralPath $files[$i] -Algorithm SHA256).Hash -ne $match.Groups[1].Value) {
        throw "Artifact checksum mismatch: $($names[$i])"
    }
}
$notes = @"
让手机成为电脑的眼睛。

- Windows 电脑端：$($names[0])
- Android 手机端：$($names[1])
- 自动构建记录：https://github.com/$repo/actions/runs/$env:GITHUB_RUN_ID
- SHA256SUMS 文件可用于核对下载完整性。

Windows 虚拟摄像头需要 Windows 11，首次安装需管理员权限。Android 需要 8.0 或更高版本；发布签名沿用既有密钥，旧调试签名版本需先卸载再安装。
"@
$notesFile = Join-Path $work 'release-notes.md'
[IO.File]::WriteAllText($notesFile, $notes, [Text.UTF8Encoding]::new($false))
if (!$existing) {
    InvokeGh -Arguments @('release', 'create', $tag, '--repo', $repo, '--target', $sha,
        '--title', "PCam — Windows $windowsVersion / Android $androidVersion", '--notes-file', $notesFile, '--draft') | Out-Null
}
# Upload into a draft first: failed uploads never expose a half-complete public release.
InvokeGh -Arguments (@('release', 'upload', $tag, '--repo', $repo, '--clobber') + $files) | Out-Null
$uploaded = GetRelease
foreach ($file in $files) {
    $local = Get-Item -LiteralPath $file
    $asset = @($uploaded.assets | Where-Object { $_.name -eq $local.Name })
    if ($asset.Count -ne 1 -or $asset[0].size -ne $local.Length) { throw "Incomplete upload: $($local.Name)" }
}
InvokeGh -Arguments @('release', 'edit', $tag, '--repo', $repo, '--draft=false', '--latest') | Out-Null
$url = "https://github.com/$repo/releases/tag/$tag"
if ($env:GITHUB_STEP_SUMMARY) { "Release: $url" | Add-Content -LiteralPath $env:GITHUB_STEP_SUMMARY -Encoding utf8 }
Write-Output $url
