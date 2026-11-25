[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$BaseVersion,
    [Parameter(Mandatory=$true)][string]$ResourceVersion,
    [string]$ServerRoot
)
$ErrorActionPreference = 'Stop'
if (!$ServerRoot) { $ServerRoot = Join-Path $PSScriptRoot 'Lolo' }
foreach ($version in @($BaseVersion, $ResourceVersion)) {
    if ($version -notmatch '^\d{4}-\d{2}-\d{2}-\d{2}-\d{2}-\d{2}$') { throw 'Unexpected client version format; no mapping changed.' }
}
$mapPath = Join-Path $ServerRoot 'data/ClientVersion.json'
$map = Get-Content -LiteralPath $mapPath -Raw | ConvertFrom-Json
if (!$map.VersionMap) { throw 'ClientVersion.json has no VersionMap.' }
$expected = $BaseVersion + '_' + $ResourceVersion
$old = $map.VersionMap.PSObject.Properties[$BaseVersion]
if ($old -and $old.Value -eq $expected) { Write-Host "Client version already registered: $expected"; return }
if ($old) {
    $oldResource = ([string]$old.Value -split '_', 2)[-1]
    if ([string]::CompareOrdinal($oldResource, $ResourceVersion) -gt 0) { throw 'Resource version is older than server requirement; mapping was not downgraded.' }
}
Copy-Item -LiteralPath $mapPath -Destination ($mapPath + '.' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff') + '.bak')
$map.VersionMap | Add-Member -NotePropertyName $BaseVersion -NotePropertyValue $expected -Force
[IO.File]::WriteAllText($mapPath, ($map | ConvertTo-Json -Depth 8), (New-Object Text.UTF8Encoding($false)))
Write-Host "Registered client version: $expected. Restart the server normally to load it."
