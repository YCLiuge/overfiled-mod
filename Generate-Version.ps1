param([string]$ModsDirectory = (Join-Path $PSScriptRoot 'GameMods\Mods'))
$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE = 'true'
$env:MSBuildEnableWorkloadResolver = 'false'
& dotnet build (Join-Path $PSScriptRoot 'VersionManifestGenerator\VersionManifestGenerator.csproj') -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw 'Manifest generator build failed.' }
& dotnet (Join-Path $PSScriptRoot 'VersionManifestGenerator\bin\Release\VersionManifestGenerator.dll') ([IO.Path]::GetFullPath($ModsDirectory))
if ($LASTEXITCODE -ne 0) { throw "Manifest generation failed ($LASTEXITCODE)." }
