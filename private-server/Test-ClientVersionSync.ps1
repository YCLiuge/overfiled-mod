$ErrorActionPreference = 'Stop'
$root = Join-Path $PSScriptRoot ('runtime-tools/version-sync-test-' + [guid]::NewGuid().ToString('N'))
$game = Join-Path $root 'game'
$server = Join-Path $root 'server'
$manifest = Join-Path $game 'launcher_Data/yoo/DefaultPackage/ManifestFiles'
New-Item -ItemType Directory -Force $manifest,(Join-Path $server 'data') | Out-Null
$mapPath = Join-Path $server 'data/ClientVersion.json'
$versionPath = Join-Path $manifest 'PackageManifest_DefaultPackage.version'
$sync = Join-Path $PSScriptRoot 'Sync-ClientVersion.ps1'
[IO.File]::WriteAllText((Join-Path $game 'version'), '2026-10-21-14-05-50')
[IO.File]::WriteAllText($versionPath, '2026-11-25-10-44-03')
[IO.File]::WriteAllText((Join-Path $manifest 'PackageManifest_DefaultPackage_2026-11-25-10-44-03.bytes'), 'fixture')
[IO.File]::WriteAllText($mapPath, '{"VersionMap":{"2026-10-21-14-05-50":"2026-10-21-14-05-50_2026-11-07-22-39-35","other":"keep"}}')
& $sync -GameRoot $game -ServerRoot $server
$map = Get-Content $mapPath -Raw | ConvertFrom-Json
if ($map.VersionMap.'2026-10-21-14-05-50' -ne '2026-10-21-14-05-50_2026-11-25-10-44-03' -or $map.VersionMap.other -ne 'keep') { throw 'Update/preservation failed' }
$before = [IO.File]::ReadAllText($mapPath)
& $sync -GameRoot $game -ServerRoot $server
if ([IO.File]::ReadAllText($mapPath) -ne $before) { throw 'Idempotence failed' }
[IO.File]::WriteAllText($versionPath, '2026-11-07-22-39-35')
[IO.File]::WriteAllText((Join-Path $manifest 'PackageManifest_DefaultPackage_2026-11-07-22-39-35.bytes'), 'fixture')
$rejected = $false
try { & $sync -GameRoot $game -ServerRoot $server } catch { $rejected = $true }
if (!$rejected -or [IO.File]::ReadAllText($mapPath) -ne $before) { throw 'Downgrade was not rejected safely' }
[IO.File]::WriteAllText($versionPath, '2026-11-26-00-00-00')
$rejected = $false
try { & $sync -GameRoot $game -ServerRoot $server } catch { $rejected = $true }
if (!$rejected -or [IO.File]::ReadAllText($mapPath) -ne $before) { throw 'Missing manifest was not rejected safely' }
Write-Output 'PASS: newer cached version, other mappings preserved, repeat sync, downgrade rejection and missing manifest rejection.'
