<#
.SYNOPSIS
Inspect, preview, or record only the visible game client region.
.EXAMPLE
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\Record-GameVideo.ps1 -Mode Inspect
.EXAMPLE
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\Record-GameVideo.ps1 -Mode Preview -Output .\artifacts\game-preview.png
.EXAMPLE
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\Record-GameVideo.ps1 -Mode Record -Duration 18 -Output .\artifacts\game-camera.mp4
.NOTES
Activates the game only with explicit -BringToFront. Does not send game commands
or change persistent OS settings. -BringToFront leaves the game foreground; the
original foreground window is included in the JSON report.
Recording uses gdigrab desktop with offsets restricted to the detected game client.
The window must remain visible, foreground, and stationary throughout recording.
Preview uses the same visible screen region; covered areas include the covering window.
Video is silent H.264/yuv420p, 30 fps, with fast-start metadata. Odd dimensions
are cropped by at most one pixel on the right/bottom to satisfy yuv420p.
#>
[CmdletBinding()]
param(
    [ValidateSet('Inspect','Preview','Record')][string]$Mode = 'Inspect',
    [string]$ProcessName = 'launcher',
    [int]$TargetProcessId = 0,
    [ValidateRange(1,300)][int]$Duration = 18,
    [string]$Output,
    [string]$FfmpegPath,
    [switch]$AllowBackground,
    [switch]$BringToFront,
    [switch]$WriteProgress,
    [switch]$Overwrite
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
if (-not ('GameVideoCapture.Native' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
namespace GameVideoCapture {
 public static class Native {
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
  [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
  [DllImport("user32.dll", SetLastError=true)] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll", SetLastError=true)] public static extern bool GetClientRect(IntPtr h, out RECT r);
  [DllImport("user32.dll", SetLastError=true)] public static extern bool ClientToScreen(IntPtr h, ref POINT p);
  [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int command);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr h);
  [DllImport("user32.dll")] public static extern IntPtr SetActiveWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern IntPtr SetFocus(IntPtr h);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, IntPtr processId);
  [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
  [DllImport("user32.dll", SetLastError=true)] public static extern bool AttachThreadInput(uint from, uint to, bool attach);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, System.Text.StringBuilder text, int maxCount);
  [DllImport("user32.dll", SetLastError=true)] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
  private delegate bool EnumWindowCallback(IntPtr h, IntPtr data);
  [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowCallback callback, IntPtr data);
  [DllImport("user32.dll", EntryPoint="GetWindowThreadProcessId")] private static extern uint GetWindowProcess(IntPtr h, out uint processId);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr h, System.Text.StringBuilder name, int maxCount);
  [DllImport("user32.dll", SetLastError=true)] public static extern bool SetWindowPos(IntPtr h, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
  public static IntPtr[] FindUnityWindows(uint processId) {
   var found = new System.Collections.Generic.List<IntPtr>();
   EnumWindows(delegate(IntPtr h, IntPtr data) {
    uint pid; GetWindowProcess(h, out pid);
    if (pid == processId && IsWindowVisible(h)) {
     var name = new System.Text.StringBuilder(256);
     GetClassName(h, name, name.Capacity);
     if (name.ToString() == "UnityWndClass") found.Add(h);
    }
    return true;
   }, IntPtr.Zero);
   return found.ToArray();
  }
 }
}
'@
}
function Set-GameForeground([IntPtr]$Handle) {
    if (-not [GameVideoCapture.Native]::IsWindow($Handle)) { throw 'Cannot focus a game window that no longer exists.' }
    if ([GameVideoCapture.Native]::IsIconic($Handle)) {
        [void][GameVideoCapture.Native]::ShowWindow($Handle, 9) # SW_RESTORE only when minimized.
        Start-Sleep -Milliseconds 200
    }
    [void][GameVideoCapture.Native]::BringWindowToTop($Handle)
    [void][GameVideoCapture.Native]::SetForegroundWindow($Handle)
    Start-Sleep -Milliseconds 250
    if ([GameVideoCapture.Native]::GetForegroundWindow() -eq $Handle) { return }
    $currentThread = [GameVideoCapture.Native]::GetCurrentThreadId()
    $foregroundThread = [GameVideoCapture.Native]::GetWindowThreadProcessId([GameVideoCapture.Native]::GetForegroundWindow(), [IntPtr]::Zero)
    $gameThread = [GameVideoCapture.Native]::GetWindowThreadProcessId($Handle, [IntPtr]::Zero)
    $attached = New-Object 'System.Collections.Generic.List[uint32]'
    try {
        foreach ($thread in @($foregroundThread, $gameThread) | Select-Object -Unique) {
            if ($thread -ne 0 -and $thread -ne $currentThread) {
                if ([GameVideoCapture.Native]::AttachThreadInput($currentThread, $thread, $true)) { $attached.Add($thread) }
            }
        }
        [void][GameVideoCapture.Native]::BringWindowToTop($Handle)
        [void][GameVideoCapture.Native]::SetForegroundWindow($Handle)
        [void][GameVideoCapture.Native]::SetActiveWindow($Handle)
        [void][GameVideoCapture.Native]::SetFocus($Handle)
    } finally {
        foreach ($thread in $attached) { [void][GameVideoCapture.Native]::AttachThreadInput($currentThread, $thread, $false) }
    }
    Start-Sleep -Milliseconds 300
    if ([GameVideoCapture.Native]::GetForegroundWindow() -ne $Handle) {
        throw 'Windows did not grant game foreground activation. Capture was not started.'
    }
}
function Get-ClientCaptureBounds([IntPtr]$Handle) {
    if (-not [GameVideoCapture.Native]::IsWindow($Handle)) { throw 'Game window no longer exists.' }
    if (-not [GameVideoCapture.Native]::IsWindowVisible($Handle)) { throw 'Game window is not visible.' }
    if ([GameVideoCapture.Native]::IsIconic($Handle)) { throw 'Game window is minimized. Restore it before capture.' }
    $client = New-Object GameVideoCapture.Native+RECT
    $window = New-Object GameVideoCapture.Native+RECT
    $origin = New-Object GameVideoCapture.Native+POINT
    if (-not [GameVideoCapture.Native]::GetWindowRect($Handle, [ref]$window)) { throw 'GetWindowRect failed.' }
    if (-not [GameVideoCapture.Native]::GetClientRect($Handle, [ref]$client)) { throw 'GetClientRect failed.' }
    if (-not [GameVideoCapture.Native]::ClientToScreen($Handle, [ref]$origin)) { throw 'ClientToScreen failed.' }
    $width = $client.Right - $client.Left
    $height = $client.Bottom - $client.Top
    if ($width -lt 64 -or $height -lt 64) { throw "Invalid game client size: ${width}x${height}." }
    $screen = [System.Windows.Forms.SystemInformation]::VirtualScreen
    if ($origin.X -lt $screen.Left -or $origin.Y -lt $screen.Top -or
        ($origin.X + $width) -gt $screen.Right -or ($origin.Y + $height) -gt $screen.Bottom) {
        throw 'Game client is partially off-screen. Move it fully onto the desktop before capture.'
    }
    [pscustomobject]@{
        X = $origin.X; Y = $origin.Y; Width = $width; Height = $height
        VideoWidth = $width - ($width % 2); VideoHeight = $height - ($height % 2)
        WindowLeft = $window.Left; WindowTop = $window.Top
        WindowRight = $window.Right; WindowBottom = $window.Bottom
        IsForeground = ([GameVideoCapture.Native]::GetForegroundWindow() -eq $Handle)
    }
}
function Find-FFmpeg {
    if ($FfmpegPath) {
        if (-not (Test-Path -LiteralPath $FfmpegPath -PathType Leaf)) { throw "FFmpeg does not exist: $FfmpegPath" }
        return (Resolve-Path -LiteralPath $FfmpegPath).Path
    }
    $command = Get-Command ffmpeg.exe -ErrorAction SilentlyContinue
    if ($command) { return $command.Source }
    $local = Join-Path $PSScriptRoot 'ffmpeg\bin\ffmpeg.exe'
    if (Test-Path -LiteralPath $local -PathType Leaf) { return $local }
    $pythonRoot = Join-Path $env:LOCALAPPDATA 'Programs\Python'
    if (Test-Path -LiteralPath $pythonRoot) {
        $matches = @(Get-ChildItem -Path (Join-Path $pythonRoot 'Python*\Lib\site-packages\imageio_ffmpeg\binaries\ffmpeg*.exe') -File -ErrorAction SilentlyContinue)
        if ($matches.Count -gt 0) { return $matches[0].FullName }
    }
    throw 'FFmpeg was not found. Supply -FfmpegPath with an executable supporting gdigrab and libx264.'
}
function Invoke-CaptureFFmpeg([string[]]$Arguments, [string]$LogPath) {
    # Windows PowerShell 5 turns native stderr into ErrorRecords. Preserve its text
    # in a log, then use the native exit code to decide whether capture succeeded.
    $savedPreference = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $lines = @(& $script:ResolvedFFmpeg @Arguments 2>&1)
        $nativeExit = $LASTEXITCODE
    } finally { $ErrorActionPreference = $savedPreference }
    $lines | ForEach-Object { $_.ToString() } | Set-Content -LiteralPath $LogPath -Encoding UTF8
    if ($nativeExit -ne 0) {
        $tail = ($lines | Select-Object -Last 12 | ForEach-Object { $_.ToString() }) -join [Environment]::NewLine
        throw "FFmpeg exited with code $nativeExit. Log: $LogPath`n$tail"
    }
}
$oldDpiContext = [GameVideoCapture.Native]::SetThreadDpiAwarenessContext([IntPtr](-4))
if ($oldDpiContext -eq [IntPtr]::Zero) { throw 'Could not enable temporary per-monitor DPI awareness for physical capture bounds.' }
try {
    if ($TargetProcessId -gt 0) {
        $game = Get-Process -Id $TargetProcessId
    } else {
        $games = @(Get-Process -Name $ProcessName -ErrorAction SilentlyContinue | Where-Object { [GameVideoCapture.Native]::FindUnityWindows([uint32]$_.Id).Count -gt 0 })
        if ($games.Count -ne 1) { throw "Expected one $ProcessName process with a visible UnityWndClass window, found $($games.Count). Supply -TargetProcessId." }
        $game = $games[0]
    }
    $unityWindows = [GameVideoCapture.Native]::FindUnityWindows([uint32]$game.Id)
    if ($unityWindows.Count -ne 1) { throw "Expected exactly one visible UnityWndClass window for process $($game.Id), found $($unityWindows.Count)." }
    $handle = $unityWindows[0]
    $gameTitle = New-Object System.Text.StringBuilder 1024
    [void][GameVideoCapture.Native]::GetWindowText($handle, $gameTitle, $gameTitle.Capacity)
    $originalForeground = [GameVideoCapture.Native]::GetForegroundWindow()
    $originalTitle = New-Object System.Text.StringBuilder 1024
    [void][GameVideoCapture.Native]::GetWindowText($originalForeground, $originalTitle, $originalTitle.Capacity)
    if ($BringToFront) {
        if ($Mode -eq 'Inspect') { throw '-BringToFront is supported only with Preview or Record.' }
        Set-GameForeground $handle
    }
    $bounds = Get-ClientCaptureBounds $handle
    $report = [ordered]@{
        Mode = $Mode; ProcessId = $game.Id; ProcessName = $game.ProcessName
        Title = $gameTitle.ToString(); WindowClass = 'UnityWndClass'; WindowHandle = $handle.ToInt64(); Bounds = $bounds
        BringToFront = [bool]$BringToFront
        OriginalForegroundWindowHandle = $originalForeground.ToInt64()
        OriginalForegroundWindowTitle = $originalTitle.ToString()
    }
    if ($Mode -eq 'Inspect') { $report | ConvertTo-Json -Depth 5; return }
    if ($Mode -eq 'Record' -and -not $bounds.IsForeground -and -not $AllowBackground) {
        throw 'Game is not the foreground window. Use -BringToFront or bring it forward separately. -AllowBackground bypasses this check only when the client is known to be unobscured.'
    }
    if (-not $bounds.IsForeground) { Write-Warning 'Game is not foreground. Visible-region capture can include covering windows.' }
    if (-not $Output) {
        $extension = if ($Mode -eq 'Preview') { 'png' } else { 'mp4' }
        $Output = Join-Path $PSScriptRoot ("..\artifacts\game-{0}.{1}" -f (Get-Date -Format 'yyyyMMdd-HHmmss'), $extension)
    }
    $Output = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Output)
    $expectedExtension = if ($Mode -eq 'Preview') { '.png' } else { '.mp4' }
    if ([IO.Path]::GetExtension($Output) -ne $expectedExtension) { throw "Output must have a $expectedExtension extension." }
    if ((Test-Path -LiteralPath $Output) -and -not $Overwrite) { throw "Output exists. Choose another path or use -Overwrite: $Output" }
    [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($Output))
    if ($Mode -eq 'Preview') {
        $bitmap = New-Object Drawing.Bitmap($bounds.Width, $bounds.Height)
        $graphics = [Drawing.Graphics]::FromImage($bitmap)
        try {
            $graphics.CopyFromScreen($bounds.X, $bounds.Y, 0, 0, $bitmap.Size, [Drawing.CopyPixelOperation]::SourceCopy)
            $bitmap.Save($Output, [Drawing.Imaging.ImageFormat]::Png)
        } finally { $graphics.Dispose(); $bitmap.Dispose() }
    } else {
        $script:ResolvedFFmpeg = Find-FFmpeg
        $report.FFmpeg = $script:ResolvedFFmpeg
        $report.DurationSeconds = $Duration
        $report.FramesPerSecond = 30
        $report.Codec = 'h264'
        $report.PixelFormat = 'yuv420p'
        $logPath = $Output + '.ffmpeg.log'
        $arguments = @('-hide_banner', '-nostdin', '-nostats', '-y', '-f', 'gdigrab',
            '-framerate', '30', '-draw_mouse', '0', '-offset_x', [string]$bounds.X,
            '-offset_y', [string]$bounds.Y, '-video_size', "$($bounds.Width)x$($bounds.Height)",
            '-i', 'desktop', '-t', [string]$Duration, '-an',
            '-vf', "crop=$($bounds.VideoWidth):$($bounds.VideoHeight):0:0",
            '-c:v', 'libx264', '-preset', 'veryfast', '-crf', '18', '-pix_fmt', 'yuv420p',
            '-r', '30', '-movflags', '+faststart', $Output)
        if ($WriteProgress) {
            $progressPath = $Output + '.progress'
            [IO.File]::WriteAllText($progressPath, '')
            $arguments = @('-progress', $progressPath) + $arguments
            $report.ProgressPath = $progressPath
        }
        $startBounds = Get-ClientCaptureBounds $handle
        if ($startBounds.X -ne $bounds.X -or $startBounds.Y -ne $bounds.Y -or $startBounds.Width -ne $bounds.Width -or $startBounds.Height -ne $bounds.Height) {
            throw 'Game client moved during preparation. Retry to recalculate capture bounds.'
        }
        if (-not $startBounds.IsForeground -and -not $AllowBackground) { throw 'Game lost foreground before capture started.' }
        Write-Host "Recording game client $($bounds.Width)x$($bounds.Height) at ($($bounds.X),$($bounds.Y)) for $Duration seconds. Keep the game visible and stationary."
        Invoke-CaptureFFmpeg -Arguments $arguments -LogPath $logPath
        $report.LogPath = $logPath
        try {
            $after = Get-ClientCaptureBounds $handle
            if ($after.X -ne $bounds.X -or $after.Y -ne $bounds.Y -or $after.Width -ne $bounds.Width -or $after.Height -ne $bounds.Height) {
                Write-Warning 'Game client moved or resized during capture. Review the recording.'
            }
            if (-not $after.IsForeground) { Write-Warning 'Game lost foreground during capture. Review for covering windows.' }
        } catch { Write-Warning "Post-capture bounds check: $_" }
    }
    $file = Get-Item -LiteralPath $Output
    if ($file.Length -eq 0) { throw 'Capture produced an empty file.' }
    $report.Output = $Output
    $report.Bytes = $file.Length
    $report | ConvertTo-Json -Depth 5
} finally {
    [void][GameVideoCapture.Native]::SetThreadDpiAwarenessContext($oldDpiContext)
}
