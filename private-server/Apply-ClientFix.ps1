param([string]$GameRoot='D:\TapTap\PC Games\176228')
$ErrorActionPreference='Stop'
$source=Join-Path $PSScriptRoot 'client/OverField.PrivateServer.dll'
$expected=(Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash
$destination=Join-Path $GameRoot 'Mods/OverField.PrivateServer.dll'
$config=Join-Path $GameRoot 'UserData/private-server.json'
$processes=@(Get-Process -Name launcher -ErrorAction SilentlyContinue | Where-Object {$_.Path -eq (Join-Path $GameRoot 'launcher.exe')})
foreach($gameProcess in $processes) {
    Write-Host "Waiting for game $($gameProcess.Id) to exit normally."
    Wait-Process -Id $gameProcess.Id -ErrorAction SilentlyContinue
}
if((Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash -ne $expected) { throw 'Plugin build changed; rerun updater.' }
$stamp=Get-Date -Format 'yyyyMMdd-HHmmss-fff'
Copy-Item -LiteralPath $destination -Destination "$destination.$stamp.bak"
Copy-Item -LiteralPath $config -Destination "$config.$stamp.bak"
Copy-Item -LiteralPath $source -Destination $destination -Force
$settings=Get-Content -LiteralPath $config -Raw | ConvertFrom-Json
$settings.SkipHotUpdate=$true
$settings | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $config -Encoding UTF8
if((Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash -ne $expected) {throw 'Installed DLL verification failed'}
Write-Host 'Installed plugin 0.3.1 and enabled cached-resource update skip. Start game normally.'
