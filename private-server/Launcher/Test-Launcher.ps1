# Isolated process lifecycle test. Never opens the real server or database.
$ErrorActionPreference = 'Stop'
$serverRoot = Split-Path $PSScriptRoot -Parent
$fixture = [IO.Path]::GetFullPath((Join-Path $serverRoot ('runtime-tools/launcher-test-' + [guid]::NewGuid().ToString('N'))))
$test = $null
try {
New-Item -ItemType Directory -Force $fixture, (Join-Path $fixture 'Lolo/bin'), (Join-Path $fixture 'TcpBridge/bin'), (Join-Path $fixture 'Launcher') | Out-Null
$csc = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
foreach ($port in @(18080,11000,12000,10086)) {
    if (Get-NetTCPConnection -State Listen -LocalPort $port -ErrorAction SilentlyContinue) { throw "Test port $port occupied; no services touched." }
}
$stub = @'
using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
class Stub {
 static void Main() {
  var done=new ManualResetEvent(false);
  Console.CancelKeyPress += delegate(object s, ConsoleCancelEventArgs e) {e.Cancel=true;done.Set();};
  bool bridge=AppDomain.CurrentDomain.FriendlyName.Contains("TcpBridge");
  foreach(int port in bridge ? new[]{10086} : new[]{18080,11000,12000}) {
   var listener=new TcpListener(IPAddress.Loopback,port);listener.Start();
  }
  done.WaitOne();
  File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"graceful-stop.txt"),"saved");
 }
}
'@
[IO.File]::WriteAllText((Join-Path $fixture 'stub.cs'), $stub)
& $csc /nologo /codepage:65001 /target:exe ("/out:" + (Join-Path $fixture 'Lolo/bin/Lolo_windows_amd64.exe')) (Join-Path $fixture 'stub.cs')
if ($LASTEXITCODE -ne 0) { throw 'Fixture compilation failed' }
Copy-Item (Join-Path $fixture 'Lolo/bin/Lolo_windows_amd64.exe') (Join-Path $fixture 'TcpBridge/bin/OverField.TcpBridge.exe')
Copy-Item (Join-Path $serverRoot 'Start-Server.ps1'),(Join-Path $serverRoot 'Start-PublicBridge.ps1'),(Join-Path $serverRoot 'Stop-Server.ps1') $fixture
Copy-Item (Join-Path $PSScriptRoot 'Run-Console.ps1') (Join-Path $fixture 'Launcher')
'{"HttpNet":{"InnerPort":18080},"GateWay":{"OuterPort":11000},"LogServer":{"OuterPort":12000}}' | Set-Content (Join-Path $fixture 'Lolo/config.json') -Encoding UTF8
'{}' | Set-Content (Join-Path $fixture 'TcpBridge/server.json')
$harness = @'
using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;
using OverFieldLauncher;
class LauncherTest {
 [STAThread] static int Main(string[] args) {
  if(args.Length == 3 && args[0] == "--stop") return Program.Main(args);
  Application.EnableVisualStyles();
  using(var form=new LauncherForm(args[0])) {
   form.Show();Application.DoEvents();
   try {
    Run(form,false,false); Run(form,false,false); Run(form,true,false); Run(form,false,true);
    foreach(string name in new[]{"Lolo/bin/graceful-stop.txt","TcpBridge/bin/graceful-stop.txt"})
      if(!File.Exists(Path.Combine(args[0],name))) throw new Exception("Graceful stop marker missing: "+name);
    Console.WriteLine("PASS: local start, duplicate start, bridge start, graceful stop of both isolated fixtures.");return 0;
   } catch(Exception ex) {Console.Error.WriteLine(ex);return 1;}
   finally {form.Close();}
  }
 }
 static void Run(Form form,bool bridge,bool stop) {
  var method=typeof(LauncherForm).GetMethod("Operate",BindingFlags.NonPublic|BindingFlags.Instance);
  var task=(Task)method.Invoke(form,new object[]{bridge,stop});
  while(!task.IsCompleted){Application.DoEvents();System.Threading.Thread.Sleep(25);}
  task.GetAwaiter().GetResult();
  var field=typeof(LauncherForm).GetField("log",BindingFlags.NonPublic|BindingFlags.Instance);
  var text=((TextBox)field.GetValue(form)).Text;
  File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"test-log.txt"),text);
  if(text.Contains("\u64cd\u4f5c\u5931\u8d25"))throw new Exception(text);
 }
}
'@
[IO.File]::WriteAllText((Join-Path $fixture 'harness.cs'), $harness, (New-Object Text.UTF8Encoding($true)))
& $csc /nologo /codepage:65001 /target:winexe /main:LauncherTest ("/out:" + (Join-Path $fixture 'Test.exe')) /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll /reference:System.Management.dll (Join-Path $PSScriptRoot 'Launcher.cs') (Join-Path $fixture 'harness.cs')
if ($LASTEXITCODE -ne 0) { throw 'Test harness compilation failed' }
$info = New-Object Diagnostics.ProcessStartInfo
$info.FileName = Join-Path $fixture 'Test.exe'
$info.Arguments = '"' + $fixture + '"'
$info.UseShellExecute = $false
$test = [Diagnostics.Process]::Start($info)
if (!$test.WaitForExit(100000)) { throw "Lifecycle test timed out. Check fixture: $fixture" }
if (Test-Path (Join-Path $fixture 'test-log.txt')) { Write-Output ([IO.File]::ReadAllText((Join-Path $fixture 'test-log.txt'))) }
if ($test.ExitCode -ne 0) { throw "Launcher lifecycle test failed. Fixture retained: $fixture" }
} finally {
    # Stop the driver first so it cannot launch more fixtures during cleanup.
    # Never use process names alone or kill a process tree: real services may be running.
    $cleanupErrors = @()
    $fixtureExecutables = @('Test.exe', 'Lolo/bin/Lolo_windows_amd64.exe', 'TcpBridge/bin/OverField.TcpBridge.exe')
    foreach ($relativeExe in $fixtureExecutables) {
        $expectedPath = [IO.Path]::GetFullPath((Join-Path $fixture $relativeExe))
        foreach ($process in [Diagnostics.Process]::GetProcessesByName([IO.Path]::GetFileNameWithoutExtension($expectedPath))) {
            try {
                # Hold the process handle before checking its path, avoiding PID reuse.
                $null = $process.Handle
                try { $actualPath = $process.MainModule.FileName }
                catch {
                    if (!$process.HasExited) { Write-Warning "Cannot verify process $($process.Id); leaving it untouched: $_" }
                    continue
                }
                if (![string]::Equals($actualPath, $expectedPath, [StringComparison]::OrdinalIgnoreCase)) { continue }
                try {
                    if (!$process.HasExited) { $process.Kill() }
                    if (!$process.WaitForExit(5000)) { throw "Fixture process $($process.Id) did not exit: $expectedPath" }
                } catch {
                    if (!$process.HasExited) { $cleanupErrors += "${expectedPath}: $_" }
                }
            } catch {
                # An unrelated or already exited process must never broaden cleanup scope.
                if (!$process.HasExited) { $cleanupErrors += "Cannot inspect process $($process.Id): $_" }
            } finally { $process.Dispose() }
        }
    }
    if ($null -ne $test) { $test.Dispose() }
    if ($cleanupErrors.Count -gt 0) { throw ("Isolated fixture cleanup failed: " + ($cleanupErrors -join '; ')) }
}
Write-Output 'PASS: local start, duplicate start, bridge start and graceful stop of both isolated fixtures.'
