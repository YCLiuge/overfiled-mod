<#
.SYNOPSIS
Record an already-prepared, paused, clean cinematic orbit through the loopback API.
.EXAMPLE
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\Film-Orbit.ps1 -CheckOnly
.EXAMPLE
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\Film-Orbit.ps1 -Output .\artifacts\orbit-film.mp4
.NOTES
Prepare the shot separately with mj and mj shot. This script never changes tracks,
seeks, prepares a shot, or closes the editor. It focuses the game, starts client-only
capture, waits for FFmpeg to report a captured frame, then sends mj play. It always
attempts mj pause after a validated filming attempt, including failure. The editor
and clean-frame mode remain open for the caller. -CheckOnly is read-only.
#>
[CmdletBinding()]
param(
    [string]$BaseUri = 'http://127.0.0.1:8080/api',
    [string]$Output,
    [string]$ProcessName = 'launcher',
    [int]$TargetProcessId = 0,
    [string]$FfmpegPath,
    [ValidateRange(2,10)][int]$PaddingSeconds = 2,
    [ValidateRange(2,10)][int]$CaptureStartTimeoutSeconds = 5,
    [switch]$Overwrite,
    [switch]$CheckOnly
)
$ErrorActionPreference = 'Stop'
$apiUri = $null
if (-not [Uri]::TryCreate($BaseUri, [UriKind]::Absolute, [ref]$apiUri) -or
    $apiUri.Scheme -ne 'http' -or -not $apiUri.IsLoopback -or
    $apiUri.UserInfo -or $apiUri.Query -or $apiUri.Fragment) {
    throw 'BaseUri must be an HTTP loopback API URL without credentials, query, or fragment.'
}
$BaseUri = $apiUri.AbsoluteUri.TrimEnd('/')
function Read-OrbitState {
    $reply = Invoke-RestMethod -Uri ($BaseUri + '/state') -Method Get -TimeoutSec 8
    if ($reply.data.status -ne 'completed' -or $null -eq $reply.data.result.orbit) {
        throw 'Game state did not complete or does not include orbit state.'
    }
    return $reply.data.result
}
function Assert-PreparedShot($State) {
    $orbit = $State.orbit
    $shot = $orbit.cinematic
    if ($orbit.active -ne $true -or $null -eq $shot -or $shot.prepared -ne $true -or
        $shot.playing -ne $false -or $shot.cleanFrame -ne $true) {
        throw 'Requires an active orbit editor with a prepared, paused cinematic shot and cleanFrame=true. Prepare the shot before filming.'
    }
    $duration = [double]$shot.duration
    $elapsed = [double]$shot.elapsed
    if ([double]::IsNaN($duration) -or [double]::IsInfinity($duration) -or
        [double]::IsNaN($elapsed) -or [double]::IsInfinity($elapsed) -or
        $duration -le 0 -or $elapsed -lt 0 -or $elapsed -ge $duration) {
        throw 'Prepared cinematic duration/elapsed is invalid or the shot is already complete.'
    }
}
function Send-OrbitCommand([string]$Command) {
    $body = @{ id = ('film-' + [guid]::NewGuid().ToString('N')); command = $Command; target = 'logic'; waitMs = 2000 } | ConvertTo-Json -Compress
    $reply = Invoke-RestMethod -Uri ($BaseUri + '/command') -Method Post -ContentType 'application/json; charset=utf-8' -Body ([Text.Encoding]::UTF8.GetBytes($body)) -TimeoutSec 8
    if ($reply.data.status -ne 'completed') {
        throw "Command $Command returned status $($reply.data.status), id $($reply.data.id): $($reply.data.error). It was not retried."
    }
    return $reply.data
}
function Read-SharedText([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return '' }
    $stream = $null
    $reader = $null
    try {
        $stream = [IO.File]::Open($Path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite)
        $reader = New-Object IO.StreamReader($stream)
        return $reader.ReadToEnd()
    } catch [IO.IOException] { return '' }
    finally {
        if ($reader) { $reader.Dispose() } elseif ($stream) { $stream.Dispose() }
    }
}
function Quote-PowerShellLiteral([string]$Text) { return "'" + $Text.Replace("'", "''") + "'" }
$initialState = Read-OrbitState
Assert-PreparedShot $initialState
$remaining = [double]$initialState.orbit.cinematic.duration - [double]$initialState.orbit.cinematic.elapsed
$captureSeconds = [int][Math]::Ceiling($remaining) + $PaddingSeconds
if ($captureSeconds -gt 300) { throw 'Remaining shot plus padding exceeds the recorder limit of 300 seconds.' }
if ($CheckOnly) {
    [ordered]@{ Ready = $true; ModuleVersion = $initialState.moduleVersion; CaptureSeconds = $captureSeconds; RemainingShotSeconds = $remaining; Orbit = $initialState.orbit } | ConvertTo-Json -Depth 12
    return
}
$recordScript = Join-Path $PSScriptRoot 'Record-GameVideo.ps1'
if (-not (Test-Path -LiteralPath $recordScript -PathType Leaf)) { throw "Recorder is missing: $recordScript" }
if (-not $Output) { $Output = Join-Path $PSScriptRoot ("..\artifacts\orbit-film-{0}.mp4" -f (Get-Date -Format 'yyyyMMdd-HHmmss')) }
$Output = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Output)
if ([IO.Path]::GetExtension($Output) -ne '.mp4') { throw 'Output must have an .mp4 extension.' }
if ((Test-Path -LiteralPath $Output) -and -not $Overwrite) { throw "Output exists. Use -Overwrite or another path: $Output" }
[void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($Output))
$progressPath = $Output + '.progress'
$stdoutPath = $Output + '.recorder.stdout.log'
$stderrPath = $Output + '.recorder.stderr.log'
$manifestPath = $Output + '.film.json'
# A fresh progress file prevents an earlier successful capture from triggering playback.
[IO.File]::WriteAllText($progressPath, '')
$childParameters = @{
    Mode = 'Record'; BringToFront = $true; WriteProgress = $true
    Duration = $captureSeconds; Output = $Output; ProcessName = $ProcessName
}
if ($TargetProcessId -gt 0) { $childParameters.TargetProcessId = $TargetProcessId }
if ($FfmpegPath) { $childParameters.FfmpegPath = $FfmpegPath }
if ($Overwrite) { $childParameters.Overwrite = $true }
$serializedParameters = $childParameters | ConvertTo-Json -Compress
$childCode = @'
$ErrorActionPreference = 'Stop'
try {
    $values = ConvertFrom-Json __PARAMETERS__
    $parameters = @{}
    foreach ($property in $values.PSObject.Properties) { $parameters[$property.Name] = $property.Value }
    & __SCRIPT__ @parameters
    exit 0
} catch {
    Write-Error $_ -ErrorAction Continue
    exit 1
}
'@
$childCode = $childCode.Replace('__PARAMETERS__', (Quote-PowerShellLiteral $serializedParameters)).Replace('__SCRIPT__', (Quote-PowerShellLiteral $recordScript))
$encodedChild = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($childCode))
$child = $null
$failure = $null
$attemptStarted = $false
$report = [ordered]@{
    Status = 'starting'; Output = $Output; ManifestPath = $manifestPath
    DurationSeconds = $captureSeconds; RemainingShotSeconds = $remaining
    ProgressPath = $progressPath; RecorderStdout = $stdoutPath; RecorderStderr = $stderrPath
    PreparedShot = $initialState.orbit.cinematic
}
try {
    $child = Start-Process -FilePath (Join-Path $PSHOME 'powershell.exe') -ArgumentList @('-NoProfile','-ExecutionPolicy','Bypass','-EncodedCommand',$encodedChild) -WindowStyle Hidden -RedirectStandardOutput $stdoutPath -RedirectStandardError $stderrPath -PassThru
    # Keep the native handle open so Windows PowerShell can read ExitCode after exit.
    $null = $child.Handle
    $attemptStarted = $true
    $report.RecorderProcessId = $child.Id
    $timer = [Diagnostics.Stopwatch]::StartNew()
    $firstFrame = $false
    while ($timer.Elapsed.TotalSeconds -lt $CaptureStartTimeoutSeconds) {
        $child.Refresh()
        if ($child.HasExited) { throw "Recorder exited before capture started (exit $($child.ExitCode)). Inspect $stderrPath and $stdoutPath." }
        $progress = Read-SharedText $progressPath
        if ([regex]::IsMatch($progress, '(?m)^frame=\s*[1-9][0-9]*\s*$')) { $firstFrame = $true; break }
        Start-Sleep -Milliseconds 100
    }
    if (-not $firstFrame) { throw "FFmpeg did not report a captured frame within $CaptureStartTimeoutSeconds seconds." }
    $report.FirstFrameWaitSeconds = [Math]::Round($timer.Elapsed.TotalSeconds, 3)
    $readyState = Read-OrbitState
    Assert-PreparedShot $readyState
    if ([Math]::Abs([double]$readyState.orbit.cinematic.duration - [double]$initialState.orbit.cinematic.duration) -gt 0.01 -or
        [Math]::Abs([double]$readyState.orbit.cinematic.elapsed - [double]$initialState.orbit.cinematic.elapsed) -gt 0.05) {
        throw 'Prepared shot changed while capture was starting. Playback was not started.'
    }
    $report.PlayCommand = Send-OrbitCommand 'mj play'
    $report.PlaybackRequestedAt = [DateTimeOffset]::Now.ToString('o')
    $report.Status = 'recording'
    while (-not $child.HasExited) {
        if ($timer.Elapsed.TotalSeconds -gt ($captureSeconds + 20)) { throw 'Recorder exceeded the expected capture deadline.' }
        Start-Sleep -Milliseconds 100
        $child.Refresh()
    }
    $child.WaitForExit()
    if ($child.ExitCode -ne 0) { throw "Recorder failed with exit $($child.ExitCode). Inspect $stderrPath and $stdoutPath." }
    $file = Get-Item -LiteralPath $Output
    if ($file.Length -eq 0) { throw 'Recorder produced an empty output.' }
    $report.Bytes = $file.Length
    $report.Status = 'completed'
} catch {
    $failure = $_
    $report.Status = 'failed'
    $report.Error = $_.Exception.Message
} finally {
    if ($attemptStarted) {
        try { $report.PauseCommand = Send-OrbitCommand 'mj pause' }
        catch {
            $report.PauseError = $_.Exception.Message
            $report.Status = 'failed'
            if ($null -eq $failure) { $failure = $_; $report.Error = "Final mj pause failed: $($_.Exception.Message)" }
            Write-Warning "Final mj pause failed: $_"
        }
    }
    if ($child) {
        $child.Refresh()
        if (-not $child.HasExited) {
            # Kill only the recorder process we created and its FFmpeg descendants.
            $report.TerminatedRecorderProcessId = $child.Id
            $savedPreference = $ErrorActionPreference
            try {
                $ErrorActionPreference = 'Continue'
                & "$env:SystemRoot\System32\taskkill.exe" /PID $child.Id /T /F 2>&1 | Out-Null
            } finally { $ErrorActionPreference = $savedPreference }
            $child.WaitForExit(5000) | Out-Null
            $child.Refresh()
            if (-not $child.HasExited) {
                $report.CleanupError = "Recorder process $($child.Id) did not exit after tree termination."
                Write-Warning $report.CleanupError
            }
        }
        $child.Dispose()
    }
    try { $report.FinalOrbit = (Read-OrbitState).orbit }
    catch { $report.FinalStateError = $_.Exception.Message }
    $json = $report | ConvertTo-Json -Depth 16
    [IO.File]::WriteAllText($manifestPath, $json, (New-Object Text.UTF8Encoding($false)))
}
$json
if ($failure) { throw $failure }
