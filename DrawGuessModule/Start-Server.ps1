<#
.SYNOPSIS
Starts the module file service or the matching drawing-room relay.
.DESCRIPTION
Files defaults to TCP 10086. Game defaults to TCP 9000 for the local
127.0.0.1:9000 preset. The public client preset is 113.219.237.106:15602;
its server must deploy this matching updated relay and expose that port.
This script does not deploy to or verify the public server.
.EXAMPLE
.\Start-Server.ps1 -Kind Game
.EXAMPLE
.\Start-Server.ps1 -Kind Game -Port 15602 -BindHost 0.0.0.0
Run on the public server when deploying its matching relay.
#>
param([ValidateSet('Files','Game')][string]$Kind='Files',[ValidateRange(0,65535)][int]$Port=0,[string]$BindHost='0.0.0.0')
$ErrorActionPreference='Stop'
$env:DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE='true'
$env:MSBuildEnableWorkloadResolver='false'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if($Kind -eq 'Game'){
 if(!$Port){$Port=9000}
 $project=Join-Path $root '服务器\Chat.Server\Chat.Server.csproj'
 & dotnet build $project -c Release --nologo -v:minimal
 if($LASTEXITCODE -ne 0){throw 'Game relay build failed'}
 & dotnet (Join-Path $root '服务器\Chat.Server\bin\Release\net9.0\Chat.Server.dll') --host $BindHost --port $Port
}else{
 if(!$Port){$Port=10086}
 $mods=Join-Path $root 'GameMods\Mods'
 if(!(Test-Path -LiteralPath (Join-Path $mods 'version.json'))){throw 'Build the modules first'}
 & dotnet build (Join-Path $root 'TcpFileServer\TcpFileServer.csproj') -c Release --nologo -v:minimal
 if($LASTEXITCODE -ne 0){throw 'File server build failed'}
 & dotnet (Join-Path $root 'TcpFileServer\bin\Release\net6.0\win-x64\TcpFileServer.dll') $Port $mods
}
if($LASTEXITCODE -ne 0){throw "Server exited ($LASTEXITCODE)"}
