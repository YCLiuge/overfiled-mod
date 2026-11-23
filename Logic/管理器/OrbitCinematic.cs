using System;
using System.Collections.Generic;
using System.Globalization;
using Il2Cpp;
using MelonLoader;
using UnityEngine;

public static partial class CameraOrbitSystem
{
    private static OrbitShotPlan _shotPlan;
    private static Transform _shotCameraParent;
    private static Vector3 _shotCameraLocalPosition, _shotCameraLocalScale;
    private static Quaternion _shotCameraLocalRotation;
    private static float _shotElapsed, _shotPreviousFov, _originalCameraFov;
    private static bool _shotPlaying, _shotPreviousLookAt, _shotPreviousController, _shotPreviousCameraControl;
    private static bool _cleanFrame, _cleanPreviousTracks, _cleanPreviousEditor, _cleanPreviousCursor;
    private static CursorLockMode _cleanPreviousCursorLock;
    private static readonly List<Canvas> _hiddenCanvases = new();
    private const string CinematicUsage = "mj [start|stop|play|pause|status] / mj shot [秒数 半径 高度 环绕角度] / mj shot clear / mj clean on|off / mj seek 0..1 / mj fov 20..100";

    private static float CommandNumber(string text)
    {
        if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float number) || !IsFinite(number))
            throw new ArgumentException("镜头参数必须是有限数字（小数点使用 .）");
        return number;
    }

    private static OrbitShotPlan ParseShot(string[] args, float angle = 0, Vector3 center = default)
    {
        if (args.Length < 2 || args.Length > 6) throw new ArgumentException(CinematicUsage);
        return new OrbitShotPlan(args.Length > 2 ? CommandNumber(args[2]) : 16f,
            args.Length > 3 ? CommandNumber(args[3]) : 4f,
            args.Length > 4 ? CommandNumber(args[4]) : 2f,
            args.Length > 5 ? CommandNumber(args[5]) : 100f,
            angle, center.x, center.y, center.z);
    }

    // This validation also runs on the HTTP listener thread: no Unity object access here.
    private static bool ValidateCinematicCommand(string[] args)
    {
        if (args.Length < 2) return false;
        switch (args[1])
        {
            case "shot":
                if (args.Length == 3 && args[2] == "clear") return true;
                ParseShot(args);
                return true;
            case "clean":
                if (args.Length != 3 || (args[2] != "on" && args[2] != "off")) throw new ArgumentException(CinematicUsage);
                return true;
            case "seek":
            case "fov":
                if (args.Length != 3) throw new ArgumentException(CinematicUsage);
                float number = CommandNumber(args[2]);
                float min = args[1] == "seek" ? 0 : 20;
                float max = args[1] == "seek" ? 1 : 100;
                if (number < min || number > max) throw new ArgumentException($"{args[1]} 参数范围为 {min} 到 {max}");
                return true;
            default: return false;
        }
    }

    private static bool ExecuteCinematicCommand(string[] args)
    {
        if (args.Length < 2) return false;
        switch (args[1])
        {
            case "shot":
                if (args.Length == 3 && args[2] == "clear") { ClearPreparedShot(); SetCleanFrame(false); return true; }
                RequireEditor();
                var center = mod.TipMsgCore.i.mainplayer.transform.position;
                Vector3 relative = _cam.transform.position - center;
                float angle = Mathf.Atan2(relative.x, relative.z);
                var plan = ParseShot(args, angle, center); // Validate before changing any running state.
                if (_isRecording) throw new InvalidOperationException("请先停止人物轨道录制");
                SetCameraPlayback(false);
                IsPersonPlaying = false;
                _orgPlayerPos = center;
                _orgPlayerRot = mod.TipMsgCore.i.mainplayer.transform.rotation;
                ResetDragState();
                if (_shotPlan == null)
                {
                    _shotPreviousFov = _cam.fieldOfView;
                    _shotPreviousCameraControl = IsCameraControlEnabled;
                    _shotPreviousLookAt = IsLookAtPlayer;
                    _shotPreviousController = CameraSystem.Instance?._MainCameraController != null && CameraSystem.Instance._MainCameraController.enabled;
                    _shotCameraParent = _cam.transform.parent;
                    _shotCameraLocalPosition = _cam.transform.localPosition;
                    _shotCameraLocalRotation = _cam.transform.localRotation;
                    _shotCameraLocalScale = _cam.transform.localScale;
                    // CameraSystem continues moving its rig even with its input controller disabled.
                    _cam.transform.SetParent(null, true);
                }
                if (!IsCameraControlEnabled) SetCameraControl(true);
                _shotPlan = plan;
                _shotElapsed = 0;
                _shotPlaying = false;
                IsLookAtPlayer = true;
                if (CameraSystem.Instance?._MainCameraController != null) CameraSystem.Instance._MainCameraController.enabled = false;
                _cam.fieldOfView = 45f;
                SetCleanFrame(true);
                ApplyShotPose();
                MelonLogger.Msg($"[Orbit] 环绕镜头就绪：{plan.Duration}s, r={plan.Radius}, h={plan.Height}, sweep={plan.SweepDegrees}。mj play 开拍，F9 播放/暂停，F10 退出。");
                return true;
            case "clean": RequireEditor(); SetCleanFrame(args[2] == "on"); return true;
            case "seek":
                RequireEditor();
                if (_shotPlan == null) throw new InvalidOperationException("请先使用 mj shot 准备拍摄镜头");
                _shotElapsed = CommandNumber(args[2]) * _shotPlan.Duration;
                if (_shotElapsed >= _shotPlan.Duration) _shotPlaying = false;
                ApplyShotPose();
                return true;
            case "fov": RequireEditor(); _cam.fieldOfView = CommandNumber(args[2]); return true;
            default: return false;
        }
    }

    private static void RequireEditor()
    {
        if (!IsActive || _cam == null || _cameraDummy == null || mod.TipMsgCore.i?.mainplayer == null)
            throw new InvalidOperationException("请先用 mj 开启轨道编辑器");
    }

    private static object ReadCinematicState()
    {
        var position = _cam != null ? _cam.transform.position : Vector3.zero;
        return new
        {
            prepared = _shotPlan != null, playing = _shotPlaying,
            duration = _shotPlan?.Duration ?? 0f, elapsed = _shotElapsed,
            progress = _shotPlan != null ? _shotElapsed / _shotPlan.Duration : 0f,
            radius = _shotPlan?.Radius ?? 0f, height = _shotPlan?.Height ?? 0f,
            sweepDegrees = _shotPlan?.SweepDegrees ?? 0f,
            cleanFrame = _cleanFrame, hiddenCanvases = _hiddenCanvases.Count,
            fieldOfView = _cam != null ? _cam.fieldOfView : 0f,
            cameraPosition = new { x = position.x, y = position.y, z = position.z }
        };
    }

    private static void SetShotPlayback(bool playing)
    {
        if (_shotPlan == null) return;
        if (playing && _shotElapsed >= _shotPlan.Duration) _shotElapsed = 0;
        _shotPlaying = playing;
        ApplyShotPose();
    }

    private static void TickShot()
    {
        if (Input.GetKeyDown(KeyCode.F9)) SetShotPlayback(!_shotPlaying);
        if (_shotPlaying)
        {
            _shotElapsed = Mathf.Min(_shotPlan.Duration, _shotElapsed + Math.Max(0f, Time.unscaledDeltaTime));
            if (_shotElapsed >= _shotPlan.Duration) _shotPlaying = false;
        }
        ApplyShotPose();
    }

    private static void ApplyShotPose()
    {
        if (_shotPlan == null || _cam == null || _cameraDummy == null) return;
        var sample = _shotPlan.Sample(_shotElapsed);
        var position = new Vector3(sample.x, sample.y, sample.z);
        var target = new Vector3(_shotPlan.CenterX, _shotPlan.CenterY + OrbitShotPlan.LookHeight, _shotPlan.CenterZ);
        _cameraDummy.transform.position = position;
        _cam.transform.SetPositionAndRotation(position, Quaternion.LookRotation(target - position));
    }

    private static void ClearPreparedShot()
    {
        if (_shotPlan == null) return;
        _shotPlaying = false;
        _shotPlan = null;
        _shotElapsed = 0;
        IsLookAtPlayer = _shotPreviousLookAt;
        if (_cam != null)
        {
            _cam.transform.SetParent(_shotCameraParent != null ? _shotCameraParent : null, false);
            _cam.transform.localPosition = _shotCameraLocalPosition;
            _cam.transform.localRotation = _shotCameraLocalRotation;
            _cam.transform.localScale = _shotCameraLocalScale;
        }
        _shotCameraParent = null;
        if (IsActive) SetCameraControl(_shotPreviousCameraControl);
        if (_cam != null) _cam.fieldOfView = _shotPreviousFov;
        if (CameraSystem.Instance?._MainCameraController != null)
            CameraSystem.Instance._MainCameraController.enabled = _shotPreviousController;
    }

    private static void SetTrackVisibility(bool visible)
    {
        IsTrackVisible = visible;
        foreach (var track in Tracks) if (track.VisualRoot != null) track.VisualRoot.SetActive(visible);
        if (PersonTrack.VisualRoot != null) PersonTrack.VisualRoot.SetActive(visible);
        if (_dummyVisual != null)
        {
            var renderer = _dummyVisual.GetComponent<Renderer>();
            if (renderer != null) renderer.enabled = visible;
        }
    }

    private static void SetCleanFrame(bool enabled)
    {
        if (_cleanFrame == enabled) return;
        if (enabled)
        {
            _cleanPreviousTracks = IsTrackVisible;
            _cleanPreviousEditor = _canvasObj != null && _canvasObj.activeSelf;
            _cleanPreviousCursor = Cursor.visible;
            _cleanPreviousCursorLock = Cursor.lockState;
            _cleanFrame = true;
            try
            {
                foreach (var canvas in UnityEngine.Object.FindObjectsOfType<Canvas>())
                {
                    if (canvas == null || !canvas.enabled ||
                        (_canvasObj != null && canvas.transform.IsChildOf(_canvasObj.transform))) continue;
                    _hiddenCanvases.Add(canvas);
                    canvas.enabled = false;
                }
                if (_canvasObj != null) _canvasObj.SetActive(false);
                SetTrackVisibility(false);
                Cursor.visible = false;
            }
            catch { SetCleanFrame(false); throw; }
        }
        else
        {
            foreach (var canvas in _hiddenCanvases)
            {
                try { if (canvas != null) canvas.enabled = true; }
                catch (Exception ex) { MelonLogger.Warning($"[Orbit] 恢复 HUD: {ex.Message}"); }
            }
            _hiddenCanvases.Clear();
            if (_canvasObj != null) _canvasObj.SetActive(_cleanPreviousEditor);
            SetTrackVisibility(_cleanPreviousTracks);
            Cursor.lockState = _cleanPreviousCursorLock;
            Cursor.visible = _cleanPreviousCursor;
            _cleanFrame = false;
        }
    }

    private static void EndCinematicSession()
    {
        try { SetCleanFrame(false); }
        finally
        {
            ClearPreparedShot();
            if (_cam != null && _originalCameraFov > 0) _cam.fieldOfView = _originalCameraFov;
        }
    }
}
