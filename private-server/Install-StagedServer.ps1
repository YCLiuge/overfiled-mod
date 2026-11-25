# Called only after Start-Server has verified that all configured ports are free.
$ErrorActionPreference = 'Stop'
$serverRoot = Join-Path $PSScriptRoot 'Lolo'
$candidate = Join-Path $serverRoot 'bin/Lolo_windows_amd64.next.exe'
if (!(Test-Path -LiteralPath $candidate)) { return }
$live = Join-Path $serverRoot 'bin/Lolo_windows_amd64.exe'
$running = @(Get-Process -Name Lolo_windows_amd64,Lolo_windows_amd64.next -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $live -or $_.Path -eq $candidate })
if ($running.Count -gt 0) { throw 'Stop the server normally before installing the staged build.' }
$backup = Join-Path $PSScriptRoot ('backups/staged-install-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
New-Item -ItemType Directory -Path $backup | Out-Null
if (Test-Path -LiteralPath $live) { Copy-Item -LiteralPath $live -Destination $backup }
Get-ChildItem -LiteralPath (Join-Path $serverRoot 'db') -Filter 'lolo.db*' -File | Copy-Item -Destination $backup
$expected = (Get-FileHash -LiteralPath $candidate -Algorithm SHA256).Hash
Copy-Item -LiteralPath $candidate -Destination $live -Force
if ((Get-FileHash -LiteralPath $live -Algorithm SHA256).Hash -ne $expected) { throw "Staged build copy failed. Backup: $backup" }
# Keep the candidate for recovery; avoid repeated backups on subsequent starts.
Move-Item -LiteralPath $candidate -Destination (Join-Path $backup 'installed-candidate.exe')
Write-Host "Installed staged server. Program and save backup: $backup"
