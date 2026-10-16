$ErrorActionPreference = 'Stop'
[xml]$project = Get-Content -LiteralPath "$PSScriptRoot/../windows/PhoneCam.csproj" -Raw
$version = @($project.Project.PropertyGroup.Version | Where-Object { $_ })[0]
if (!$version) { throw 'Windows 项目缺少版本号' }
$exe = Join-Path $PSScriptRoot "../releases/PCam-$version.exe"
if (!(Test-Path -LiteralPath $exe -PathType Leaf)) { throw "请先构建当前版本：PCam-$version.exe" }
$instances = @()
$ports = @()
try {
    for ($n=0; $n -lt 2; $n++) {
        $process = Start-Process -FilePath $exe -WindowStyle Hidden -PassThru
        $instances += $process
        $httpPort = 0; $tcpPort = 0
        for ($i=0; $i -lt 30; $i++) {
            Start-Sleep -Milliseconds 300
            if ($process.HasExited) { throw 'WPF 启动后退出' }
            $listeners = @(Get-NetTCPConnection -State Listen -OwningProcess $process.Id -ErrorAction SilentlyContinue)
            $httpPort = ($listeners | Where-Object LocalAddress -eq '127.0.0.1' | Select-Object -First 1).LocalPort
            $tcpPort = ($listeners | Where-Object LocalAddress -eq '0.0.0.0' | Select-Object -First 1).LocalPort
            if ($httpPort -and $tcpPort) { break }
        }
        if (!$httpPort -or !$tcpPort) { throw '随机端口没有启动' }
        $ports += $httpPort; $ports += $tcpPort
        $page = Invoke-WebRequest "http://127.0.0.1:$httpPort/" -TimeoutSec 3
        if ($page.StatusCode -ne 200 -or $page.Content -notmatch '/stream') { throw '视频页面失败' }
        $empty = Invoke-WebRequest "http://127.0.0.1:$httpPort/frame" -SkipHttpErrorCheck
        if ($empty.StatusCode -ne 503) { throw '空帧状态错误' }
        $client = [System.Net.Sockets.TcpClient]::new()
        try {
            $client.Connect('127.0.0.1',$tcpPort)
            $stream = $client.GetStream(); $stream.ReadTimeout = 3000
            $token = [System.Text.Encoding]::ASCII.GetBytes('ZZZZZZZZZZZZZZZZ')
            $stream.Write($token,0,$token.Length)
            if ($stream.ReadByte() -ne 0) { throw '未拒绝错误配对码' }
        } finally { $client.Dispose() }
    }
    if (($ports | Select-Object -Unique).Count -ne 4) { throw '多实例端口冲突' }
    Write-Host 'PASS：两个 WPF 实例、四个不同随机端口、HTTP 200/503、真实 TCP 错码拒绝'
} finally {
    foreach ($process in $instances) { if (!$process.HasExited) { Stop-Process -Id $process.Id } }
}





