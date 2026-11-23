<#
.SYNOPSIS
    频道聊天服务器的端到端冒烟测试。

.DESCRIPTION
    1. 以低心跳参数启动 Chat.Server（--heartbeat 1 --timeout 3），
       用 net9.0 与 .NET Framework 4.8 各跑一遍 Chat.Demo --smoke（19 项断言）；
    2. 再以很小的单次传输上限启动第二个 Chat.Server（--max-line 256 --max-text 100），
       跑 Chat.Demo --smoke-limits（上限专项断言：超限断开、超限不转发、成员列表裁剪……）；
    3. 关闭服务器并打印服务器日志。

.PARAMETER Port
    主测试端口，默认 9100；上限专项测试用 Port+1。

.PARAMETER Tfms
    要跑的目标框架，默认 net9.0 与 net48；本机缺少对应运行时会自动跳过。

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File tools\smoke-test.ps1
    powershell -NoProfile -ExecutionPolicy Bypass -File tools\smoke-test.ps1 -Tfms net9.0 -Port 9200

.NOTES
    本文件保存为「UTF-8 带 BOM」，否则 Windows PowerShell 5.1 会按 GBK 解析其中的中文而报语法错误。
#>
[CmdletBinding()]
param(
    [int]$Port = 9100,
    [string[]]$Tfms = @('net9.0', 'net48'),
    [string]$Configuration = 'Debug'
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot

function Test-NetFx48 {
    try {
        $release = (Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full' -ErrorAction Stop).Release
        return $release -ge 528040
    }
    catch {
        return $false
    }
}

function Test-PortOpen([int]$p) {
    try {
        $probe = New-Object System.Net.Sockets.TcpClient
        $probe.Connect('127.0.0.1', $p)
        $probe.Close()
        return $true
    }
    catch {
        return $false
    }
}

function Wait-PortOpen([int]$p, [int]$seconds = 10) {
    for ($i = 0; $i -lt ($seconds * 4); $i++) {
        Start-Sleep -Milliseconds 250
        if (Test-PortOpen $p) { return $true }
    }

    return $false
}

# 启动一个服务器，跑完给定的自检模式集合后关掉，返回每个 (框架, 模式) 的退出码
function Invoke-ServerScenario {
    param(
        [int]$ScenarioPort,
        [string]$Title,
        [string[]]$ServerArgs,
        [string[]]$Modes,
        [string[]]$Runners,
        [string]$ServerExe,
        [string]$Configuration
    )

    $outLog = Join-Path $env:TEMP "chat-server-$ScenarioPort.out.log"
    $errLog = Join-Path $env:TEMP "chat-server-$ScenarioPort.err.log"
    Remove-Item $outLog, $errLog -ErrorAction SilentlyContinue

    Write-Host ''
    Write-Host "== $Title ==" -ForegroundColor Cyan
    Write-Host ("   参数: " + ($ServerArgs -join ' ')) -ForegroundColor DarkGray

    $server = Start-Process -FilePath $ServerExe `
        -ArgumentList (@('--host', '127.0.0.1', '--port', $ScenarioPort) + $ServerArgs) `
        -RedirectStandardOutput $outLog -RedirectStandardError $errLog -PassThru -WindowStyle Hidden

    $results = @()
    try {
        if (-not (Wait-PortOpen $ScenarioPort)) {
            throw "服务器未能在 10 秒内监听端口 $ScenarioPort，请查看 $errLog"
        }

        foreach ($mode in $Modes) {
            foreach ($tfm in $Runners) {
                Write-Host ''
                Write-Host "== $mode（$tfm）==" -ForegroundColor Cyan

                $exe = Join-Path $root "Chat.Demo\bin\$Configuration\$tfm\Chat.Demo.exe"
                if (-not (Test-Path $exe)) {
                    Write-Host "跳过 $tfm：找不到 $exe" -ForegroundColor Yellow
                    continue
                }

                # 注意：必须用 Out-Host 把子进程输出直接打到控制台，
                # 否则它会混进本函数的返回值，污染 $results
                & $exe --host 127.0.0.1 --port $ScenarioPort $mode | Out-Host
                $results += [pscustomobject]@{ Name = "$mode/$tfm"; ExitCode = $LASTEXITCODE }
            }
        }
    }
    finally {
        if ($server -and -not $server.HasExited) {
            Stop-Process -Id $server.Id -Force -ErrorAction SilentlyContinue
            $server.WaitForExit(3000) | Out-Null
        }

        Write-Host ''
        Write-Host "== 服务器日志（端口 $ScenarioPort，末尾 25 行）==" -ForegroundColor Cyan
        # 服务器日志是 UTF-8，必须显式指定编码，否则 PowerShell 5.1 会按 ANSI 解码成乱码
        if (Test-Path $outLog) { Get-Content $outLog -Encoding UTF8 | Select-Object -Last 25 | Out-Host }
        if ((Test-Path $errLog) -and (Get-Item $errLog).Length -gt 0) {
            Write-Host '== 服务器错误输出 ==' -ForegroundColor Yellow
            Get-Content $errLog -Encoding UTF8 | Out-Host
        }
    }

    return $results
}

Push-Location $root
try {
    Write-Host '== 构建 ==' -ForegroundColor Cyan
    dotnet build 'Chat.Server\Chat.Server.csproj' -c $Configuration -v m --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Chat.Server 构建失败' }

    $runners = @()
    foreach ($tfm in $Tfms) {
        if ($tfm -eq 'net48' -and -not (Test-NetFx48)) {
            Write-Host "跳过 $tfm：本机未安装 .NET Framework 4.8 运行时" -ForegroundColor Yellow
            continue
        }

        dotnet build 'Chat.Demo\Chat.Demo.csproj' -c $Configuration "-p:ChatTfms=$tfm" -v m --nologo
        if ($LASTEXITCODE -ne 0) {
            Write-Host "跳过 $tfm：Chat.Demo 构建失败" -ForegroundColor Yellow
            continue
        }

        $runners += $tfm
    }

    if ($runners.Count -eq 0) { throw '没有任何可运行的目标框架' }

    $serverExe = Join-Path $root "Chat.Server\bin\$Configuration\net9.0\Chat.Server.exe"

    $results = @()
    $results += Invoke-ServerScenario `
        -ScenarioPort $Port `
        -Title '场景 1：默认配置（心跳 1s / 死连接 3s，其余全默认）' `
        -ServerArgs @('--heartbeat', '1', '--timeout', '3', '--verbose') `
        -Modes @('--smoke', '--smoke-large') `
        -Runners $runners `
        -ServerExe $serverExe `
        -Configuration $Configuration

    $results += Invoke-ServerScenario `
        -ScenarioPort ($Port + 1) `
        -Title '场景 2：单次传输上限专项（--max-line 512 --max-text 600）' `
        -ServerArgs @('--max-line', '512', '--max-text', '600', '--heartbeat', '30', '--timeout', '60', '--verbose') `
        -Modes @('--smoke-limits') `
        -Runners $runners `
        -ServerExe $serverExe `
        -Configuration $Configuration

    Write-Host ''
    Write-Host '== 结果 ==' -ForegroundColor Cyan
    $failed = 0
    foreach ($result in $results) {
        if ($result.ExitCode -eq 0) {
            Write-Host ("  {0}：全部通过" -f $result.Name) -ForegroundColor Green
        }
        else {
            Write-Host ("  {0}：存在失败项（退出码 {1}）" -f $result.Name, $result.ExitCode) -ForegroundColor Red
            $failed++
        }
    }

    if ($failed -eq 0) {
        Write-Host '冒烟测试全部通过' -ForegroundColor Green
        exit 0
    }

    exit 1
}
finally {
    Pop-Location
}
