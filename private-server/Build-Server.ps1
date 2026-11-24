[CmdletBinding()]
param([switch]$TestOnly, [switch]$Stage)
$ErrorActionPreference = 'Stop'
$lolo = Join-Path $PSScriptRoot 'Lolo'
$tools = Join-Path $PSScriptRoot 'runtime-tools'
$go = Join-Path $tools 'go/bin/go.exe'
if (!(Test-Path $go)) { throw 'Portable Go missing; follow Lolo/LOCAL-SETUP.md first.' }
$revision = & git -C $lolo rev-parse HEAD
if ($LASTEXITCODE -ne 0 -or $revision -ne '375423e8c0806bfe8804001ac46d8bc371843045') { throw 'Unexpected Lolo base revision; review patches before upgrading.' }
$patch = Join-Path $PSScriptRoot 'server-patches/local-server.patch'
# Windows PowerShell treats native stderr as an error record; this expected
# reverse-check failure means the patch has not yet been installed.
$previousPreference = $ErrorActionPreference
try {
    $ErrorActionPreference = 'Continue'
    & git -C $lolo apply --reverse --check $patch 2>$null
    $alreadyApplied = $LASTEXITCODE -eq 0
} finally { $ErrorActionPreference = $previousPreference }
if (!$alreadyApplied) {
    & git -C $lolo apply --check $patch
    if ($LASTEXITCODE -ne 0) { throw 'Patch conflicts with local changes; no files modified.' }
    & git -C $lolo apply $patch
    if ($LASTEXITCODE -ne 0) { throw 'Patch failed.' }
}
Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'server-tests') -Recurse -Filter '*_test.go' | ForEach-Object {
    $relative = $_.FullName.Substring((Join-Path $PSScriptRoot 'server-tests').Length + 1)
    $dest = Join-Path $lolo $relative
    if ((Test-Path $dest) -and (Get-FileHash $dest).Hash -ne (Get-FileHash $_.FullName).Hash) { throw "Test differs from tracked copy: $relative" }
    Copy-Item -LiteralPath $_.FullName -Destination $dest -Force
}
$env:GOCACHE=Join-Path $tools 'go-build-cache'
$env:GOMODCACHE=Join-Path $tools 'go-mod-cache'
$env:GOPATH=Join-Path $tools 'gopath'
$env:GOTMPDIR=Join-Path $tools 'tmp'
$env:GOTOOLCHAIN='local'
$env:CGO_ENABLED='0'
& $go -C $lolo test ./... -count=1
if ($LASTEXITCODE -ne 0) { throw 'Server tests failed; binary not replaced.' }
if (!$TestOnly) {
    $exe = Join-Path $lolo 'bin/Lolo_windows_amd64.exe'
    if ($Stage) { $exe = Join-Path $lolo 'bin/Lolo_windows_amd64.next.exe' }
    if (Get-Process -Name Lolo_windows_amd64,Lolo_windows_amd64.next -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $exe }) { throw 'Server is running. Stop it with Ctrl+C in its console before replacing the binary.' }
    if (Test-Path $exe) {
        $backup=Join-Path $PSScriptRoot ('backups/build-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
        New-Item -ItemType Directory -Force $backup | Out-Null
        Copy-Item $exe $backup
    }
    & $go -C $lolo build -trimpath -ldflags '-s -w -X gucooing/lolo/pkg.Commit=375423e-local-fixes -X gucooing/lolo/pkg.ServerVersion=local-sandbox-12' -o $exe ./main.go
    if ($LASTEXITCODE -ne 0) { throw 'Server build failed.' }
    Get-FileHash $exe -Algorithm SHA256
}
