$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
& dotnet build (Join-Path $PSScriptRoot 'ChineseChessModule.csproj') -c Release -p:Platform=x64 -p:MSBuildEnableWorkloadResolver=false
if ($LASTEXITCODE -ne 0) { throw 'Chinese chess build failed' }
& dotnet run --project (Join-Path $root 'tools/ChineseChessVerification/ChineseChessVerification.csproj') -c Release
if ($LASTEXITCODE -ne 0) { throw 'Chinese chess rules verification failed' }
& dotnet run --project (Join-Path $root 'tools/ChineseChessIntegrationVerification/Verify.csproj') -c Release
if ($LASTEXITCODE -ne 0) { throw 'Chinese chess runtime verification failed' }
$package = Join-Path $root 'GameMods/ChineseChess-1.0.0'
$moduleSource = Join-Path $package 'ModuleSource'
New-Item -ItemType Directory -Force -Path $moduleSource | Out-Null
Copy-Item (Join-Path $root 'GameMods/Mods/ChineseChessModule.dll') $moduleSource -Force
Copy-Item (Join-Path $PSScriptRoot 'README.md') (Join-Path $package 'README.md') -Force
$manifest = Get-Content (Join-Path $root 'GameMods/Mods/version.json') -Raw | ConvertFrom-Json
$entry = @($manifest.modules | Where-Object { $_.id -eq 'chinese_chess' })
if ($entry.Count -ne 1) { throw 'Chess manifest entry missing or duplicated' }
@{ modules = $entry } | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $package 'module-entry.json') -Encoding utf8
$archive = Join-Path $root 'GameMods/ChineseChess-1.0.0.zip'
Compress-Archive -Path (Join-Path $package '*') -DestinationPath $archive -Force
Write-Output "Package: $archive"
