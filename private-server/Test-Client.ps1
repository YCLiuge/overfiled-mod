param([string]$GameDirectory = 'D:\TapTap\PC Games\176228')
$ErrorActionPreference = 'Stop'
$game = (Resolve-Path -LiteralPath $GameDirectory).Path
$client = Get-Process -Name launcher -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq (Join-Path $game 'launcher.exe') }
if (!$client) { throw 'Game is not running.' }
$log = Join-Path $game 'MelonLoader/Latest.log'
if (Test-Path -LiteralPath $log) {
    Get-Content -LiteralPath $log | Select-String '\[PrivateServer\]' | ForEach-Object { $_.Line }
}
$connections = @(Get-NetTCPConnection -OwningProcess $client.Id -State Established -ErrorAction SilentlyContinue)
$gate = @($connections | Where-Object { $_.RemoteAddress -in @('127.0.0.1', '::1') -and $_.RemotePort -eq 11000 })
if ($gate.Count -gt 0) {
    Write-Host 'PASS: game process has an established TCP connection to the local Lolo game gateway.'
    $gate | Select-Object OwningProcess,RemoteAddress,RemotePort | Format-Table
} else {
    Write-Warning 'No established local game gateway connection yet. Hook installation alone does not prove private-server login. Try logging in and re-run this check.'
}
