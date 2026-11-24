[CmdletBinding()]
param([Parameter(Mandatory=$true)][ValidateNotNullOrEmpty()][string]$TunnelRemote)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$bridgeDirectory = Join-Path $root 'TcpBridge'
$serverConfig = Join-Path $bridgeDirectory 'server.json'
if (!(Test-Path -LiteralPath $serverConfig)) {
    $bytes = New-Object byte[] 32
    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try { $rng.GetBytes($bytes) } finally { $rng.Dispose() }
    $key = ([BitConverter]::ToString($bytes)).Replace('-','').ToLowerInvariant()
    [IO.File]::WriteAllText($serverConfig, (@{Mode='server';Listen='127.0.0.1:10086';Key=$key} | ConvertTo-Json), (New-Object Text.UTF8Encoding($false)))
}
$settings = Get-Content -LiteralPath $serverConfig -Raw | ConvertFrom-Json
$package = Join-Path $root 'friend-package'
New-Item -ItemType Directory -Path $package -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $root 'client/OverField.PrivateServer.dll') -Destination $package -Force
@{Enabled=$true;BaseUrl='http://127.0.0.1:18081';GatewayHost='127.0.0.1';GatewayPort=11001;LogPort=12001;SkipHotUpdate=$true;TunnelEnabled=$true;TunnelRemote=$TunnelRemote;TunnelKey=$settings.Key} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $package 'private-server.json') -Encoding UTF8
Copy-Item -LiteralPath (Join-Path $root 'friend-tools/Install.ps1') -Destination $package -Force
Copy-Item -LiteralPath (Join-Path $root 'friend-tools/Install.cmd') -Destination $package -Force
Copy-Item -LiteralPath (Join-Path $root 'friend-tools/README.txt') -Destination $package -Force
$zip = Join-Path $root 'OverField-Friend-Client.zip'
$packageFiles = @('OverField.PrivateServer.dll', 'private-server.json', 'Install.ps1', 'Install.cmd', 'README.txt') | ForEach-Object { Join-Path $package $_ }
Compress-Archive -LiteralPath $packageFiles -DestinationPath $zip -Force
Write-Host "Friend package: $zip"
