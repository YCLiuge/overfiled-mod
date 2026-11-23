param([ValidateSet('Debug','Release')][string]$Configuration='Release',[ValidatePattern('^\d+\.\d+\.\d+(\.\d+)?$')][string]$Version)
$ErrorActionPreference='Stop'
$env:DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE='true'
$env:MSBuildEnableWorkloadResolver='false'
$buildArgs=@('build',(Join-Path $PSScriptRoot 'DrawGuessModule.csproj'),'-c',$Configuration,'-p:Platform=x64','--nologo')
if($Version){$buildArgs+="-p:Version=$Version"}
& dotnet @buildArgs
if($LASTEXITCODE -ne 0){throw "Build failed ($LASTEXITCODE)"}
Write-Output "Ready: $([IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\GameMods\Mods')))"
