$ErrorActionPreference = 'Stop'
$bridge = Join-Path $PSScriptRoot 'TcpBridge/bin/OverField.TcpBridge.exe'
$config = Join-Path $PSScriptRoot 'TcpBridge/server.json'
if (!(Test-Path -LiteralPath $bridge) -or !(Test-Path -LiteralPath $config)) { throw 'Bridge executable or server.json missing.' }
foreach ($port in @(18080,11000,12000)) {
    if (!(Get-NetTCPConnection -State Listen -LocalPort $port -ErrorAction SilentlyContinue)) { throw "Start the Lolo server first; port $port is not listening." }
}
& $bridge -config $config
if ($LASTEXITCODE -ne 0) { throw "Bridge exited with code $LASTEXITCODE" }
