$ErrorActionPreference='Stop'
$env:DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE='true'
$env:MSBuildEnableWorkloadResolver='false'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
& dotnet build (Join-Path $root '服务器\Chat.Server\Chat.Server.csproj') -c Release --nologo -v:minimal
if($LASTEXITCODE -ne 0){throw 'Relay build failed'}
& dotnet run --project (Join-Path $root 'tools\ChannelRelayVerification\ChannelRelayVerification.csproj') -c Release
if($LASTEXITCODE -ne 0){throw 'Channel relay verification failed'}
& dotnet run --project (Join-Path $root 'tools\DrawGuessVerification\DrawGuessVerification.csproj') -c Release
if($LASTEXITCODE -ne 0){throw 'Authority verification failed'}
& dotnet run --project (Join-Path $root 'tools\SharedCanvasVerification\SharedCanvasVerification.csproj') -c Release
if($LASTEXITCODE -ne 0){throw 'Shared canvas verification failed'}
& dotnet run --project (Join-Path $root 'tools\PaintingExportVerification\PaintingExportVerification.csproj') -c Release
if($LASTEXITCODE -ne 0){throw 'Painting export verification failed'}
Push-Location $root
try {
 & dotnet run --project (Join-Path $root 'tools\DrawGuessIntegrationVerification\Verify.csproj') -c Release -- (Join-Path $root '服务器\Chat.Server\bin\Release\net9.0\Chat.Server.dll')
 if($LASTEXITCODE -ne 0){throw 'Runtime integration failed'}
} finally {Pop-Location}
