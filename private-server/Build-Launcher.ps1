$ErrorActionPreference = 'Stop'
$csc = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
if (!(Test-Path -LiteralPath $csc)) { $csc = Join-Path $env:WINDIR 'Microsoft.NET/Framework/v4.0.30319/csc.exe' }
if (!(Test-Path -LiteralPath $csc)) { throw 'Windows .NET Framework 4.x compiler is required.' }
$output = Join-Path $PSScriptRoot 'OverField.ServerLauncher.exe'
& $csc /nologo /codepage:65001 /target:winexe /platform:anycpu /optimize+ /utf8output "/out:$output" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll /reference:System.Management.dll (Join-Path $PSScriptRoot 'Launcher/Launcher.cs')
if ($LASTEXITCODE -ne 0) { throw 'Launcher build failed.' }
Write-Host "Launcher built: $output"
