param([string]$BaseUri = 'http://127.0.0.1:8080/api')
$ErrorActionPreference = 'Stop'
$checks = [System.Collections.Generic.List[string]]::new()
function Assert-Check([bool]$Condition, [string]$Name) {
    if (-not $Condition) { throw "FAILED: $Name" }
    $checks.Add($Name)
}
function Invoke-OrbitCommand([string]$Command, [switch]$ExpectFailure) {
    $body = @{ id = ('orbit-test-' + [guid]::NewGuid().ToString('N')); command = $Command; target = 'logic'; waitMs = 1500 } | ConvertTo-Json -Compress
    try {
        $reply = Invoke-RestMethod -Uri "$BaseUri/command" -Method Post -ContentType 'application/json; charset=utf-8' -Body ([Text.Encoding]::UTF8.GetBytes($body)) -TimeoutSec 8
    } catch {
        if (-not $_.ErrorDetails.Message) { throw }
        $reply = $_.ErrorDetails.Message | ConvertFrom-Json
    }
    if ($reply.data.status -eq 'queued' -or $reply.data.status -eq 'running') {
        throw "Command still pending: $Command ($($reply.data.id)); inspect receipt before retrying."
    }
    if ($ExpectFailure) {
        if ($reply.data.status -ne 'failed') { throw "Expected command failure: $Command" }
    } elseif ($reply.data.status -ne 'completed') {
        throw "Command failed: $Command -- $($reply.data.error)"
    }
    return $reply
}
function Read-GameState {
    $reply = Invoke-RestMethod -Uri "$BaseUri/state" -TimeoutSec 8
    if ($reply.data.status -ne 'completed') { throw 'State read did not complete' }
    return $reply.data.result
}
function Assert-Released($State) {
    $o = $State.orbit
    Assert-Check (-not $o.active -and -not $o.cameraPlaying -and -not $o.personPlaying) 'Editor and playback stopped'
    Assert-Check ($o.visualRoots -eq 0 -and $o.pointObjects -eq 0 -and $o.curveObjects -eq 0 -and $o.materialCount -eq 0 -and $o.pointRows -eq 0) 'All owned visual pools released'
}
$baseline = Read-GameState
$moduleVersion = $null
if (-not [version]::TryParse([string]$baseline.moduleVersion, [ref]$moduleVersion) -or $moduleVersion -lt [version]'1.0.3' -or $null -eq $baseline.orbit) {
    throw 'Expected Logic 1.0.3 or newer with orbit state'
}
$hasCinematic = $moduleVersion -ge [version]'1.0.4'
if ($baseline.orbit.active -or $baseline.orbit.trackCount -ne 0 -or $baseline.orbit.personPointCount -ne 0) {
    throw 'Refusing test: editor or existing in-memory tracks are present; use a freshly reloaded module.'
}
$ownsTest = $true
try {
    $null = Invoke-OrbitCommand 'mj play' -ExpectFailure
    Assert-Check (-not (Read-GameState).orbit.active) 'Playing a closed editor fails without opening it'
    $null = Invoke-OrbitCommand 'mj'
    $opened = (Read-GameState).orbit
    Assert-Check ($opened.active -and $opened.trackCount -eq 1 -and $opened.cameraPointCount -eq 0) 'Bare mj opens default editor'
    $null = Invoke-OrbitCommand 'mj start'
    $again = (Read-GameState).orbit
    Assert-Check ($again.materialCount -eq $opened.materialCount -and $again.curveObjects -eq $opened.curveObjects -and $again.trackCount -eq 1) 'Repeated start is idempotent'
    $null = Invoke-OrbitCommand 'mj play' -ExpectFailure
    Assert-Check (-not (Read-GameState).orbit.cameraPlaying) 'Empty camera playlist fails without getting stuck playing'
    $null = Invoke-OrbitCommand 'mj invalid' -ExpectFailure
    Assert-Check ((Read-GameState).orbit.active) 'Invalid subcommand preserves editor state'
    if ($hasCinematic) {
        $beforeShot = (Read-GameState).orbit
        Assert-Check ($null -ne $beforeShot.cinematic) 'Cinematic state is available in Logic 1.0.4 or newer'
        $baselineFov = [double]$beforeShot.cinematic.fieldOfView
        Assert-Check ((-not [double]::IsNaN($baselineFov) -and -not [double]::IsInfinity($baselineFov)) -and $baselineFov -gt 0) 'Baseline camera FOV is finite and positive'
        $null = Invoke-OrbitCommand 'mj shot 2 2 1.8 30'
        $prepared = (Read-GameState).orbit
        $shot = $prepared.cinematic
        Assert-Check ($prepared.active -and $shot.prepared -and -not $shot.playing -and -not $prepared.cameraPlaying -and -not $prepared.personPlaying -and $shot.cleanFrame -and $shot.duration -eq 2 -and $shot.elapsed -eq 0 -and $shot.progress -eq 0) 'Shot prepares paused at the start with a clean frame'
        Assert-Check ($prepared.trackCount -eq $beforeShot.trackCount -and $prepared.cameraPointCount -eq $beforeShot.cameraPointCount -and $prepared.personPointCount -eq $beforeShot.personPointCount -and $prepared.activeTrackIndex -eq $beforeShot.activeTrackIndex) 'Preparing an isolated shot preserves track and point counts'
        $null = Invoke-OrbitCommand 'mj seek 0.5'
        $seeked = (Read-GameState).orbit.cinematic
        Assert-Check ($seeked.prepared -and -not $seeked.playing -and [Math]::Abs([double]$seeked.progress - 0.5) -lt 0.0001 -and [Math]::Abs([double]$seeked.elapsed - 1) -lt 0.0001) 'Seek positions a paused shot at half duration'
        $null = Invoke-OrbitCommand 'mj fov 50'
        $configured = (Read-GameState).orbit.cinematic
        Assert-Check ([Math]::Abs([double]$configured.fieldOfView - 50) -lt 0.0001) 'Cinematic FOV can be set to 50 degrees'
        foreach ($invalid in @('mj seek -0.01', 'mj seek 1.01', 'mj seek NaN', 'mj fov 19', 'mj fov 101', 'mj fov Infinity', 'mj shot NaN 2 1.8 30', 'mj shot 121 2 1.8 30', 'mj shot 2 51 1.8 30', 'mj shot 2 2 21 30', 'mj shot 2 2 1.8 361')) {
            $null = Invoke-OrbitCommand $invalid -ExpectFailure
            $rejected = (Read-GameState).orbit
            $unchanged = $rejected.cinematic
            Assert-Check ($unchanged.prepared -and -not $unchanged.playing -and $unchanged.cleanFrame -and $unchanged.duration -eq $configured.duration -and $unchanged.elapsed -eq $configured.elapsed -and $unchanged.progress -eq $configured.progress -and $unchanged.fieldOfView -eq $configured.fieldOfView -and $unchanged.cameraPosition.x -eq $configured.cameraPosition.x -and $unchanged.cameraPosition.y -eq $configured.cameraPosition.y -and $unchanged.cameraPosition.z -eq $configured.cameraPosition.z -and $rejected.trackCount -eq $beforeShot.trackCount -and $rejected.cameraPointCount -eq $beforeShot.cameraPointCount -and $rejected.personPointCount -eq $beforeShot.personPointCount) "Invalid cinematic command preserves the prepared shot: $invalid"
        }
        $null = Invoke-OrbitCommand 'mj clean off'
        $cleanOff = (Read-GameState).orbit.cinematic
        Assert-Check ($cleanOff.prepared -and -not $cleanOff.cleanFrame -and $cleanOff.hiddenCanvases -eq 0) 'Clean off restores HUD references without clearing the shot'
        $null = Invoke-OrbitCommand 'mj clean off'
        $cleanOffAgain = (Read-GameState).orbit.cinematic
        Assert-Check ($cleanOffAgain.prepared -and -not $cleanOffAgain.cleanFrame -and $cleanOffAgain.hiddenCanvases -eq 0) 'Repeated clean off is idempotent'
        $null = Invoke-OrbitCommand 'mj clean on'
        $cleanOn = (Read-GameState).orbit.cinematic
        Assert-Check ($cleanOn.prepared -and $cleanOn.cleanFrame) 'Clean on hides the HUD while retaining the shot'
        $null = Invoke-OrbitCommand 'mj clean on'
        $cleanOnAgain = (Read-GameState).orbit.cinematic
        Assert-Check ($cleanOnAgain.prepared -and $cleanOnAgain.cleanFrame -and $cleanOnAgain.hiddenCanvases -eq $cleanOn.hiddenCanvases) 'Repeated clean on does not accumulate HUD references'
        $null = Invoke-OrbitCommand 'mj seek 1'
        $endpoint = (Read-GameState).orbit.cinematic
        Assert-Check ($endpoint.prepared -and -not $endpoint.playing -and $endpoint.progress -eq 1 -and $endpoint.elapsed -eq $endpoint.duration) 'Seek to the endpoint leaves the shot paused'
        $null = Invoke-OrbitCommand 'mj play'
        $shotPlaying = (Read-GameState).orbit
        Assert-Check ($shotPlaying.cinematic.prepared -and $shotPlaying.cinematic.playing -and $shotPlaying.cameraPlaying -and $shotPlaying.cinematic.progress -ge 0 -and $shotPlaying.cinematic.progress -lt 1) 'Play restarts a prepared shot from its endpoint'
        $null = Invoke-OrbitCommand 'mj pause'
        $pausedFirst = (Read-GameState).orbit
        $pausedSecond = (Read-GameState).orbit
        Assert-Check ($pausedFirst.cinematic.prepared -and -not $pausedFirst.cinematic.playing -and -not $pausedFirst.cameraPlaying -and -not $pausedSecond.cinematic.playing -and -not $pausedSecond.cameraPlaying -and $pausedFirst.cinematic.elapsed -eq $pausedSecond.cinematic.elapsed -and $pausedFirst.cinematic.progress -eq $pausedSecond.cinematic.progress -and $pausedFirst.cinematic.cameraPosition.x -eq $pausedSecond.cinematic.cameraPosition.x -and $pausedFirst.cinematic.cameraPosition.y -eq $pausedSecond.cinematic.cameraPosition.y -and $pausedFirst.cinematic.cameraPosition.z -eq $pausedSecond.cinematic.cameraPosition.z) 'Pause freezes shot time and camera pose across state reads'
        $null = Invoke-OrbitCommand 'mj shot clear'
        $clearedShot = (Read-GameState).orbit
        Assert-Check (-not $clearedShot.cinematic.prepared -and -not $clearedShot.cinematic.playing -and -not $clearedShot.cinematic.cleanFrame -and $clearedShot.cinematic.hiddenCanvases -eq 0 -and [Math]::Abs([double]$clearedShot.cinematic.fieldOfView - $baselineFov) -lt 0.0001) 'Shot clear releases the clean frame and restores the previous FOV'
        Assert-Check ($clearedShot.active -and $clearedShot.trackCount -eq $beforeShot.trackCount -and $clearedShot.cameraPointCount -eq $beforeShot.cameraPointCount -and $clearedShot.personPointCount -eq $beforeShot.personPointCount -and $clearedShot.activeTrackIndex -eq $beforeShot.activeTrackIndex) 'Shot clear preserves the editor and existing track model'
    }
    $null = Invoke-OrbitCommand 'call CameraOrbitSystem.AddPoint false'
    $null = Invoke-OrbitCommand 'call CameraOrbitSystem.AddPoint false'
    $two = (Read-GameState).orbit
    Assert-Check ($two.cameraPointCount -eq 2 -and $two.pointRows -eq 2 -and $two.pointObjects -eq 2 -and $two.visualRoots -eq 1) 'Camera markers and editable rows created'
    $null = Invoke-OrbitCommand 'call CameraOrbitSystem.RefreshPointListUI'
    $null = Invoke-OrbitCommand 'call CameraOrbitSystem.RefreshTrackVisuals'
    $refreshed = (Read-GameState).orbit
    Assert-Check ($refreshed.pointRows -eq $two.pointRows -and $refreshed.pointObjects -eq $two.pointObjects -and $refreshed.materialCount -eq $two.materialCount -and $refreshed.curveObjects -eq $two.curveObjects) 'Refresh reuses rows markers materials and curve widgets'
    $null = Invoke-OrbitCommand 'call CameraOrbitSystem.AddPoint false'
    $null = Invoke-OrbitCommand 'call CameraOrbitSystem.RemovePoint false 2'
    $afterRemove = (Read-GameState).orbit
    Assert-Check ($afterRemove.cameraPointCount -eq 2 -and $afterRemove.pointRows -eq 3 -and $afterRemove.pointObjects -eq 3) 'Removing a point keeps surplus pool objects reusable'
    $null = Invoke-OrbitCommand 'call CameraOrbitSystem.AddPoint false'
    $afterReuse = (Read-GameState).orbit
    Assert-Check ($afterReuse.cameraPointCount -eq 3 -and $afterReuse.pointRows -eq 3 -and $afterReuse.pointObjects -eq 3 -and $afterReuse.materialCount -eq $afterRemove.materialCount) 'Appending reuses pooled surplus rows and markers'
    $null = Invoke-OrbitCommand 'call CameraOrbitSystem.RemovePoint false 2'
    $null = Invoke-OrbitCommand 'call CameraOrbitSystem.CreateTrack verification_empty'
    $null = Invoke-OrbitCommand 'mj play'
    $playing = (Read-GameState).orbit
    Assert-Check ($playing.cameraPlaying -and $playing.activeTrackIndex -eq 0 -and $playing.cameraPointCount -eq 2) 'Playback skips an empty active track'
    $null = Invoke-OrbitCommand 'call CameraOrbitSystem.AdvancePlayback 6'
    $wrapped = (Read-GameState).orbit
    Assert-Check ($wrapped.cameraPlaying -and $wrapped.activeTrackIndex -eq 0 -and $wrapped.cameraProgress -gt 0 -and $wrapped.cameraProgress -lt 0.8) 'Playback wraps and carries remaining frame time past empty track'
    $null = Invoke-OrbitCommand 'mj pause'
    Assert-Check (-not (Read-GameState).orbit.cameraPlaying) 'Pause stops camera playback'
    $null = Invoke-OrbitCommand 'call CameraOrbitSystem.AddPoint true'
    $null = Invoke-OrbitCommand 'call CameraOrbitSystem.AddPoint true'
    $null = Invoke-OrbitCommand 'call CameraOrbitSystem.TogglePersonPlayback'
    Assert-Check ((Read-GameState).orbit.personPlaying) 'Person playback starts with two points'
    $null = Invoke-OrbitCommand 'call CameraOrbitSystem.AdvancePlayback 6'
    $personFinished = (Read-GameState).orbit
    Assert-Check (-not $personFinished.personPlaying -and $personFinished.personProgress -eq 1) 'Person playback reaches endpoint and stops automatically'
    $null = Invoke-OrbitCommand 'call CameraOrbitSystem.TogglePersonPlayback'
    $personRestarted = (Read-GameState).orbit
    Assert-Check ($personRestarted.personPlaying -and $personRestarted.personProgress -lt 1) 'Completed person track can restart from beginning'
    $null = Invoke-OrbitCommand 'call CameraOrbitSystem.TogglePersonPlayback'
    Assert-Check (-not (Read-GameState).orbit.personPlaying) 'Person playback can pause'
    $null = Invoke-OrbitCommand 'mj stop'
    $stopped = Read-GameState
    Assert-Released $stopped
    Assert-Check ($stopped.orbit.trackCount -eq 2) 'Normal close retains unsaved in-memory tracks'
    $null = Invoke-OrbitCommand 'mj start'
    Assert-Check ((Read-GameState).orbit.cameraPointCount -eq 2) 'Reopen restores retained camera points'
    $null = Invoke-OrbitCommand 'mj stop'
    Assert-Released (Read-GameState)
    $null = Invoke-OrbitCommand 'mj stop'
    Assert-Released (Read-GameState)
    $null = Invoke-OrbitCommand 'call CameraOrbitSystem.Shutdown'
    $final = Read-GameState
    Assert-Released $final
    Assert-Check ($final.orbit.trackCount -eq 0 -and $final.playerReady) 'Shutdown removes test tracks and game player remains ready'
    $dx = [double]$final.position.x - [double]$baseline.position.x
    $dy = [double]$final.position.y - [double]$baseline.position.y
    $dz = [double]$final.position.z - [double]$baseline.position.z
    Assert-Check (($dx*$dx + $dy*$dy + $dz*$dz) -lt 0.01) 'Verification left player position unchanged'
    [pscustomobject]@{ passed = $checks.Count; checks = $checks.ToArray(); moduleVersion = $final.moduleVersion; scene = $final.scene; orbit = $final.orbit } | ConvertTo-Json -Depth 10
} finally {
    if ($ownsTest) {
        try { $null = Invoke-OrbitCommand 'call CameraOrbitSystem.Shutdown' }
        catch { Write-Warning "Cleanup failed; use mj stop before continuing: $($_.Exception.Message)" }
    }
}
