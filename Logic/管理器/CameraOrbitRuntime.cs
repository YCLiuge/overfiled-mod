using System;
using System.Collections.Generic;
using System.Linq;
using Il2Cpp;
using MelonLoader;
using mod;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Lifecycle, playback and pooled rendering helpers for the mj editor.
public static partial class CameraOrbitSystem
{
    private static readonly Dictionary<Color, Material> _visualMaterials = new();
    private static readonly List<Text> _curveLabels = new();
    private sealed class PointRow
    {
        public GameObject Root;
        public InputField X, Y, Z, Action;
        public OrbitTrack Owner;
        public TrackPoint Point;
    }
    private static readonly List<PointRow> _cameraRows = new();
    private static readonly List<PointRow> _personRows = new();
    private static Slider _speedLimitSlider;
    private static GameObject _ownedEventSystem;
    private static OrbitTrack _draggedTrack;
    private static bool _controlCaptured;
    private static bool _originalControllerEnabled;
    private static CursorLockMode _originalCursorLock;
    private static bool _originalCursorVisible;
    private static int _sceneHandle;

    public static void ValidateCommand(string command)
    {
        var args = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (args.Length == 0 || args[0] != "mj") throw new ArgumentException(CinematicUsage);
        if (ValidateCinematicCommand(args)) return;
        if (args.Length > 2 || (args.Length == 2 && !new[] { "start", "stop", "play", "pause", "status" }.Contains(args[1])))
            throw new ArgumentException(CinematicUsage);
    }

    public static void ExecuteCommand(string command)
    {
        ValidateCommand(command);
        var args = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (ExecuteCinematicCommand(args)) return;
        string action = args.Length == 1 ? "start" : args[1];
        switch (action)
        {
            case "start": StartSystem(); break;
            case "stop": StopSystem(); break;
            case "play":
                if (!IsActive) throw new InvalidOperationException("请先用 mj 开启轨道编辑器");
                if (_shotPlan != null) SetShotPlayback(true);
                else if (!SetCameraPlayback(true)) throw new InvalidOperationException("没有可播放的相机轨道，每条轨道至少需要两个点");
                break;
            case "pause": SetShotPlayback(false); SetCameraPlayback(false); break;
            case "status":
                MelonLogger.Msg($"[Orbit] active={IsActive}, tracks={Tracks.Count}, cameraPlaying={IsGlobalPlaying}, personPlaying={IsPersonPlaying}");
                break;
        }
    }

    public static void Shutdown()
    {
        StopSystem();
        Tracks.Clear();
        PersonTrack = new OrbitTrack { Name = "人物轨道" };
        _currentTrackIndex = 0;
        _lastPersonPointIndex = -1;
    }

    public static object ReadState() => new
    {
        active = IsActive,
        cameraControl = IsActive && IsCameraControlEnabled,
        cameraReady = _cam != null,
        cameraPlaying = IsGlobalPlaying || _shotPlaying,
        personPlaying = IsPersonPlaying,
        recording = _isRecording,
        cinematic = ReadCinematicState(),
        activeTrackIndex = _currentTrackIndex,
        trackCount = Tracks.Count,
        activeTrackName = ActiveTrack?.Name,
        cameraPointCount = ActiveTrack?.Points.Count ?? 0,
        cameraProgress = ActiveTrack?.Progress ?? 0f,
        pausedBySpeedCurve = IsGlobalPlaying && ActiveTrack != null && ActiveTrack.GetSpeedMultiplier(ActiveTrack.Progress) <= 0f,
        personPointCount = PersonTrack.Points.Count,
        personProgress = PersonTrack.Progress,
        visualRoots = Tracks.Count(t => t.VisualRoot != null) + (PersonTrack.VisualRoot != null ? 1 : 0),
        pointObjects = Tracks.Sum(t => t.Spheres.Count) + PersonTrack.Spheres.Count,
        curveObjects = _curvePoints.Count + _curveLabels.Count + _curveLines.Count + _bgLines.Count,
        materialCount = _visualMaterials.Count,
        pointRows = _cameraRows.Count + _personRows.Count
    };

    private static void CaptureGameControl()
    {
        _originalControllerEnabled = CameraSystem.Instance?._MainCameraController?.enabled ?? true;
        _originalCursorLock = Cursor.lockState;
        _originalCursorVisible = Cursor.visible;
        _sceneHandle = SceneManager.GetActiveScene().handle;
        _controlCaptured = true;
    }

    private static void RestoreGameControl()
    {
        if (!_controlCaptured) return;
        _controlCaptured = false;
        try
        {
            if (CameraSystem.Instance?._MainCameraController != null)
                CameraSystem.Instance._MainCameraController.enabled = _originalControllerEnabled;
        }
        finally
        {
            Cursor.lockState = _originalCursorLock;
            Cursor.visible = _originalCursorVisible;
        }
    }

    private static void SetCameraControl(bool enabled)
    {
        IsCameraControlEnabled = enabled;
        if (enabled && TipMsgCore.i?.mainplayer != null)
        {
            _orgPlayerPos = TipMsgCore.i.mainplayer.transform.position;
            _orgPlayerRot = TipMsgCore.i.mainplayer.transform.rotation;
        }
        if (!enabled)
        {
            IsPersonPlaying = false;
            SetCameraPlayback(false);
        }
        if (CameraSystem.Instance == null) return;
        if (enabled && _cameraDummy != null) CameraSystem.Instance.SetCameraFollowUnit(_cameraDummy);
        else if (TipMsgCore.i?.mainplayer != null)
        {
            CameraSystem.Instance.ResetMainCameraLocalTransform();
            CameraSystem.Instance.SetCameraFollowUnit(TipMsgCore.i.mainplayer.gameObject);
        }
        if (CameraSystem.Instance._MainCameraController != null)
            CameraSystem.Instance._MainCameraController.enabled = enabled ? !IsLookAtPlayer : _originalControllerEnabled;
    }

    private static void ResetDragState()
    {
        bool restoreRows = _isDraggingRow;
        var owner = _draggedTrack;
        bool person = _draggedIsPerson;
        var content = _draggedIsPerson ? _personPointListContent : _camPointListContent;
        if (content != null)
        {
            var scroll = content.GetComponentInParent<ScrollRect>();
            if (scroll != null) scroll.enabled = true;
        }
        _isDraggingRow = _isDraggingPanel = _isDraggingBall = _isDraggingCurveBg = false;
        _draggedRowObj = null;
        _draggedRowRect = null;
        _draggedTrack = null;
        _draggedIndex = _draggingCurveIndex = -1;
        if (restoreRows && owner != null)
            RefreshSingleList(person ? _personPointListContent : _camPointListContent, owner, person);
    }

    private static Material GetVisualMaterial(Color color)
    {
        if (_visualMaterials.TryGetValue(color, out var material) && material != null) return material;
        var shader = Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color") ?? Shader.Find("Standard");
        if (shader == null) throw new InvalidOperationException("找不到轨道可视化 Shader");
        material = new Material(shader) { color = color };
        _visualMaterials[color] = material;
        return material;
    }

    private static void ResizeCurvePool(List<Image> pool, int count, string name, Color color, bool raycast)
    {
        while (pool.Count < count)
        {
            var obj = new GameObject($"{name}_{pool.Count}");
            obj.transform.SetParent(_curveContentRect, false);
            var image = obj.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = raycast;
            image.rectTransform.anchorMin = image.rectTransform.anchorMax = image.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            pool.Add(image);
        }
        for (int i = 0; i < pool.Count; i++)
            if (pool[i].gameObject.activeSelf != (i < count)) pool[i].gameObject.SetActive(i < count);
    }

    public static void Tick()
    {
        if (!IsActive) return;
        if (Input.GetKeyDown(KeyCode.F10) || _cam == null || _cameraDummy == null ||
            TipMsgCore.i?.mainplayer == null || SceneManager.GetActiveScene().handle != _sceneHandle)
        {
            StopSystem();
            return;
        }
        if (_shotPlan != null) { TickShot(); return; }
        try { TickInput(); }
        finally
        {
            // Editing a curve, dragging a panel or typing must not freeze the playback clock.
            if (IsActive && IsCameraControlEnabled) AdvancePlayback(Time.deltaTime);
        }
    }

    private static bool SelectPlayableTrack(int startIndex)
    {
        for (int offset = 0; offset < Tracks.Count; offset++)
        {
            int index = (startIndex + offset + Tracks.Count) % Tracks.Count;
            if (Tracks[index].Points.Count < 2) continue;
            _currentTrackIndex = index;
            return true;
        }
        return false;
    }

    private static bool SetCameraPlayback(bool playing)
    {
        foreach (var track in Tracks) track.IsPlaying = false;
        if (playing && (!IsActive || !SelectPlayableTrack(Math.Max(0, _currentTrackIndex))))
        {
            IsGlobalPlaying = false;
            MelonLogger.Warning("[Orbit] 没有可播放的轨道：至少需要两个位置点");
            UpdateUI();
            return false;
        }
        IsGlobalPlaying = playing;
        if (playing)
        {
            if (!IsCameraControlEnabled) SetCameraControl(true);
            ActiveTrack.IsPlaying = true;
            SyncActiveTrackUI();
        }
        else UpdateUI();
        return true;
    }

    private static void SyncActiveTrackUI()
    {
        ResetDragState();
        RefreshTrackVisuals();
        RefreshSingleList(_camPointListContent, ActiveTrack, false);
        UpdateUI();
        UpdateCurveUI();
        UpdateSpeedLimitText();
    }

    private static void TogglePersonPlayback()
    {
        if (IsPersonPlaying) IsPersonPlaying = false;
        else if (PersonTrack.Points.Count >= 2 && TipMsgCore.i?.mainplayer != null)
        {
            if (!IsCameraControlEnabled) SetCameraControl(true);
            if (PersonTrack.Progress >= 1f) { PersonTrack.Progress = 0f; _lastPersonPointIndex = -1; }
            _isRecording = false;
            IsPersonPlaying = true;
        }
        else MelonLogger.Warning("[Orbit] 人物轨道至少需要两个位置点");
        UpdateUI();
    }

    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    private static Vector3 EvaluateTrack(OrbitTrack track, float progress)
    {
        float total = Mathf.Clamp01(progress) * (track.Points.Count - 1);
        int segment = Mathf.Clamp(Mathf.FloorToInt(total), 0, track.Points.Count - 2);
        return CatmullRom(track.Points[Math.Max(0, segment - 1)].Pos, track.Points[segment].Pos,
            track.Points[segment + 1].Pos, track.Points[Math.Min(track.Points.Count - 1, segment + 2)].Pos, total - segment);
    }

    private static void AdvancePlayback(float deltaTime)
    {
        if (IsPersonPlaying)
        {
            if (PersonTrack.Points.Count < 2 || TipMsgCore.i?.mainplayer == null)
            {
                IsPersonPlaying = false;
                UpdateUI();
            }
            else
            {
                PersonTrack.Progress = Mathf.Clamp01(PersonTrack.Progress + PersonTrack.BaseSpeed * Math.Max(0f, deltaTime));
                float total = PersonTrack.Progress * (PersonTrack.Points.Count - 1);
                int reached = Math.Min(Mathf.FloorToInt(total), PersonTrack.Points.Count - 1);
                int segment = Math.Min(reached, PersonTrack.Points.Count - 2);
                Vector3 direction = PersonTrack.Points[segment + 1].Pos - PersonTrack.Points[segment].Pos;
                var rotation = direction.sqrMagnitude > 0.000001f ? Quaternion.LookRotation(direction) : _orgPlayerRot;
                _orgPlayerPos = EvaluateTrack(PersonTrack, PersonTrack.Progress);
                _orgPlayerRot = rotation;
                TipMsgCore.i.mainplayer.SetPositionAndRotation(_orgPlayerPos, rotation, true);
                for (int i = _lastPersonPointIndex + 1; i <= reached; i++)
                    if (PersonTrack.Points[i].ActionID > 0) PlayEmotion(PersonTrack.Points[i].ActionID);
                _lastPersonPointIndex = reached;
                if (PersonTrack.Progress >= 1f) { IsPersonPlaying = false; UpdateUI(); }
            }
        }
        if (!IsGlobalPlaying || _cameraDummy == null) return;
        if (ActiveTrack == null || ActiveTrack.Points.Count < 2)
        {
            if (!SetCameraPlayback(true)) return;
        }
        float remaining = Math.Max(0f, deltaTime);
        int previousTrackIndex = _currentTrackIndex;
        // Preserve frame time across track boundaries, skip empty tracks, and cap work on a large hitch.
        for (int pass = 0; pass < 128; pass++)
        {
            var track = ActiveTrack;
            float rate = track.BaseSpeed * track.GetSpeedMultiplier(track.Progress);
            if (!IsFinite(rate) || rate <= 0f) break; // Zero multiplier is an explicit hold in the curve editor.
            float secondsToEnd = (1f - track.Progress) / rate;
            if (remaining < secondsToEnd)
            {
                track.Progress = Mathf.Clamp01(track.Progress + remaining * rate);
                break;
            }
            remaining -= secondsToEnd;
            track.Progress = 0f;
            track.IsPlaying = false;
            if (!SelectPlayableTrack(_currentTrackIndex + 1)) { SetCameraPlayback(false); return; }
            ActiveTrack.Progress = 0f;
            ActiveTrack.IsPlaying = true;
            if (remaining <= 0f) break;
        }
        if (_currentTrackIndex != previousTrackIndex) SyncActiveTrackUI();
        if (ActiveTrack?.Points.Count >= 2)
            _cameraDummy.transform.position = EvaluateTrack(ActiveTrack, ActiveTrack.Progress);
    }
}
