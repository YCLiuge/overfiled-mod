param(
    [Parameter(Mandatory = $true)][string]$GameDirectory,
    [string]$BaseUrl = 'http://127.0.0.1:18080',
    [switch]$Disable,
    [switch]$StageWhileRunning
)
$ErrorActionPreference = 'Stop'
$game = (Resolve-Path -LiteralPath $GameDirectory).Path
if (!(Test-Path -LiteralPath (Join-Path $game 'MelonLoader'))) { throw 'MelonLoader directory not found. Select the actual game directory.' }
if (!(Test-Path -LiteralPath (Join-Path $game 'launcher.exe')) -or !(Test-Path -LiteralPath (Join-Path $game 'launcher_Data'))) { throw 'OverField launcher.exe and launcher_Data not found in the selected directory.' }
$uri = $null
if (![Uri]::TryCreate($BaseUrl, [UriKind]::Absolute, [ref]$uri) -or $uri.Scheme -notin @('http', 'https') -or $uri.UserInfo -or $uri.AbsolutePath -ne '/' -or $uri.Query -or $uri.Fragment) { throw 'BaseUrl must be an HTTP(S) origin, e.g. http://127.0.0.1:18080' }
$gameExe = Join-Path $game 'launcher.exe'
if (Get-Process -Name 'launcher' -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $gameExe }) {
    if (!$StageWhileRunning) { throw 'Exit OverField first, or use -StageWhileRunning to stage for the next launch.' }
    Write-Warning 'Staging for next launch only. Exit and relaunch OverField after the local server starts.'
}
$configDirectory = Join-Path $game 'UserData'
$configPath = Join-Path $configDirectory 'private-server.json'
New-Item -ItemType Directory -Path $configDirectory -Force | Out-Null
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss-fff'
if (Test-Path -LiteralPath $configPath) { Copy-Item -LiteralPath $configPath -Destination "$configPath.$stamp.bak" }
if (!$Disable) {
    $dll = Join-Path $PSScriptRoot 'client/OverField.PrivateServer.dll'
    if (!(Test-Path -LiteralPath $dll)) { throw 'Build ClientRedirect/ClientRedirect.csproj first.' }
    $mods = Join-Path $game 'Mods'
    New-Item -ItemType Directory -Path $mods -Force | Out-Null
    $destination = Join-Path $mods 'OverField.PrivateServer.dll'
    if (Test-Path -LiteralPath $destination) { Copy-Item -LiteralPath $destination -Destination "$destination.$stamp.bak" }
    Copy-Item -LiteralPath $dll -Destination $destination
}
@{ Enabled = !$Disable; BaseUrl = $uri.GetLeftPart([UriPartial]::Authority) } | ConvertTo-Json | Set-Content -LiteralPath $configPath -Encoding utf8
Write-Host "Client redirect Enabled=$(!$Disable); config=$configPath"
Write-Host 'Start the server first, then launch the game through its usual MelonLoader entry.'
