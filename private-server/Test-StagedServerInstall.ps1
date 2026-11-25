[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$fixture = Join-Path $PSScriptRoot ('runtime-tools/staged-install-test-' + [guid]::NewGuid().ToString('N'))
$bin = Join-Path $fixture 'Lolo/bin'
$db = Join-Path $fixture 'Lolo/db'
New-Item -ItemType Directory -Path $bin,$db | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Install-StagedServer.ps1') -Destination $fixture
$live = Join-Path $bin 'Lolo_windows_amd64.exe'
$candidate = Join-Path $bin 'Lolo_windows_amd64.next.exe'
[IO.File]::WriteAllText($live, 'old-program')
[IO.File]::WriteAllText($candidate, 'new-program')
[IO.File]::WriteAllText((Join-Path $db 'lolo.db'), 'save-fixture')
[IO.File]::WriteAllText((Join-Path $db 'lolo.db-wal'), 'wal-fixture')
$installer = Join-Path $fixture 'Install-StagedServer.ps1'
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $installer
if ($LASTEXITCODE -ne 0) { throw 'Fixture install failed.' }
if ([IO.File]::ReadAllText($live) -ne 'new-program' -or (Test-Path -LiteralPath $candidate)) { throw 'Candidate promotion failed.' }
$backups = @(Get-ChildItem -LiteralPath (Join-Path $fixture 'backups') -Directory)
if ($backups.Count -ne 1) { throw 'Expected one backup.' }
foreach ($entry in @(@('Lolo_windows_amd64.exe','old-program'), @('lolo.db','save-fixture'), @('lolo.db-wal','wal-fixture'), @('installed-candidate.exe','new-program'))) {
    if ([IO.File]::ReadAllText((Join-Path $backups[0].FullName $entry[0])) -ne $entry[1]) { throw "Bad backup: $($entry[0])" }
}
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $installer
if ($LASTEXITCODE -ne 0 -or @(Get-ChildItem -LiteralPath (Join-Path $fixture 'backups') -Directory).Count -ne 1) { throw 'Repeat install changed backups.' }
if ([IO.File]::ReadAllText((Join-Path $db 'lolo.db')) -ne 'save-fixture') { throw 'Installation modified fixture save.' }
Write-Host 'PASS staged install, old program and SQLite/WAL backups, candidate recovery, idempotence, unchanged fixture save.'
Write-Host "Fixture retained for inspection: $fixture"
