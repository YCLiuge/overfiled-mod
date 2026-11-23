param([string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$workspaceRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $workspaceRoot 'artifacts/furniture-catalog' }
$exportHelper = (Join-Path $PSScriptRoot 'binV3/Release/net6.0/FurnitureExportV3.dll').Replace('\','/')
$exportDirectory = $OutputDirectory.Replace('\','/')
if ($exportHelper.Contains(' ') -or $exportDirectory.Contains(' ')) { throw 'The game call parser requires paths without spaces.' }
function Invoke-GameRead([string]$Command) {
    $body = @{command=$Command;target='logic';waitMs=5000} | ConvertTo-Json
    $reply = Invoke-RestMethod -Uri 'http://127.0.0.1:8080/api/command' -Method Post -ContentType 'application/json; charset=utf-8' -Body ([System.Text.Encoding]::UTF8.GetBytes($body)) -TimeoutSec 20
    if ($reply.code -ne 200 -or $reply.data.status -ne 'completed') { throw ($reply | ConvertTo-Json -Depth 8) }
    [pscustomobject]@{Id=$reply.data.id;Status=$reply.data.status;Command=$Command} | Format-List
}
Invoke-GameRead "call System.Reflection.Assembly.LoadFile $exportHelper"
$exportStartedAt = [DateTime]::UtcNow
Invoke-GameRead "call FurnitureExport.ExporterV3.Run $exportDirectory"
$summaryFile = Join-Path $OutputDirectory 'summary.json'
if (-not (Test-Path -LiteralPath $summaryFile) -or (Get-Item -LiteralPath $summaryFile).LastWriteTimeUtc -lt $exportStartedAt) {
    throw "Export did not produce a fresh summary; inspect $OutputDirectory/error.json and the game log."
}
Write-Output "Export complete: $OutputDirectory"
