$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE = 'true'
$env:MSBuildEnableWorkloadResolver = 'false'

& dotnet build (Join-Path $PSScriptRoot 'HotUpdateVerification.csproj') -c Release --nologo -p:NuGetAudit=false
if ($LASTEXITCODE -ne 0) { throw 'Verification harness build failed.' }
& dotnet build (Join-Path $PSScriptRoot 'Fixtures/Fixtures.csproj') -c Release --nologo -p:NuGetAudit=false
if ($LASTEXITCODE -ne 0) { throw 'Fixture build failed.' }
& dotnet (Join-Path $PSScriptRoot 'bin/Release/net6.0/HotUpdateVerification.dll') (Join-Path $PSScriptRoot 'Fixtures/bin/Release/net6.0/Fixtures.dll')
if ($LASTEXITCODE -ne 0) { throw 'Hot update verification failed.' }
