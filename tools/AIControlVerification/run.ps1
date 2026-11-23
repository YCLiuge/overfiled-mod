param(
    [string]$RuntimeVersion
)

$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE = 'true'
$env:MSBuildEnableWorkloadResolver = 'false'
$project = Join-Path $PSScriptRoot 'AIControlVerification.csproj'
$config = Join-Path $PSScriptRoot 'NuGet.Config'
$result = Join-Path $PSScriptRoot 'verification-results.txt'
$source = (Resolve-Path (Join-Path $PSScriptRoot '../../Logic/*/AIControlHttpServer.cs')).Path
Write-Output ('Transport SHA256: ' + (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash)
dotnet build $project --configfile $config --verbosity minimal
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
if ($RuntimeVersion) {
    $assembly = Join-Path $PSScriptRoot 'bin/Debug/net6.0/AIControlVerification.dll'
    dotnet --fx-version $RuntimeVersion $assembly | Tee-Object -Variable runOutput
}
else {
    dotnet run --project $project --no-build | Tee-Object -Variable runOutput
}
$runExitCode = $LASTEXITCODE
$runOutput | Set-Content -LiteralPath $result -Encoding UTF8
exit $runExitCode
