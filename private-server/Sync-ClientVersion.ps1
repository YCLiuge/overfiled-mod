[CmdletBinding()]
param([string]$GameRoot = 'D:\TapTap\PC Games\176228', [string]$ServerRoot)
$ErrorActionPreference = 'Stop'
if (!$ServerRoot) { $ServerRoot = Join-Path $PSScriptRoot 'Lolo' }
$basePath = Join-Path $GameRoot 'version'
$manifestRoot = Join-Path $GameRoot 'launcher_Data/yoo/DefaultPackage/ManifestFiles'
$versionPath = Join-Path $manifestRoot 'PackageManifest_DefaultPackage.version'
$mapPath = Join-Path $ServerRoot 'data/ClientVersion.json'
# A machine without an installed client keeps the server's existing version mapping.
if (!(Test-Path -LiteralPath $basePath) -or !(Test-Path -LiteralPath $versionPath)) {
    Write-Host 'Client version files not found; existing server version map retained.'
    return
}
$base = ([IO.File]::ReadAllText($basePath)).Trim()
$resource = ([IO.File]::ReadAllText($versionPath)).Trim()
foreach ($version in @($base, $resource)) {
    if ($version -notmatch '^\d{4}-\d{2}-\d{2}-\d{2}-\d{2}-\d{2}$') { throw 'Unexpected client version format; no mapping changed.' }
}
$binaryManifest = Join-Path $manifestRoot "PackageManifest_DefaultPackage_$resource.bytes"
if (!(Test-Path -LiteralPath $binaryManifest)) { throw "Cached manifest missing: $binaryManifest" }
$map = Get-Content -LiteralPath $mapPath -Raw | ConvertFrom-Json
if (!$map.VersionMap) { throw 'ClientVersion.json has no VersionMap.' }
$expected = $base + '_' + $resource
$old = $map.VersionMap.PSObject.Properties[$base]
if ($old -and $old.Value -eq $expected) { Write-Host "Client version already synchronized: $expected"; return }
if ($old) {
    $oldResource = ([string]$old.Value -split '_', 2)[-1]
    if ([string]::CompareOrdinal($oldResource, $resource) -gt 0) { throw 'Cached resource is older than server requirement; mapping was not downgraded. Update the game first.' }
}
Copy-Item -LiteralPath $mapPath -Destination ($mapPath + '.' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff') + '.bak')
$map.VersionMap | Add-Member -NotePropertyName $base -NotePropertyValue $expected -Force
[IO.File]::WriteAllText($mapPath, ($map | ConvertTo-Json -Depth 8), (New-Object Text.UTF8Encoding($false)))
Write-Host "Synchronized local client version: $expected. Takes effect when the server starts."
