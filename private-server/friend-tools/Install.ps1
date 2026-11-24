param([string]$GameDirectory)
$ErrorActionPreference = 'Stop'
if (!$GameDirectory) { $GameDirectory = Read-Host 'Enter the OverField game directory containing launcher.exe' }
$game = (Resolve-Path -LiteralPath $GameDirectory.Trim('"')).Path
if (!(Test-Path -LiteralPath (Join-Path $game 'launcher.exe')) -or !(Test-Path -LiteralPath (Join-Path $game 'MelonLoader'))) { throw 'Select the game directory with MelonLoader already installed.' }
$gameExe = Join-Path $game 'launcher.exe'
if (Get-Process -Name launcher -ErrorAction SilentlyContinue | Where-Object {$_.Path -eq $gameExe}) { throw 'Exit the game before installing.' }
$stamp=Get-Date -Format 'yyyyMMdd-HHmmss-fff'
foreach ($pair in @(@{Source='OverField.PrivateServer.dll';Target='Mods/OverField.PrivateServer.dll'},@{Source='private-server.json';Target='UserData/private-server.json'})) {
    $target=Join-Path $game $pair.Target
    New-Item -ItemType Directory -Path (Split-Path $target) -Force | Out-Null
    if (Test-Path -LiteralPath $target) { Copy-Item -LiteralPath $target -Destination "$target.$stamp.bak" }
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $pair.Source) -Destination $target -Force
}
Write-Host 'Installed. Launch the game normally; the plugin starts its built-in tunnel automatically.'
