# Send normal Ctrl+C only to the verified server console; never force-kill.
$ErrorActionPreference = 'Stop'
$exe = Join-Path $PSScriptRoot 'Lolo/bin/Lolo_windows_amd64.exe'
$server = @(Get-Process -Name Lolo_windows_amd64 -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $exe })
if ($server.Count -eq 0) { Write-Host 'Server is already stopped.'; return }
if ($server.Count -ne 1) { throw 'Multiple server processes; no signal sent.' }
$target = $server[0]
$allowed = @([uint32]$PID, [uint32]$target.Id)
$ancestor = Get-CimInstance Win32_Process -Filter "ProcessId = $($target.Id)"
for ($depth = 0; $depth -lt 8; $depth++) {
    $ancestor = Get-CimInstance Win32_Process -Filter "ProcessId = $($ancestor.ParentProcessId)"
    if (!$ancestor -or $ancestor.Name -notin @('powershell.exe', 'pwsh.exe')) { break }
    $allowed += [uint32]$ancestor.ProcessId
}
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public class LoloConsoleStop {
 [DllImport("kernel32.dll", SetLastError=true)] public static extern bool FreeConsole();
 [DllImport("kernel32.dll", SetLastError=true)] public static extern bool AttachConsole(uint pid);
 [DllImport("kernel32.dll", SetLastError=true)] public static extern uint GetConsoleProcessList(uint[] pids, uint size);
 [DllImport("kernel32.dll", SetLastError=true)] public static extern bool SetConsoleCtrlHandler(IntPtr handler, bool add);
 [DllImport("kernel32.dll", SetLastError=true)] public static extern bool GenerateConsoleCtrlEvent(uint evt, uint group);
}
'@
[LoloConsoleStop]::FreeConsole() | Out-Null
if (![LoloConsoleStop]::AttachConsole([uint32]$target.Id)) { throw 'Cannot attach server console; no signal sent. Use Ctrl+C in its original console.' }
try {
    $ids = New-Object uint32[] 64
    $count = [LoloConsoleStop]::GetConsoleProcessList($ids, 64)
    if ($count -eq 0 -or $count -gt 64) { throw 'Unexpected console membership; no signal sent.' }
    foreach ($id in $ids[0..($count - 1)]) {
        if ($id -notin $allowed) { throw "Unrelated process $id shares console; no signal sent. Use Ctrl+C in its original console." }
    }
    [LoloConsoleStop]::SetConsoleCtrlHandler([IntPtr]::Zero, $true) | Out-Null
    if (![LoloConsoleStop]::GenerateConsoleCtrlEvent(0, 0)) { throw 'Ctrl+C signal failed.' }
} finally {
    [LoloConsoleStop]::FreeConsole() | Out-Null
}
if (!$target.WaitForExit(15000)) { throw 'Server has not finished graceful shutdown; no force-kill performed.' }
Write-Host 'Server exited after normal Ctrl+C. Verify save-complete log before replacing files.'
