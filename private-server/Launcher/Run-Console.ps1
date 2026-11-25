param([Parameter(Mandatory=$true)][ValidateSet('Start-Server.ps1','Start-PublicBridge.ps1')][string]$ScriptName)
$ErrorActionPreference = 'Stop'
try { & (Join-Path (Split-Path $PSScriptRoot -Parent) $ScriptName) }
catch {
    Write-Host $_ -ForegroundColor Red
    Read-Host 'Startup failed. Press Enter to close this window' | Out-Null
    exit 1
}
