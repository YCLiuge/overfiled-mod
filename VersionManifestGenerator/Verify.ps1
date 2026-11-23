$ErrorActionPreference = 'Stop'
$workspace = Split-Path -Parent $PSScriptRoot
$generator = Join-Path $PSScriptRoot 'bin\Release\VersionManifestGenerator.dll'
if (!(Test-Path -LiteralPath $generator)) { throw 'Build the Release solution first.' }
$root = Join-Path $PSScriptRoot ('verification-output\' + [guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($root)
function Assert($condition, [string]$message) {
    if (!$condition) { throw "FAIL: $message" }
    Write-Output "PASS: $message"
}
function Generate {
    & dotnet $generator $root
    if ($LASTEXITCODE -ne 0) { throw 'Generator failed.' }
}
$mods = Join-Path $workspace 'GameMods\Mods'
Copy-Item -LiteralPath (Join-Path $mods 'mod.dll') -Destination (Join-Path $root 'MOD.dll')
Copy-Item -LiteralPath (Join-Path $mods 'Logic.dll') -Destination $root
Copy-Item -LiteralPath (Join-Path $mods 'HotUpdateModule.dll') -Destination $root
# A valid managed support assembly must not become a hot-update module.
Copy-Item -LiteralPath $generator -Destination (Join-Path $root 'Support.dll')
Generate
$manifestPath = Join-Path $root 'version.json'
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
Assert ($manifest.modules.Count -eq 2) 'fresh generation includes exactly two modules, excluding MOD.dll and support DLL'
Assert (($manifest.modules.id -join ',') -eq 'logic_main,hot_update_demo') 'IDs and priority ordering come from assembly metadata'
$bytes = [IO.File]::ReadAllBytes($manifestPath)
Assert (!($bytes[0] -eq 239 -and $bytes[1] -eq 187 -and $bytes[2] -eq 191)) 'manifest is UTF-8 without BOM'
foreach ($module in $manifest.modules) {
    $actual = (Get-FileHash -LiteralPath (Join-Path $root $module.url) -Algorithm SHA256).Hash
    Assert ($actual -eq $module.hash) "SHA256 is correct for $($module.id)"
}
$initialHash = (Get-FileHash -LiteralPath $manifestPath).Hash
$processes = @()
for ($i = 0; $i -lt 4; $i++) {
    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = 'dotnet'
    $startInfo.Arguments = '"' + $generator + '" "' + $root + '"'
    $startInfo.UseShellExecute = $false
    $processes += [Diagnostics.Process]::Start($startInfo)
}
$exitCodes = @()
foreach ($process in $processes) {
    $process.WaitForExit()
    $exitCodes += $process.ExitCode
    $process.Dispose()
}
Assert (($exitCodes | Where-Object { $_ -ne 0 }).Count -eq 0) 'all four concurrent generators exit successfully'
Assert ((Get-FileHash -LiteralPath $manifestPath).Hash -eq $initialHash) 'concurrent scans publish a deterministic complete manifest'
Copy-Item -LiteralPath (Join-Path $root 'HotUpdateModule.dll') -Destination (Join-Path $root 'Duplicate.dll')
& dotnet $generator $root
Assert ($LASTEXITCODE -ne 0) 'duplicate module ID fails generation'
Assert ((Get-FileHash -LiteralPath $manifestPath).Hash -eq $initialHash) 'failed generation preserves the previous manifest'
# This is a generated fixture directory beneath verification-output, verified before deletion.
$duplicatePath = [IO.Path]::GetFullPath((Join-Path $root 'Duplicate.dll'))
$hotPath = [IO.Path]::GetFullPath((Join-Path $root 'HotUpdateModule.dll'))
Assert ($duplicatePath.StartsWith($root + '\') -and $hotPath.StartsWith($root + '\')) 'fixture deletion paths are within the generated test directory'
Remove-Item -LiteralPath $duplicatePath, $hotPath
Generate
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
Assert ($manifest.modules.Count -eq 1 -and $manifest.modules[0].id -eq 'logic_main') 'deleted module is removed on the next scan'
Write-Output "VERIFIED: generator integration. Fixtures: $root"
