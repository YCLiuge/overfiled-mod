param(
    [string]$GameRoot = 'D:\TapTap\PC Games\176228',
    [string]$CecilPath = (Join-Path $PSScriptRoot '..\..\libs\Mono.Cecil.dll')
)
$ErrorActionPreference = 'Stop'
[void][Reflection.Assembly]::Load([IO.File]::ReadAllBytes((Resolve-Path $CecilPath)))
$interop = Join-Path $GameRoot 'MelonLoader\Il2CppAssemblies'
$game = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $interop 'Assembly-CSharp.dll'))
$yoo = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $interop 'Il2CppYooAsset.dll'))
$script:checks = 0
function Check($ok, $message) { if (!$ok) { throw $message }; $script:checks++ }
function GetMetadataType($assembly, $name) {
    $result = $assembly.MainModule.Types | Where-Object FullName -eq $name
    Check ($null -ne $result) ('Missing type: ' + $name)
    return $result
}
function Method($type, $name, $returnType, $parameters) {
    $result = @($type.Methods | Where-Object { $_.Name -eq $name -and $_.ReturnType.FullName -eq $returnType -and (($_.Parameters | ForEach-Object {$_.ParameterType.FullName}) -join ',') -eq $parameters })
    Check ($result.Count -eq 1) ('Method signature mismatch: ' + $type.FullName + '::' + $name)
}
function Property($type, $name, $valueType, $isStatic, $canWrite) {
    $p = $type.Properties | Where-Object Name -eq $name
    Check ($null -ne $p -and $p.PropertyType.FullName -eq $valueType -and $p.GetMethod.IsStatic -eq $isStatic -and (!$canWrite -or $null -ne $p.SetMethod)) ('Property contract mismatch: ' + $type.FullName + '::' + $name)
}
$hot = GetMetadataType $game 'Il2Cpp.HotUpdateSystem'
$res = GetMetadataType $game 'Il2Cpp.ResManager'
$config = GetMetadataType $game 'Il2Cpp.InitConfigAsset'
$settings = $config.NestedTypes | Where-Object Name -eq 'ConfigProperty'
Check ($null -ne $settings) 'Missing ConfigProperty'
$package = GetMetadataType $yoo 'Il2CppYooAsset.ResourcePackage'
Method $config 'GetConfig' 'Il2Cpp.InitConfigAsset/ConfigProperty' ''
Method $hot 'StartHotUpdate' 'System.Void' 'Il2CppSystem.Action'
Method $hot 'OnHotUpdateEnd' 'System.Void' ''
Method $hot 'OnHotFixCS' 'System.Void' ''
Method $hot 'WarmUpShader' 'System.Void' ''
Method $package 'GetPackageVersion' 'System.String' ''
Property $settings 'YooAssetMode' 'Il2CppYooAsset.EPlayMode' $false $true
Property $res 'package' 'Il2CppYooAsset.ResourcePackage' $true $false
Property $res '_Init' 'System.Boolean' $true $false
Property $package 'InitializeStatus' 'Il2CppYooAsset.EOperationStatus' $false $false
Property $package '_playMode' 'Il2CppYooAsset.EPlayMode' $false $false
Property $hot '_endCallBack' 'Il2CppSystem.Action' $false $true
Property $hot '_originVersion' 'System.String' $false $true
$play = GetMetadataType $yoo 'Il2CppYooAsset.EPlayMode'
$status = GetMetadataType $yoo 'Il2CppYooAsset.EOperationStatus'
Check (($play.Fields | Where-Object Name -eq 'HostPlayMode').Constant -eq 2) 'HostPlayMode value changed'
Method $package 'CreateResourceDownloader' 'Il2CppYooAsset.ResourceDownloaderOperation' 'System.Int32,System.Int32,System.Int32'
$downloaderBase = GetMetadataType $yoo 'Il2CppYooAsset.DownloaderOperation'
Property $downloaderBase 'TotalDownloadCount' 'System.Int32' $false $false
Check (($status.Fields | Where-Object Name -eq 'Succeed').Constant -eq 2) 'Succeed value changed'
Write-Output "PASS: $script:checks hot-update metadata assertions; assemblies read only, no game startup or native hook execution."
Get-FileHash (Join-Path $interop 'Assembly-CSharp.dll') -Algorithm SHA256 | Select-Object Algorithm, Hash
Get-FileHash (Join-Path $interop 'Il2CppYooAsset.dll') -Algorithm SHA256 | Select-Object Algorithm, Hash
