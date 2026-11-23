param([string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $root 'artifacts\ChessServer-Board' }
$serverRoot = Split-Path $PSScriptRoot -Parent
$project = Join-Path $serverRoot 'Chat.Server\Chat.Server.csproj'
# Explicit project and Release: never copy an old Debug binary or publish the whole solution.
& dotnet publish $project -c Release --self-contained false -p:UseAppHost=false --nologo -o $OutputDirectory
if ($LASTEXITCODE -ne 0) { throw "BOARD server publish failed: $LASTEXITCODE" }
$files = 'Chat.Server.dll','Chat.Server.deps.json','Chat.Server.runtimeconfig.json'
$manifest = foreach ($name in $files) {
    $file = Join-Path $OutputDirectory $name
    if (-not (Test-Path -LiteralPath $file)) { throw "Missing deployment file: $file" }
    [ordered]@{ file=$name; sha256=(Get-FileHash -Algorithm SHA256 -LiteralPath $file).Hash; bytes=(Get-Item -LiteralPath $file).Length }
}
[ordered]@{ build='1.0.1-board'; framework='net9.0'; selfContained=$false; sourceProject=$project; publishedUtc=[DateTime]::UtcNow.ToString('o'); files=@($manifest) } | ConvertTo-Json -Depth 5 | Set-Content -Encoding utf8 (Join-Path $OutputDirectory 'build-manifest.json')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'BOARD-DEPLOY.md') -Destination (Join-Path $OutputDirectory 'DEPLOY.md') -Force
Write-Host "Published BOARD server: $OutputDirectory"
Write-Host "Run: dotnet `"$(Join-Path $OutputDirectory 'Chat.Server.dll')`" --host 0.0.0.0 --port 9000"
