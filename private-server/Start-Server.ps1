$ErrorActionPreference = 'Stop'
$serverDirectory = Join-Path $PSScriptRoot 'Lolo'
$exe = Join-Path $serverDirectory 'bin/Lolo_windows_amd64.exe'
$configPath = Join-Path $serverDirectory 'config.json'
if (!(Test-Path -LiteralPath $exe)) { throw 'Lolo executable missing. See README.md for setup/build instructions.' }
if (!(Test-Path -LiteralPath $configPath)) { throw 'Lolo/config.json missing. Generate and configure it first.' }
$config = Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json
$ports = @([int]$config.HttpNet.InnerPort, [int]$config.GateWay.OuterPort, [int]$config.LogServer.OuterPort)
foreach ($port in $ports) {
    $listener = Get-NetTCPConnection -State Listen -LocalPort $port -ErrorAction SilentlyContinue
    if ($listener) { throw "Port $port is in use. Stop the existing server or change config before starting." }
}
Push-Location $serverDirectory
try {
    & $exe -c './config.json'
    if ($LASTEXITCODE -ne 0) { throw "Lolo exited with code $LASTEXITCODE" }
} finally { Pop-Location }
