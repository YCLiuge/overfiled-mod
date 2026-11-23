using Il2Cpp;
using MelonLoader;
using MelonLoader.Utils;
using mod;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using static Il2Cpp.IExcelSystem;

using Action = System.Action;

public class TrackPoint
{
    public Vector3 Pos;
    public int ActionID = 0;

    public TrackPoint(Vector3 pos, int actionId = 0)
    {
        Pos = pos;
        ActionID = actionId;
    }
}

public class OrbitTrack
{
    public string Name;
    public List<TrackPoint> Points = new List<TrackPoint>();

    // 存储控制点：X代表进度(0~1)，Y代表速度倍率
    public List<Vector2> SpeedCurve = new List<Vector2>();

    public float Progress = 0f;
    public bool IsPlaying = false;
    public float BaseSpeed = 0.2f;

    // 速度上限
    public float MaxSpeedLimit = 2.0f;

    public GameObject VisualRoot;
    public LineRenderer Line;
    public List<GameObject> Spheres = new List<GameObject>();

    public void ClearVisuals()
    {
        if (VisualRoot != null)
        {
            VisualRoot.SetActive(false);
            UnityEngine.Object.Destroy(VisualRoot);
        }
        VisualRoot = null;
        Line = null;
        Spheres.Clear();
    }

    public void InitDefaultCurve()
    {
        SpeedCurve.Clear();
        SpeedCurve.Add(new Vector2(0f, 1f));
        SpeedCurve.Add(new Vector2(1f, 1f));
    }

    public float GetSpeedMultiplier(float progress)
    {
        if (SpeedCurve == null || SpeedCurve.Count < 2) return 1f;

        if (progress <= SpeedCurve[0].x) return SpeedCurve[0].y;
        if (progress >= SpeedCurve[SpeedCurve.Count - 1].x) return SpeedCurve[SpeedCurve.Count - 1].y;

        for (int i = 0; i < SpeedCurve.Count - 1; i++)
        {
            if (progress >= SpeedCurve[i].x && progress <= SpeedCurve[i + 1].x)
            {
                float interval = SpeedCurve[i + 1].x - SpeedCurve[i].x;
                if (interval <= 0.000001f) return SpeedCurve[i + 1].y;
                float t = (progress - SpeedCurve[i].x) / interval;
                return Mathf.Lerp(SpeedCurve[i].y, SpeedCurve[i + 1].y, t);
            }
        }
        return 1f;
    }
}

public static partial class CameraOrbitSystem
{
    public static bool IsActive { get; private set; } = false;
    public static bool IsCameraControlEnabled = true;

    public static List<OrbitTrack> Tracks = new List<OrbitTrack>();
    public static OrbitTrack ActiveTrack => (_currentTrackIndex >= 0 && _currentTrackIndex < Tracks.Count) ? Tracks[_currentTrackIndex] : null;

    public static OrbitTrack PersonTrack = new OrbitTrack { Name = "人物轨道" };
    public static bool IsPersonPlaying = false;
    private static int _lastPersonPointIndex = -1;

    private static bool _isRecording = false;
    private static Vector3 _lastRecordPos;
    private const float RECORD_THRESHOLD = 0.5f;

    private static Transform _selectedTarget = null;
    private static GameObject _targetSelectPanel;

    private static GameObject _cameraDummy;
    private static GameObject _dummyVisual;
    private static Camera _cam;
    private static Vector3 _orgPos;
    private static Quaternion _orgRot;

    private static Vector3 _orgPlayerPos;
    private static Quaternion _orgPlayerRot;

    private static int _currentTrackIndex = 0;

    public static bool IsGlobalPlaying = false;

    public static float MouseSensitivity = 2.0f;
    public static float FlySpeed = 10f;
    private static float _yaw = 0f;

    public static bool IsTrackVisible = true;
    public static bool IsLookAtPlayer = false;

    private static GameObject _canvasObj;
    private static GameObject _panelObj;
    private static GameObject _floatingBallObj;
    private static Text _infoText;
    private static Text _trackNameText;
    private static Transform _camPointListContent;
    private static Transform _personPointListContent;
    private static RectTransform _camPointListContentRect;
    private static RectTransform _personPointListContentRect;
    private static Text _speedLimitTxt;

    // --- 面板/悬浮球拖拽相关 ---
    private static bool _isDraggingPanel = false;
    private static Vector2 _panelDragOffset;

    private static bool _isDraggingBall = false;
    private static Vector2 _ballDragOffset;
    private static Vector2 _ballPointerDownPos;

    // --- 曲线编辑器相关 ---
    private static RectTransform _curveViewportRect; // 可视区
    private static RectTransform _curveContentRect;  // 实际内容区
    private static List<Image> _curvePoints = new List<Image>();
    private static List<Image> _curveLines = new List<Image>();
    private static List<Image> _bgLines = new List<Image>();

    private static int _draggingCurveIndex = -1;
    private static bool _isDraggingCurveBg = false;
    private static Vector2 _curveBgStartPos;
    private static Vector2 _curveBgStartMouse;
    private static float _curveZoom = 1.0f;

    private const float VIEW_WIDTH = 500f;
    private const float CONTENT_WIDTH = 2000f;
    private const float CURVE_HEIGHT = 100f;

    // --- 记录点拖拽状态 ---
    private static bool _isDraggingRow = false;
    private static bool _draggedIsPerson = false;
    private static int _draggedIndex = -1;
    private static GameObject _draggedRowObj;
    private static RectTransform _draggedRowRect;
    private static Vector2 _draggedStartPos;
    private static float _draggedStartMouseY;

    private static string SaveDir => Path.Combine(MelonEnvironment.UserDataDirectory, "mod储存数据");
    private static string SavePath => Path.Combine(SaveDir, "orbit_data.json");

    public static void PreInit()
    {
        _cam = Camera.main;
    }

    public static void StartSystem()
    {
        if (IsActive) return;
        if (_cam == null) _cam = Camera.main;
        if (_cam == null || TipMsgCore.i?.mainplayer == null || CameraSystem.Instance == null)
            throw new InvalidOperationException("相机或玩家尚未初始化");

        CaptureGameControl();
        IsActive = true;
        try
        {
        IsCameraControlEnabled = true;

        _orgPos = _cam.transform.position;
        _orgRot = _cam.transform.rotation;
        _originalCameraFov = _cam.fieldOfView;

        Vector3 euler = _orgRot.eulerAngles;
        _yaw = euler.y;

        if (TipMsgCore.i != null && TipMsgCore.i.mainplayer != null)
        {
            _orgPlayerPos = TipMsgCore.i.mainplayer.transform.position;
            _orgPlayerRot = TipMsgCore.i.mainplayer.transform.rotation;
            _lastRecordPos = _orgPlayerPos;
        }

        if (_cameraDummy == null)
        {
            _cameraDummy = new GameObject("CameraOrbit_Dummy");
            _cameraDummy.transform.position = _cam.transform.position;
            _cameraDummy.transform.rotation = Quaternion.Euler(0f, _yaw, 0f);
            _cameraDummy.layer = 0;

            _dummyVisual = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            _dummyVisual.name = "DummyVisual";
            _dummyVisual.transform.SetParent(_cameraDummy.transform, false);
            _dummyVisual.transform.localScale = Vector3.one * 0.2f;
            _dummyVisual.layer = 0;

            var col = _dummyVisual.GetComponent<Collider>();
            if (col != null) UnityEngine.Object.Destroy(col);

            var rend = _dummyVisual.GetComponent<Renderer>();
            if (rend != null)
            {
                rend.sharedMaterial = GetVisualMaterial(Color.red);
            }
        }

        if (CameraSystem.Instance != null)
        {
            CameraSystem.Instance.SetCameraFollowUnit(_cameraDummy);
        }

        ToggleGameControl(true);

        _currentTrackIndex = 0;
        if (Tracks.Count == 0) CreateTrack("默认轨道_1");

        IsTrackVisible = true;
        IsLookAtPlayer = false;
        IsPersonPlaying = false;
        _isRecording = false;
        PersonTrack.Progress = 0f;
        _lastPersonPointIndex = -1;
        IsGlobalPlaying = false;
        foreach (var track in Tracks) { track.Progress = 0f; track.IsPlaying = false; }

        BuildUI();
        RefreshTrackVisuals();
        RefreshPointListUI();
        UpdateUI();
        UpdateCurveUI();

        MelonLogger.Msg("[Orbit] 相机轨道系统已开启");
        }
        catch
        {
            StopSystem();
            throw;
        }
    }

    public static void StopSystem()
    {
        bool wasActive = IsActive;
        try { EndCinematicSession(); }
        catch (Exception ex) { MelonLogger.Warning($"[Orbit] 恢复拍摄状态: {ex.Message}"); }
        IsActive = false;
        IsGlobalPlaying = false;
        IsPersonPlaying = false;
        _isRecording = false;
        try
        {
            if (wasActive && CameraSystem.Instance != null)
            {
                CameraSystem.Instance.ResetMainCameraLocalTransform();
                if (TipMsgCore.i?.mainplayer != null)
                    CameraSystem.Instance.SetCameraFollowUnit(TipMsgCore.i.mainplayer.gameObject);
            }
        }
        catch (Exception ex) { MelonLogger.Warning($"[Orbit] 恢复相机: {ex.Message}"); }
        finally
        {
            try { RestoreGameControl(); }
            catch (Exception ex) { MelonLogger.Warning($"[Orbit] 恢复控制状态: {ex.Message}"); }
            if (_cameraDummy != null) UnityEngine.Object.Destroy(_cameraDummy);
            _cameraDummy = null;
            _dummyVisual = null;
            foreach (var t in Tracks) { t.IsPlaying = false; t.ClearVisuals(); }
            PersonTrack.ClearVisuals();
            if (_canvasObj != null) { _canvasObj.SetActive(false); UnityEngine.Object.Destroy(_canvasObj); }
            if (_ownedEventSystem != null)
            {
                _ownedEventSystem.SetActive(false);
                UnityEngine.Object.Destroy(_ownedEventSystem);
            }
            _ownedEventSystem = null;
            _canvasObj = null;
            _panelObj = null;
            _floatingBallObj = null;
            _infoText = null;
            _trackNameText = null;
            _speedLimitTxt = null;
            _speedLimitSlider = null;
            _camPointListContent = null;
            _personPointListContent = null;
            _camPointListContentRect = null;
            _personPointListContentRect = null;
            _targetSelectPanel = null;
            _selectedTarget = null;
            _curveViewportRect = null;
            _curveContentRect = null;
            _cameraRows.Clear();
            _personRows.Clear();
            _curvePoints.Clear();
            _curveLabels.Clear();
            _curveLines.Clear();
            _bgLines.Clear();
            ResetDragState();
            foreach (var material in _visualMaterials.Values)
                if (material != null) UnityEngine.Object.Destroy(material);
            _visualMaterials.Clear();
            _cam = null;
        }
        if (wasActive) MelonLogger.Msg("[Orbit] 相机轨道系统已关闭，轨道数据保留至模块卸载");
    }

    private static void ToggleGameControl(bool enableControl)
    {
        if (CameraSystem.Instance != null && CameraSystem.Instance._MainCameraController != null)
            CameraSystem.Instance._MainCameraController.enabled = enableControl;

        Cursor.lockState = enableControl ? CursorLockMode.None : CursorLockMode.Locked;
        Cursor.visible = enableControl;
    }

    private static void TickInput()
    {
        if (!IsActive) return;

        // 0. 处理曲线交互
        if (HandleCurveInteraction()) return;

        // 1. 处理列表项拖拽排序
        if (Input.GetMouseButtonDown(0) && !_isDraggingRow)
        {
            if (EventSystem.current != null)
            {
                PointerEventData eventData = new PointerEventData(EventSystem.current);
                eventData.position = Input.mousePosition;
                var results = new Il2CppSystem.Collections.Generic.List<RaycastResult>();
                EventSystem.current.RaycastAll(eventData, results);

                foreach (var res in results)
                {
                    if (res.gameObject.name == "DragHandle")
                    {
                        Transform rowTransform = res.gameObject.transform.parent;
                        Transform activeContent = rowTransform.parent;

                        if (activeContent != _camPointListContent && activeContent != _personPointListContent) continue;
                        _draggedIsPerson = (activeContent == _personPointListContent);
                        OrbitTrack targetTrack = _draggedIsPerson ? PersonTrack : ActiveTrack;

                        if (targetTrack != null)
                        {
                            int index = rowTransform.GetSiblingIndex();
                            if (index >= 0 && index < targetTrack.Points.Count)
                            {
                                _draggedTrack = targetTrack;
                                _isDraggingRow = true;
                                _draggedIndex = index;
                                _draggedRowObj = rowTransform.gameObject;
                                _draggedRowRect = rowTransform.GetComponent<RectTransform>();
                                _draggedStartPos = _draggedRowRect.anchoredPosition;
                                _draggedStartMouseY = Input.mousePosition.y;

                                _draggedRowObj.transform.SetAsLastSibling();
                                var img = _draggedRowObj.GetComponent<Image>();
                                if (img != null) img.color = new Color(1f, 1f, 1f, 0.5f);

                                // 【核心修复】拖拽开始时，禁用 ScrollRect 防止它跟着滚
                                var scrollRect = activeContent.GetComponentInParent<ScrollRect>();
                                if (scrollRect != null) scrollRect.enabled = false;

                                break;
                            }
                        }
                    }
                }
            }
        }

        if (_isDraggingRow && _draggedRowRect != null)
        {
            Vector2 mousePos = Input.mousePosition;
            float deltaY = mousePos.y - _draggedStartMouseY;
            float scale = _canvasObj.GetComponent<Canvas>().scaleFactor;

            _draggedRowRect.anchoredPosition = new Vector2(_draggedStartPos.x, _draggedStartPos.y + deltaY / scale);

            if (Input.GetMouseButtonUp(0))
            {
                _isDraggingRow = false;
                Transform activeContent = _draggedIsPerson ? _personPointListContent : _camPointListContent;
                OrbitTrack targetTrack = _draggedTrack;
                float rowSpacing = 34f;

                float newY = _draggedRowRect.anchoredPosition.y;
                int targetIndex = Mathf.RoundToInt((-newY - 15f) / rowSpacing);
                targetIndex = Mathf.Clamp(targetIndex, 0, targetTrack.Points.Count - 1);

                if (_draggedIndex >= 0 && _draggedIndex < targetTrack.Points.Count && targetIndex != _draggedIndex && targetIndex >= 0 && targetIndex < targetTrack.Points.Count)
                {
                    var item = targetTrack.Points[_draggedIndex];
                    targetTrack.Points.RemoveAt(_draggedIndex);
                    targetTrack.Points.Insert(targetIndex, item);
                }

                if (_draggedRowObj != null)
                {
                    var img = _draggedRowObj.GetComponent<Image>();
                    if (img != null) img.color = new Color(1, 1, 1, 0);
                }

                // 【核心修复】拖拽结束，恢复 ScrollRect
                var scrollRect = activeContent.GetComponentInParent<ScrollRect>();
                if (scrollRect != null) scrollRect.enabled = true;

                _draggedRowObj = null;
                _draggedRowRect = null;

                RefreshTrackVisuals(targetTrack);
                RefreshPointListUI();
                UpdateCurveUI();
                return;
            }
            return; // 拖拽期间阻断后续代码，防止误触面板拖拽
        }

        // 2. 正常 UI 拖拽和输入逻辑
        HandleUIDrag();
        if (Input.GetKeyDown(KeyCode.F10)) { StopSystem(); return; }

        if (Input.GetKeyDown(KeyCode.Tab))
        {
            SetCameraControl(!IsCameraControlEnabled);
            UpdateUI();
            return;
        }

        // 录制逻辑
        if (_isRecording && TipMsgCore.i != null && TipMsgCore.i.mainplayer != null)
        {
            Vector3 curPos = TipMsgCore.i.mainplayer.transform.position;
            _orgPlayerPos = curPos;
            _orgPlayerRot = TipMsgCore.i.mainplayer.transform.rotation;
            if (PersonTrack.Points.Count >= 4096)
            {
                _isRecording = false;
                MelonLogger.Warning("[Orbit] 人物轨道已达到 4096 点，录制停止");
                UpdateUI();
            }
            else if (Vector3.Distance(curPos, _lastRecordPos) >= RECORD_THRESHOLD)
            {
                PersonTrack.Points.Add(new TrackPoint(curPos));
                _lastRecordPos = curPos;
                RefreshTrackVisuals(PersonTrack);
                RefreshSingleList(_personPointListContent, PersonTrack, true);
            }
        }

        if (!IsCameraControlEnabled) return;

        bool isTyping = EventSystem.current != null && EventSystem.current.currentSelectedGameObject != null
                        && EventSystem.current.currentSelectedGameObject.GetComponent<InputField>() != null;
        if (isTyping) return;

        if (!_cleanFrame && Input.GetKeyDown(KeyCode.Alpha1))
            SetTrackVisibility(!IsTrackVisible);

        if (Input.GetKeyDown(KeyCode.Alpha2))
        {
            IsLookAtPlayer = !IsLookAtPlayer;
            if (CameraSystem.Instance != null)
            {
                if (IsLookAtPlayer)
                {
                    if (CameraSystem.Instance._MainCameraController != null)
                        CameraSystem.Instance._MainCameraController.enabled = false;
                    
                }
                else
                {
                    if (CameraSystem.Instance._MainCameraController != null)
                        CameraSystem.Instance._MainCameraController.enabled = true;
                    CameraSystem.Instance.ResetMainCameraLocalTransform();
                }
            }
        }
        if (CameraSystem.Instance?._MainCameraController != null && !CameraSystem.Instance._MainCameraController.enabled)
            CameraSystem.Instance._MainCameraController.LateUpdate();

        if (Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt)) return;

        if (!IsPointerOverUI() && _cameraDummy != null)
        {
            float mouseX = Input.GetAxis("Mouse X") * MouseSensitivity;
            _yaw += mouseX;
            _cameraDummy.transform.rotation = Quaternion.Euler(0f, _yaw, 0f);
        }

        // 相机自由飞行
        if (!IsGlobalPlaying)
        {
            float h = Input.GetAxis("Horizontal");
            float v = Input.GetAxis("Vertical");
            float upDown = 0f;
            if (Input.GetKey(KeyCode.LeftShift)) upDown = 1f;
            if (Input.GetKey(KeyCode.LeftControl)) upDown = -1f;

            if ((Mathf.Abs(h) > 0.01f || Mathf.Abs(v) > 0.01f || Mathf.Abs(upDown) > 0.01f) && _cameraDummy != null)
            {
                Vector3 camForward = _cam.transform.forward;
                camForward.y = 0f; camForward.Normalize();
                Vector3 camRight = _cam.transform.right;
                camRight.y = 0f; camRight.Normalize();

                Vector3 moveDir = camForward * v + camRight * h + Vector3.up * upDown;
                _cameraDummy.transform.position += moveDir.normalized * FlySpeed * Time.deltaTime;
            }
            return;
        }

    }

    public static void LateTick()
    {
        if (!IsActive || !IsCameraControlEnabled || _cam == null) return;

        if (!IsPersonPlaying && !_isRecording && TipMsgCore.i != null && TipMsgCore.i.mainplayer != null)
        {
            TipMsgCore.i.mainplayer.SetPositionAndRotation(_orgPlayerPos, _orgPlayerRot, true);
        }

        if (_shotPlan != null) { ApplyShotPose(); return; }
        if (IsLookAtPlayer && _cameraDummy != null)
        {
            Transform target = _selectedTarget;
            Transform myTransform = TipMsgCore.i != null && TipMsgCore.i.mainplayer != null ? TipMsgCore.i.mainplayer.transform : null;

            if (target == null && myTransform != null) target = myTransform;

            if (target != null)
            {
                Vector3 targetPos = (target == myTransform) ? target.position + Vector3.up * 2f : target.position;
                _cam.transform.position = _cameraDummy.transform.position;
                _cam.transform.LookAt(targetPos);
            }
        }
    }

    // ==========================================
    // 曲线编辑器逻辑
    // ==========================================
    private static bool HandleCurveInteraction()
    {
        if (_curveViewportRect == null || ActiveTrack == null || !_panelObj.activeInHierarchy) return false;
        Vector2 mousePos = Input.mousePosition;
        bool isOverViewport = RectTransformUtility.RectangleContainsScreenPoint(_curveViewportRect, mousePos, null);

        // 0. 处理鼠标滚轮横向缩放
        if (isOverViewport)
        {
            float scroll = Input.mouseScrollDelta.y;
            if (Mathf.Abs(scroll) > 0.01f)
            {
                _curveZoom = Mathf.Clamp(_curveZoom + scroll * 0.2f, 0.2f, 10f);
                UpdateCurveUI();
                return true;
            }
        }

        // 1. 处理右键交互 (加点/删点)
        if (isOverViewport && Input.GetMouseButtonDown(1) && !Input.GetMouseButton(0))
        {
            // 【新增】优先检测是否点中了已存在的控制点，如果是则删除
            for (int i = 0; i < ActiveTrack.SpeedCurve.Count; i++)
            {
                if (RectTransformUtility.RectangleContainsScreenPoint(_curvePoints[i].rectTransform, mousePos, null))
                {
                    // 强制要求至少保留 2 个控制点（起点和终点），否则曲线无法计算
                    if (i > 0 && i < ActiveTrack.SpeedCurve.Count - 1)
                    {
                        _draggingCurveIndex = -1;
                        ActiveTrack.SpeedCurve.RemoveAt(i);
                        UpdateCurveUI();
                    }
                    else
                    {
                        MelonLogger.Warning("[Orbit] 至少需要保留 2 个控制点！");
                    }
                    return true;
                }
            }

            // 如果没点中已有点，说明点在了空白处，则添加新点
            Vector2 localMousePos;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_curveContentRect, mousePos, null, out localMousePos);

            float actualWidth = CONTENT_WIDTH * _curveZoom;
            float progress = Mathf.Clamp01((localMousePos.x + actualWidth / 2f) / actualWidth);
            float speed = Mathf.Clamp01((localMousePos.y + CURVE_HEIGHT / 2f) / CURVE_HEIGHT) * ActiveTrack.MaxSpeedLimit;

            _draggingCurveIndex = -1;
            int existing = ActiveTrack.SpeedCurve.FindIndex(p => Mathf.Abs(p.x - progress) < 0.00001f);
            if (existing >= 0) ActiveTrack.SpeedCurve[existing] = new Vector2(ActiveTrack.SpeedCurve[existing].x, speed);
            else if (ActiveTrack.SpeedCurve.Count < 256) ActiveTrack.SpeedCurve.Add(new Vector2(progress, speed));
            ActiveTrack.SpeedCurve.Sort((a, b) => a.x.CompareTo(b.x));

            UpdateCurveUI();
            return true;
        }

        // 2. 处理拖拽交互
        if (Input.GetMouseButtonDown(0) && isOverViewport)
        {
            // 判断点中了哪个控制点
            for (int i = 0; i < ActiveTrack.SpeedCurve.Count; i++)
            {
                if (RectTransformUtility.RectangleContainsScreenPoint(_curvePoints[i].rectTransform, mousePos, null))
                {
                    _draggingCurveIndex = i;
                    return true;
                }
            }

            // 如果没点中控制点，就是点中了背景，准备拖拽平移
            _isDraggingCurveBg = true;
            _curveBgStartPos = _curveContentRect.anchoredPosition;
            _curveBgStartMouse = mousePos;
        }

        // 3. 处理拖拽移动
        if (Input.GetMouseButton(0))
        {
            if (_draggingCurveIndex >= 0 && _draggingCurveIndex < ActiveTrack.SpeedCurve.Count)
            {
                Vector2 localMousePos;
                RectTransformUtility.ScreenPointToLocalPointInRectangle(_curveContentRect, mousePos, null, out localMousePos);

                // 点拖拽只改变 Y 轴速度 (限制在 0 ~ 上限 范围)
                float speed = Mathf.Clamp01((localMousePos.y + CURVE_HEIGHT / 2f) / CURVE_HEIGHT) * ActiveTrack.MaxSpeedLimit;

                var pt = ActiveTrack.SpeedCurve[_draggingCurveIndex];
                pt.y = speed;
                ActiveTrack.SpeedCurve[_draggingCurveIndex] = pt;

                UpdateCurveUI();
                return true;
            }
            else if (_isDraggingCurveBg)
            {
                // 背景拖拽：平移画布
                float deltaX = mousePos.x - _curveBgStartMouse.x;
                _curveContentRect.anchoredPosition = new Vector2(_curveBgStartPos.x + deltaX, _curveContentRect.anchoredPosition.y);
                return true;
            }
        }

        if (Input.GetMouseButtonUp(0) || Input.GetMouseButtonUp(1))
        {
            _draggingCurveIndex = -1;
            _isDraggingCurveBg = false;
        }

        return false;
    }

    // Reuse Unity objects while dragging: only grow pools when node counts increase.
    private static void UpdateCurveUI()
    {
        if (_curveContentRect == null || ActiveTrack == null) return;
        float width = CONTENT_WIDTH * _curveZoom;
        _curveContentRect.sizeDelta = new Vector2(width, CURVE_HEIGHT);
        int pointCount = ActiveTrack.Points.Count;
        int gridCount = pointCount > 1 ? Math.Min(pointCount, 256) : 0;
        ResizeCurvePool(_bgLines, gridCount, "BgLine", new Color(0.6f, 0.2f, 0.8f, 0.3f), false);
        for (int i = 0; i < gridCount; i++)
        {
            var rect = _bgLines[i].rectTransform;
            rect.sizeDelta = new Vector2(2f, CURVE_HEIGHT);
            rect.anchoredPosition = new Vector2(i / (float)(gridCount - 1) * width - width / 2f, 0);
        }
        var curve = ActiveTrack.SpeedCurve;
        ResizeCurvePool(_curveLines, Math.Max(0, curve.Count - 1), "Line", Color.cyan, false);
        ResizeCurvePool(_curvePoints, curve.Count, "Point", Color.yellow, true);
        while (_curveLabels.Count < curve.Count)
        {
            var text = CreateText(_curvePoints[_curveLabels.Count].transform, "ValText", "", 11, Color.white,
                new Vector2(0, 10), new Vector2(40, 15));
            text.rectTransform.anchorMin = text.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            text.rectTransform.pivot = new Vector2(0.5f, 0f);
            _curveLabels.Add(text);
        }
        float limit = Math.Max(0.01f, ActiveTrack.MaxSpeedLimit);
        for (int i = 0; i < curve.Count; i++)
        {
            Vector2 position = new Vector2(curve[i].x * width - width / 2f, curve[i].y / limit * CURVE_HEIGHT - CURVE_HEIGHT / 2f);
            var rect = _curvePoints[i].rectTransform;
            rect.sizeDelta = new Vector2(14, 14);
            rect.anchoredPosition = position;
            _curvePoints[i].transform.SetAsLastSibling();
            _curveLabels[i].text = curve[i].y.ToString("F1");
            if (i == 0) continue;
            Vector2 previous = _curvePoints[i - 1].rectTransform.anchoredPosition;
            Vector2 delta = position - previous;
            var line = _curveLines[i - 1].rectTransform;
            line.anchoredPosition = (previous + position) / 2f;
            line.sizeDelta = new Vector2(delta.magnitude, 2f);
            line.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
        }
    }

    public static void SaveTracks()
    {
        try
        {
            var data = new SaveData { Tracks = Tracks.Select(ToSaveTrack).ToList(), PersonTrack = ToSaveTrack(PersonTrack) };
            OrbitSaveCodec.SaveAtomic(SavePath, data);
            MelonLogger.Msg($"[Orbit] 轨道数据已保存至: {SavePath}");
        }
        catch (Exception ex) { MelonLogger.Error($"[Orbit] 保存失败: {ex.Message}"); }
    }

    private static SaveTrack ToSaveTrack(OrbitTrack track) => new SaveTrack
    {
        Name = track.Name, MaxSpeedLimit = track.MaxSpeedLimit, BaseSpeed = track.BaseSpeed,
        SpeedCurve = track.SpeedCurve.Select(p => new SaveCurvePoint { x = p.x, y = p.y }).ToList(),
        Points = track.Points.Select(p => new SavePoint { x = p.Pos.x, y = p.Pos.y, z = p.Pos.z, actId = p.ActionID }).ToList()
    };

    private static OrbitTrack FromSaveTrack(SaveTrack track) => new OrbitTrack
    {
        Name = track.Name, MaxSpeedLimit = track.MaxSpeedLimit, BaseSpeed = track.BaseSpeed,
        SpeedCurve = track.SpeedCurve.Select(p => new Vector2(p.x, p.y)).ToList(),
        Points = track.Points.Select(p => new TrackPoint(new Vector3(p.x, p.y, p.z), p.actId)).ToList()
    };

    public static void LoadTracks()
    {
        try
        {
            if (!File.Exists(SavePath)) { MelonLogger.Warning("[Orbit] 尚无轨道存档"); return; }
            // Validate and map the entire replacement before touching the live tracks.
            var data = OrbitSaveCodec.Deserialize(File.ReadAllText(SavePath));
            var replacement = data.Tracks.Select(FromSaveTrack).ToList();
            var person = FromSaveTrack(data.PersonTrack);
            if (replacement.Count == 0)
            {
                var empty = new OrbitTrack { Name = "默认轨道_1" };
                empty.InitDefaultCurve();
                replacement.Add(empty);
            }
            IsGlobalPlaying = false;
            IsPersonPlaying = false;
            _isRecording = false;
            _lastPersonPointIndex = -1;
            ResetDragState();
            foreach (var track in Tracks) track.ClearVisuals();
            PersonTrack.ClearVisuals();
            Tracks = replacement;
            PersonTrack = person;
            _currentTrackIndex = 0;
            RefreshTrackVisuals();
            RefreshPointListUI();
            UpdateUI();
            UpdateCurveUI();
            UpdateSpeedLimitText();
        }
        catch (Exception ex) { MelonLogger.Error($"[Orbit] 读取失败，保留现有轨道: {ex.Message}"); }
    }

    private static void BuildUI()
    {
        if (UnityEngine.Object.FindObjectOfType<EventSystem>() == null)
        {
            var es = new GameObject("Orbit_EventSystem");
            _ownedEventSystem = es;
            es.AddComponent<EventSystem>();
            es.AddComponent<StandaloneInputModule>();
        }

        _canvasObj = new GameObject("OrbitUI_Canvas");
        var canvas = _canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 9999;

        var scaler = _canvasObj.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        _canvasObj.AddComponent<GraphicRaycaster>();

        _panelObj = new GameObject("MainPanel");
        _panelObj.transform.SetParent(_canvasObj.transform, false);
        var panelImg = _panelObj.AddComponent<Image>();
        panelImg.color = new Color(0.1f, 0.1f, 0.1f, 0.95f);

        var panelRect = _panelObj.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0, 1);
        panelRect.anchorMax = new Vector2(0, 1);
        panelRect.pivot = new Vector2(0, 1);
        panelRect.anchoredPosition = new Vector2(20, -20);
        panelRect.sizeDelta = new Vector2(680, 920);

        CreateText(_panelObj.transform, "Title", "多轨道相机系统", 18, Color.white, new Vector2(0, -10), new Vector2(600, 30)).fontStyle = FontStyle.Bold;

        // 【核心修复】坐标改为 320，避开标题
        var minBtn = CreateButton(_panelObj.transform, "—", new Vector2(320, -10), () => {
            _panelObj.SetActive(false);
            _floatingBallObj.SetActive(true);
        }, 30, 30);
        minBtn.GetComponent<Image>().color = new Color(0.3f, 0.3f, 0.3f);

        CreateText(_panelObj.transform, "Hint", "Tab 交还 | 左键拖点 | 右键空白加点 | 右键点删点 | 滚轮缩放", 12, Color.yellow, new Vector2(0, -40), new Vector2(640, 20));
        _infoText = CreateText(_panelObj.transform, "Info", "", 14, Color.cyan, new Vector2(0, -65), new Vector2(640, 25));

        CreateButton(_panelObj.transform, "选择观看目标", new Vector2(0, -100), () => { ShowTargetSelect(); }, 620, 30);
        CreateButton(_panelObj.transform, "读取数据", new Vector2(-160, -135), () => { LoadTracks(); }, 300, 30);
        CreateButton(_panelObj.transform, "保存数据", new Vector2(160, -135), () => { SaveTracks(); }, 300, 30);

        CreateButton(_panelObj.transform, "◀ 上一条相机轨道", new Vector2(-170, -175), () => { SwitchTrack(-1); }, 300, 30);
        CreateButton(_panelObj.transform, "下一条相机轨道 ▶", new Vector2(170, -175), () => { SwitchTrack(1); }, 300, 30);
        _trackNameText = CreateText(_panelObj.transform, "TrackName", "当前相机轨道", 16, Color.yellow, new Vector2(0, -215), new Vector2(640, 25));

        CreateButton(_panelObj.transform, "新建空相机轨道", new Vector2(0, -245), () => { CreateTrack($"轨道_{Tracks.Count + 1}"); }, 620, 30);
        CreateButton(_panelObj.transform, "播放/暂停 相机轨道", new Vector2(0, -285), () => { SetCameraPlayback(!IsGlobalPlaying); }, 620, 35);

        // ==========================================
        // 曲线编辑器可视区
        // ==========================================
        var viewportObj = new GameObject("CurveViewport");
        viewportObj.transform.SetParent(_panelObj.transform, false);
        var vImg = viewportObj.AddComponent<Image>();
        vImg.color = new Color(0.15f, 0.15f, 0.15f, 1f);
        _curveViewportRect = viewportObj.GetComponent<RectTransform>();
        _curveViewportRect.anchorMin = new Vector2(0.5f, 1); _curveViewportRect.anchorMax = new Vector2(0.5f, 1);
        _curveViewportRect.pivot = new Vector2(0.5f, 1);
        _curveViewportRect.anchoredPosition = new Vector2(0, -330);
        _curveViewportRect.sizeDelta = new Vector2(VIEW_WIDTH, CURVE_HEIGHT);

        // 添加遮罩防止画布超出
        viewportObj.AddComponent<RectMask2D>();

        // 实际内容区
        var contentObj = new GameObject("CurveContent");
        contentObj.transform.SetParent(viewportObj.transform, false);
        _curveContentRect = contentObj.AddComponent<RectTransform>();
        _curveContentRect.anchorMin = new Vector2(0.5f, 0.5f); _curveContentRect.anchorMax = new Vector2(0.5f, 0.5f);
        _curveContentRect.pivot = new Vector2(0.5f, 0.5f);
        _curveContentRect.anchoredPosition = Vector2.zero;
        _curveContentRect.sizeDelta = new Vector2(CONTENT_WIDTH, CURVE_HEIGHT);

        // 自动居中画布（让 x=0.5 在视图中间）
        _curveContentRect.anchoredPosition = new Vector2(-CONTENT_WIDTH / 2f + VIEW_WIDTH / 2f, 0);

        // 方便接收右键事件的透明背景
        var bgHitbox = new GameObject("CurveHitbox");
        bgHitbox.transform.SetParent(contentObj.transform, false);
        var hitboxImg = bgHitbox.AddComponent<Image>();
        hitboxImg.color = new Color(0, 0, 0, 0);
        hitboxImg.raycastTarget = true;
        var hitboxRect = hitboxImg.rectTransform;
        hitboxRect.anchorMin = Vector2.zero; hitboxRect.anchorMax = Vector2.one;
        hitboxRect.offsetMin = Vector2.zero; hitboxRect.offsetMax = Vector2.zero;

        // 速度上限滑块
        _speedLimitTxt = CreateText(_panelObj.transform, "SpeedLimitTxt", "速度上限: 2.0", 14, Color.white, new Vector2(-200, -450), new Vector2(200, 25));
        _speedLimitTxt.alignment = TextAnchor.MiddleLeft;
        _speedLimitSlider = CreateSlider(_panelObj.transform, new Vector2(70, -460), 0.5f, 10f, ActiveTrack?.MaxSpeedLimit ?? 2f, (val) => {
            if (ActiveTrack != null)
            {
                ActiveTrack.MaxSpeedLimit = val;
                for (int i = 0; i < ActiveTrack.SpeedCurve.Count; i++)
                {
                    var point = ActiveTrack.SpeedCurve[i];
                    point.y = Mathf.Clamp(point.y, 0f, val);
                    ActiveTrack.SpeedCurve[i] = point;
                }
                _speedLimitTxt.text = $"速度上限: {val:F1}";
                UpdateCurveUI();
            }
        }, 400);

        CreateButton(_panelObj.transform, "添加 相机位置点", new Vector2(0, -500), () => { AddPoint(false); }, 620, 35);

        CreateText(_panelObj.transform, "PersonTitle", "--- 人物移动轨道 ---", 16, Color.green, new Vector2(0, -550), new Vector2(640, 25));
        CreateButton(_panelObj.transform, "播放/暂停 人物轨道", new Vector2(-160, -590), () => { TogglePersonPlayback(); }, 300, 35);
        var recBtn = CreateButton(_panelObj.transform, "开始/停止 录制", new Vector2(160, -590), () => {
            _isRecording = !_isRecording;
            if (_isRecording) IsPersonPlaying = false;
            if (_isRecording && TipMsgCore.i != null && TipMsgCore.i.mainplayer != null)
                _lastRecordPos = TipMsgCore.i.mainplayer.transform.position;
            UpdateUI();
        }, 300, 35);
        CreateButton(_panelObj.transform, "添加 人物当前位置点", new Vector2(0, -640), () => { AddPoint(true); }, 620, 35);

        CreateText(_panelObj.transform, "CamListTitle", "相机点 (点≡拖拽):", 14, Color.gray, new Vector2(-160, -680), new Vector2(300, 20));
        BuildScrollList(_panelObj.transform, new Vector2(-160, -700), 310, 160, out _camPointListContent, out _camPointListContentRect);

        CreateText(_panelObj.transform, "PersonListTitle", "人物点 (点≡拖拽):", 14, Color.gray, new Vector2(160, -680), new Vector2(300, 20));
        BuildScrollList(_panelObj.transform, new Vector2(160, -700), 310, 160, out _personPointListContent, out _personPointListContentRect);

        var quitBtn = CreateButton(_panelObj.transform, "退出并恢复 (F10)", new Vector2(0, -890), () => { StopSystem(); }, 640, 40);
        quitBtn.GetComponent<Image>().color = new Color(0.7f, 0.2f, 0.2f);

        _floatingBallObj = new GameObject("FloatingBall");
        _floatingBallObj.transform.SetParent(_canvasObj.transform, false);
        var ballImg = _floatingBallObj.AddComponent<Image>();
        ballImg.color = new Color(0.2f, 0.6f, 1f, 0.9f);
        var ballRect = _floatingBallObj.GetComponent<RectTransform>();
        ballRect.anchorMin = new Vector2(0, 1); ballRect.anchorMax = new Vector2(0, 1);
        ballRect.pivot = new Vector2(0.5f, 0.5f);
        ballRect.anchoredPosition = new Vector2(80, -80);
        ballRect.sizeDelta = new Vector2(60, 60);

        var ballTextObj = new GameObject("Text");
        ballTextObj.transform.SetParent(_floatingBallObj.transform, false);
        var ballText = ballTextObj.AddComponent<Text>();
        ballText.text = "镜头"; ballText.fontSize = 18; ballText.color = Color.white; ballText.alignment = TextAnchor.MiddleCenter;
        ballText.font = Resources.GetBuiltinResource<Font>("Arial.ttf"); ballText.raycastTarget = false;
        var ballTextRect = ballTextObj.GetComponent<RectTransform>();
        ballTextRect.anchorMin = Vector2.zero; ballTextRect.anchorMax = Vector2.one;
        ballTextRect.offsetMin = Vector2.zero; ballTextRect.offsetMax = Vector2.zero;

        _floatingBallObj.SetActive(false);

        UpdateUI();
        UpdateCurveUI();
        UpdateSpeedLimitText();
    }

    private static void UpdateSpeedLimitText()
    {
        if (_speedLimitTxt != null && ActiveTrack != null)
        {
            _speedLimitTxt.text = $"速度上限: {ActiveTrack.MaxSpeedLimit:F1}";
            if (_speedLimitSlider != null) _speedLimitSlider.SetValueWithoutNotify(ActiveTrack.MaxSpeedLimit);
        }
    }

    private static void BuildScrollList(Transform parent, Vector2 pos, float w, float h, out Transform contentOut, out RectTransform contentRectOut)
    {
        var scrollObj = new GameObject("ScrollView");
        scrollObj.transform.SetParent(parent, false);
        var scrollRect = scrollObj.AddComponent<ScrollRect>();
        var scrollRectComp = scrollObj.GetComponent<RectTransform>();
        scrollRectComp.anchorMin = new Vector2(0.5f, 1); scrollRectComp.anchorMax = new Vector2(0.5f, 1);
        scrollRectComp.pivot = new Vector2(0.5f, 1);
        scrollRectComp.anchoredPosition = pos;
        scrollRectComp.sizeDelta = new Vector2(w, h);

        var viewport = new GameObject("Viewport");
        viewport.transform.SetParent(scrollObj.transform, false);
        var viewportImg = viewport.AddComponent<Image>();
        viewportImg.color = new Color(0, 0, 0, 0.5f);
        viewportImg.raycastTarget = true;
        var viewportRect = viewport.GetComponent<RectTransform>();
        viewportRect.anchorMin = Vector2.zero; viewportRect.anchorMax = Vector2.one;
        viewportRect.offsetMin = Vector2.zero; viewportRect.offsetMax = Vector2.zero;
        viewport.AddComponent<Mask>().showMaskGraphic = true;

        var content = new GameObject("Content");
        content.transform.SetParent(viewport.transform, false);
        var cRect = content.AddComponent<RectTransform>();
        cRect.anchorMin = new Vector2(0.5f, 1);
        cRect.anchorMax = new Vector2(0.5f, 1);
        cRect.pivot = new Vector2(0.5f, 1);
        cRect.sizeDelta = new Vector2(w, 0);
        cRect.anchoredPosition = Vector2.zero;

        scrollRect.content = cRect;
        scrollRect.viewport = viewportRect;
        scrollRect.horizontal = false; scrollRect.vertical = true;
        scrollRect.movementType = ScrollRect.MovementType.Clamped;

        contentOut = content.transform;
        contentRectOut = cRect;
    }

    private static void RefreshPointListUI()
    {
        RefreshSingleList(_camPointListContent, ActiveTrack, false);
        RefreshSingleList(_personPointListContent, PersonTrack, true);
    }

    private static void RefreshSingleList(Transform content, OrbitTrack track, bool isPerson)
    {
        if (content == null || track == null) return;
        var rows = isPerson ? _personRows : _cameraRows;
        while (rows.Count < track.Points.Count) rows.Add(CreatePointRow(content));
        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            bool visible = i < track.Points.Count;
            row.Root.SetActive(visible);
            row.Root.transform.SetSiblingIndex(i);
            if (!visible) { row.Owner = null; row.Point = null; continue; }
            var rect = row.Root.GetComponent<RectTransform>();
            rect.anchoredPosition = new Vector2(0, -15f - i * 34f);
            row.Root.GetComponent<Image>().color = new Color(1, 1, 1, 0);
            var point = track.Points[i];
            // Keep unfinished coordinate edits when recording appends another point.
            if (row.Owner != track || row.Point != point)
            {
                row.X.text = point.Pos.x.ToString("R");
                row.Y.text = point.Pos.y.ToString("R");
                row.Z.text = point.Pos.z.ToString("R");
                row.Action.text = point.ActionID.ToString();
            }
            row.Owner = track;
            row.Point = point;
        }
        content.GetComponent<RectTransform>().sizeDelta = new Vector2(0, track.Points.Count * 34f + 10f);
    }

    private static PointRow CreatePointRow(Transform content)
    {
        var row = new PointRow { Root = new GameObject("OrbitPointRow") };
        row.Root.transform.SetParent(content, false);
        row.Root.AddComponent<Image>().color = new Color(1, 1, 1, 0);
        var rect = row.Root.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1);
        rect.pivot = new Vector2(0.5f, 1);
        rect.sizeDelta = new Vector2(300, 30);
        var layout = row.Root.AddComponent<HorizontalLayoutGroup>();
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = layout.childForceExpandHeight = false;
        layout.spacing = 4;
        layout.childAlignment = TextAnchor.MiddleCenter;
        var handle = CreateButton(row.Root.transform, "≡", Vector2.zero, () => { }, 30, 24);
        handle.name = "DragHandle";
        handle.GetComponent<Image>().color = new Color(0.4f, 0.4f, 0.4f);
        handle.GetComponent<Button>().onClick.RemoveAllListeners();
        row.X = CreateListInputField(row.Root.transform, 42, "0");
        row.Y = CreateListInputField(row.Root.transform, 42, "0");
        row.Z = CreateListInputField(row.Root.transform, 42, "0");
        row.Action = CreateListInputField(row.Root.transform, 42, "0");
        CreateButton(row.Root.transform, "改", Vector2.zero, () =>
        {
            if (row.Owner == null || row.Point == null || !row.Owner.Points.Contains(row.Point)) return;
            if (!float.TryParse(row.X.text, out float x) || !float.TryParse(row.Y.text, out float y) ||
                !float.TryParse(row.Z.text, out float z) || !IsFinite(x) || !IsFinite(y) || !IsFinite(z) ||
                !int.TryParse(row.Action.text, out int action))
            { MelonLogger.Warning("[Orbit] 请输入有效坐标和动作编号"); return; }
            row.Point.Pos = new Vector3(x, y, z);
            row.Point.ActionID = action;
            RefreshTrackVisuals(row.Owner);
        }, 48, 24);
        var delete = CreateButton(row.Root.transform, "删", Vector2.zero, () =>
        {
            if (row.Owner == null || row.Point == null) return;
            int index = row.Owner.Points.IndexOf(row.Point);
            if (index >= 0) RemoveTrackPoint(row.Owner, index);
        }, 48, 24);
        delete.GetComponent<Image>().color = new Color(0.6f, 0.1f, 0.1f);
        return row;
    }

    private static void UpdateUI()
    {
        if (_infoText == null) return;
        string playState = IsCameraControlEnabled ? "<color=green>相机接管中</color>" : "<color=red>已交还游戏</color>";
        if (IsCameraControlEnabled)
        {
            playState += IsGlobalPlaying ? " | 轨道播放" : " | 自由飞行";
            playState += IsPersonPlaying ? " | <color=yellow>人物运动中</color>" : "";
            playState += _isRecording ? " | <color=red>人物录制中...</color>" : "";
        }
        _infoText.text = playState;

        if (_trackNameText != null && ActiveTrack != null)
        {
            _trackNameText.text = $"相机轨道: {ActiveTrack.Name} ({_currentTrackIndex + 1}/{Tracks.Count})";
        }
    }

    private static void PlayEmotion(int id)
    {
        if (TipMsgCore.i != null && TipMsgCore.i.mainplayer != null)
        {
            UnitEmotionSystem.Instance.PlayEmotionAction(TipMsgCore.i.mainplayer.GetComponent<Unit>(), id, true);
            MelonLogger.Msg($"[Orbit] 主角播放动作: {id}");
        }
    }

    private static void ShowTargetSelect()
    {
        if (_targetSelectPanel != null) UnityEngine.Object.Destroy(_targetSelectPanel);
        _targetSelectPanel = new GameObject("TargetSelectPanel");
        _targetSelectPanel.transform.SetParent(_canvasObj.transform, false);
        var img = _targetSelectPanel.AddComponent<Image>();
        img.color = new Color(0.05f, 0.05f, 0.05f, 0.95f);
        var rect = _targetSelectPanel.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f); rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f); rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = new Vector2(300, 400);

        CreateText(_targetSelectPanel.transform, "Title", "选择观看目标", 18, Color.white, new Vector2(0, -20), new Vector2(280, 30)).fontStyle = FontStyle.Bold;

        var scrollObj = new GameObject("Scroll");
        scrollObj.transform.SetParent(_targetSelectPanel.transform, false);
        var scrollRect = scrollObj.AddComponent<ScrollRect>();
        var sRect = scrollObj.GetComponent<RectTransform>();
        sRect.anchorMin = new Vector2(0.5f, 1); sRect.anchorMax = new Vector2(0.5f, 1);
        sRect.pivot = new Vector2(0.5f, 1); sRect.anchoredPosition = new Vector2(0, -60); sRect.sizeDelta = new Vector2(260, 280);

        var viewport = new GameObject("Viewport");
        viewport.transform.SetParent(scrollObj.transform, false);
        var viewImg = viewport.AddComponent<Image>(); viewImg.color = new Color(0, 0, 0, 0.5f);
        var vRect = viewport.GetComponent<RectTransform>();
        vRect.anchorMin = Vector2.zero; vRect.anchorMax = Vector2.one;
        vRect.offsetMin = Vector2.zero; vRect.offsetMax = Vector2.zero;
        viewport.AddComponent<Mask>().showMaskGraphic = true;

        var content = new GameObject("Content");
        content.transform.SetParent(viewport.transform, false);
        var cRect = content.AddComponent<RectTransform>();
        cRect.anchorMin = new Vector2(0, 1); cRect.anchorMax = new Vector2(1, 1);
        cRect.pivot = new Vector2(0.5f, 1); cRect.sizeDelta = new Vector2(0, 0);

        var layout = content.AddComponent<VerticalLayoutGroup>();
        layout.childControlWidth = true; layout.childControlHeight = true;
        layout.childForceExpandWidth = false; layout.childForceExpandHeight = false;
        layout.spacing = 5; layout.padding = new RectOffset(5, 5, 5, 5);
        var fitter = content.AddComponent<ContentSizeFitter>(); fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        scrollRect.content = cRect; scrollRect.viewport = vRect;
        scrollRect.horizontal = false; scrollRect.vertical = true;

        PopulateTargetList(content.transform);

        var closeBtn = CreateButton(_targetSelectPanel.transform, "关闭", new Vector2(0, -360), () => { UnityEngine.Object.Destroy(_targetSelectPanel); }, 200, 35);
        closeBtn.GetComponent<Image>().color = new Color(0.7f, 0.2f, 0.2f);
    }

    private static void PopulateTargetList(Transform contentTransform)
    {
        if (contentTransform == null) return;
        for (int i = contentTransform.childCount - 1; i >= 0; i--)
            UnityEngine.Object.Destroy(contentTransform.GetChild(i).gameObject);

        int count = 0;
        try
        {
            if (TipMsgCore.i != null && TipMsgCore.i.mainplayer != null)
            {
                CreateTargetButton(contentTransform, "自己 (MainPlayer)", TipMsgCore.i.mainplayer.transform);
                count++;
            }

            if (TipMsgCore.i != null && TipMsgCore.i.players != null)
            {
                List<Transform> transforms = new List<Transform>();
                List<string> names = new List<string>();

                foreach (var kvp in TipMsgCore.i.players)
                {
                    if (kvp.Value != null)
                    {
                        if (TipMsgCore.i.mainplayer != null && kvp.Value == TipMsgCore.i.mainplayer.transform) continue;
                        names.Add(kvp.Key); transforms.Add(kvp.Value);
                    }
                }

                for (int i = 0; i < transforms.Count; i++)
                {
                    CreateTargetButton(contentTransform, names[i], transforms[i]);
                    count++;
                }
            }
        }
        catch (Exception ex) { MelonLogger.Error($"[Orbit] 读取字典发生异常: {ex.Message}"); }

        if (count == 0)
        {
            var hint = CreateText(contentTransform, "EmptyHint", "当前周围没有其他玩家", 14, Color.gray, Vector2.zero, new Vector2(240, 30));
            var le = hint.gameObject.AddComponent<LayoutElement>();
            le.minHeight = 30; le.preferredHeight = 30;
        }
    }

    private static void CreateTargetButton(Transform parent, string name, Transform target)
    {
        var btnObj = CreateButton(parent, name, Vector2.zero, () => {
            _selectedTarget = target;
            MelonLogger.Msg($"[Orbit] 已选择目标: {name}");
            UnityEngine.Object.Destroy(_targetSelectPanel);
        }, 240, 30);
        btnObj.GetComponent<Image>().color = new Color(0.3f, 0.3f, 0.3f);
    }

    public static void CreateTrack(string name)
    {
        if (Tracks.Count >= 128) { MelonLogger.Warning("[Orbit] 最多创建 128 条相机轨道"); return; }
        SetCameraPlayback(false);
        ResetDragState();
        var track = new OrbitTrack { Name = name };
        track.InitDefaultCurve();
        Tracks.Add(track);
        _currentTrackIndex = Tracks.Count - 1;
        RefreshTrackVisuals();
        RefreshPointListUI();
        UpdateUI();
        UpdateCurveUI();
        UpdateSpeedLimitText();
    }

    public static void SwitchTrack(int delta)
    {
        if (Tracks.Count == 0) return;
        SetCameraPlayback(false);
        ResetDragState();
        _currentTrackIndex = ((_currentTrackIndex + delta) % Tracks.Count + Tracks.Count) % Tracks.Count;
        RefreshTrackVisuals();
        RefreshPointListUI();
        UpdateUI();
        UpdateCurveUI();
        UpdateSpeedLimitText();
    }

    public static void AddPoint(bool isPerson = false)
    {
        if (!IsActive || (isPerson && TipMsgCore.i?.mainplayer == null) || (!isPerson && _cameraDummy == null)) return;
        var track = isPerson ? PersonTrack : ActiveTrack;
        if (track == null || track.Points.Count >= 4096) return;
        Vector3 pos = isPerson ? TipMsgCore.i.mainplayer.transform.position : _cameraDummy.transform.position;

        if (isPerson)
        {
            PersonTrack.Points.Add(new TrackPoint(pos));
            RefreshTrackVisuals(PersonTrack);
        }
        else
        {
            if (ActiveTrack == null) return;
            ActiveTrack.Points.Add(new TrackPoint(pos));
            RefreshTrackVisuals(ActiveTrack);
        }
        RefreshPointListUI();
        UpdateCurveUI();
    }

    public static void RemovePoint(bool isPerson, int index)
    {
        RemoveTrackPoint(isPerson ? PersonTrack : ActiveTrack, index);
    }

    private static void RemoveTrackPoint(OrbitTrack t, int index)
    {
        if (t == null || index < 0 || index >= t.Points.Count) return;
        t.Points.RemoveAt(index);
        RefreshTrackVisuals(t);
        RefreshPointListUI();
        UpdateCurveUI();
    }

    private static void RefreshTrackVisuals(OrbitTrack trackToRebuild = null)
    {
        if (!IsActive) return;
        if (trackToRebuild != null) { RefreshOneTrack(trackToRebuild); return; }
        foreach (var track in Tracks) RefreshOneTrack(track);
        RefreshOneTrack(PersonTrack);
    }

    private static void RefreshOneTrack(OrbitTrack track)
    {
        if (track.Points.Count == 0) { if (track.VisualRoot != null) track.VisualRoot.SetActive(false); return; }
        if (track.VisualRoot == null)
        {
            track.VisualRoot = new GameObject($"[Orbit] {track.Name}");
            var line = new GameObject("Line");
            line.transform.SetParent(track.VisualRoot.transform, false);
            track.Line = line.AddComponent<LineRenderer>();
            track.Line.useWorldSpace = true;
            track.Line.startWidth = track.Line.endWidth = 0.05f;
        }
        track.Line.sharedMaterial = GetVisualMaterial(track == PersonTrack ? new Color(1f, 0.5f, 0f, 0.5f)
            : track == ActiveTrack ? Color.cyan : new Color(0.5f, 0.5f, 0.5f, 0.5f));
        const int segments = 20;
        var positions = new Vector3[(track.Points.Count - 1) * segments + 1];
        int index = 0;
        for (int i = 0; i < track.Points.Count - 1; i++)
        {
            var p0 = track.Points[Mathf.Max(0, i - 1)].Pos;
            var p1 = track.Points[i].Pos;
            var p2 = track.Points[i + 1].Pos;
            var p3 = track.Points[Mathf.Min(track.Points.Count - 1, i + 2)].Pos;
            for (int j = 0; j < segments; j++) positions[index++] = CatmullRom(p0, p1, p2, p3, j / (float)segments);
        }
        positions[index] = track.Points[track.Points.Count - 1].Pos;
        track.Line.positionCount = positions.Length;
        track.Line.SetPositions(positions);
        while (track.Spheres.Count < track.Points.Count)
        {
            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name = "Point";
            sphere.transform.SetParent(track.VisualRoot.transform, false);
            sphere.transform.localScale = Vector3.one * 0.15f;
            var collider = sphere.GetComponent<Collider>();
            if (collider != null) UnityEngine.Object.Destroy(collider);
            track.Spheres.Add(sphere);
        }
        var pointMaterial = GetVisualMaterial(track == PersonTrack ? Color.magenta : track == ActiveTrack ? Color.red : Color.yellow);
        for (int i = 0; i < track.Spheres.Count; i++)
        {
            bool visible = i < track.Points.Count;
            track.Spheres[i].SetActive(visible);
            if (!visible) continue;
            track.Spheres[i].transform.position = track.Points[i].Pos;
            track.Spheres[i].GetComponent<Renderer>().sharedMaterial = pointMaterial;
        }
        track.VisualRoot.SetActive(IsTrackVisible);
    }

    private static void HandleUIDrag()
    {
        if (_canvasObj == null) return;
        Vector2 mousePos = Input.mousePosition;
        float scale = _canvasObj.GetComponent<Canvas>().scaleFactor;

        if (_panelObj != null && _panelObj.activeSelf)
        {
            RectTransform panelRect = _panelObj.GetComponent<RectTransform>();
            RectTransformUtility.ScreenPointToLocalPointInRectangle(panelRect, mousePos, null, out Vector2 localPoint);
            bool isOverTitleBar = localPoint.x >= 0 && localPoint.x <= panelRect.sizeDelta.x && localPoint.y <= 0 && localPoint.y >= -40;

            if (Input.GetMouseButtonDown(0) && isOverTitleBar)
            {
                _isDraggingPanel = true;
                _panelDragOffset = mousePos;
            }

            if (Input.GetMouseButton(0) && _isDraggingPanel)
            {
                Vector2 delta = mousePos - _panelDragOffset;
                panelRect.anchoredPosition += delta / scale;
                _panelDragOffset = mousePos;
            }
        }

        if (_floatingBallObj != null && _floatingBallObj.activeSelf)
        {
            RectTransform ballRect = _floatingBallObj.GetComponent<RectTransform>();
            bool isOverBall = RectTransformUtility.RectangleContainsScreenPoint(ballRect, mousePos, null);

            if (Input.GetMouseButtonDown(0) && isOverBall)
            {
                _isDraggingBall = true;
                _ballDragOffset = mousePos;
                _ballPointerDownPos = mousePos;
            }

            if (Input.GetMouseButton(0) && _isDraggingBall)
            {
                Vector2 delta = mousePos - _ballDragOffset;
                ballRect.anchoredPosition += delta / scale;
                _ballDragOffset = mousePos;
            }

            if (Input.GetMouseButtonUp(0) && _isDraggingBall)
            {
                _isDraggingBall = false;
                if (Vector2.Distance(mousePos, _ballPointerDownPos) < 10f)
                {
                    _floatingBallObj.SetActive(false);
                    _panelObj.SetActive(true);
                }
            }
        }

        if (Input.GetMouseButtonUp(0)) _isDraggingPanel = false;
    }

    private static bool IsPointerOverUI()
    {
        Vector2 mousePos = Input.mousePosition;
        if (_panelObj != null && _panelObj.activeSelf)
        {
            if (RectTransformUtility.RectangleContainsScreenPoint(_panelObj.GetComponent<RectTransform>(), mousePos, null)) return true;
        }
        if (_floatingBallObj != null && _floatingBallObj.activeSelf)
        {
            if (RectTransformUtility.RectangleContainsScreenPoint(_floatingBallObj.GetComponent<RectTransform>(), mousePos, null)) return true;
        }
        if (_targetSelectPanel != null && _targetSelectPanel.activeSelf)
        {
            if (RectTransformUtility.RectangleContainsScreenPoint(_targetSelectPanel.GetComponent<RectTransform>(), mousePos, null)) return true;
        }
        return false;
    }

    private static Text CreateText(Transform parent, string name, string content, int fontSize, Color color, Vector2 pos, Vector2 size)
    {
        var obj = new GameObject(name);
        obj.transform.SetParent(parent, false);
        var txt = obj.AddComponent<Text>();
        txt.text = content; txt.fontSize = fontSize; txt.color = color;
        txt.alignment = TextAnchor.MiddleCenter;
        txt.font = Resources.GetBuiltinResource<Font>("Arial.ttf");

        // 【核心修复】禁止文字拦截鼠标点击
        txt.raycastTarget = false;

        var rect = txt.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 1); rect.anchorMax = new Vector2(0.5f, 1);
        rect.pivot = new Vector2(0.5f, 1);
        rect.anchoredPosition = pos; rect.sizeDelta = size;
        return txt;
    }

    private static GameObject CreateButton(Transform parent, string label, Vector2 pos, Action onClick, float width = 400, float height = 40)
    {
        var obj = new GameObject("Btn_" + label);
        obj.transform.SetParent(parent, false);
        var img = obj.AddComponent<Image>();
        img.color = new Color(0.2f, 0.2f, 0.2f, 0.9f);
        var btn = obj.AddComponent<Button>();

        btn.onClick.AddListener(new Action(() => {
            onClick?.Invoke();
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
        }));

        var rect = obj.GetComponent<RectTransform>();

        if (parent.GetComponent<HorizontalLayoutGroup>() != null || parent.GetComponent<VerticalLayoutGroup>() != null)
        {
            var layout = obj.AddComponent<LayoutElement>();
            layout.minWidth = width;
            layout.preferredWidth = width;
            layout.flexibleWidth = 0;
            layout.minHeight = height;
            layout.preferredHeight = height;
            layout.flexibleHeight = 0;
        }
        else
        {
            rect.anchorMin = new Vector2(0.5f, 1); rect.anchorMax = new Vector2(0.5f, 1);
            rect.pivot = new Vector2(0.5f, 1);
            rect.anchoredPosition = pos; rect.sizeDelta = new Vector2(width, height);
        }

        var txtObj = new GameObject("Text");
        txtObj.transform.SetParent(obj.transform, false);
        var txt = txtObj.AddComponent<Text>();
        txt.text = label; txt.fontSize = Mathf.Clamp((int)(height * 0.4f), 12, 18);
        txt.color = Color.white; txt.alignment = TextAnchor.MiddleCenter;
        txt.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        txt.raycastTarget = false;

        var txtRect = txtObj.GetComponent<RectTransform>();
        txtRect.anchorMin = Vector2.zero; txtRect.anchorMax = Vector2.one;
        txtRect.offsetMin = Vector2.zero; txtRect.offsetMax = Vector2.zero;
        return obj;
    }

    private static InputField CreateListInputField(Transform parent, float width, string defaultText)
    {
        var obj = new GameObject("InputField");
        obj.transform.SetParent(parent, false);

        var layout = obj.AddComponent<LayoutElement>();
        layout.minWidth = width;
        layout.preferredWidth = width;
        layout.flexibleWidth = 0;

        layout.minHeight = 24;
        layout.preferredHeight = 24;
        layout.flexibleHeight = 0;

        var img = obj.AddComponent<Image>();
        img.color = new Color(0.3f, 0.3f, 0.3f, 0.9f);

        var input = obj.AddComponent<InputField>();

        var textObj = new GameObject("Text");
        textObj.transform.SetParent(obj.transform, false);
        var text = textObj.AddComponent<Text>();
        text.text = defaultText; text.fontSize = 13; text.color = Color.white;
        text.alignment = TextAnchor.MiddleCenter;
        text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        text.raycastTarget = false;

        var textRect = textObj.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero; textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero; textRect.offsetMax = Vector2.zero;

        input.textComponent = text;
        input.text = defaultText;
        return input;
    }

    // 【修复】将返回类型改为 Slider
    private static Slider CreateSlider(Transform parent, Vector2 pos, float min, float max, float val, Action<float> onChanged, float width = 400)
    {
        var obj = new GameObject("Slider");
        obj.transform.SetParent(parent, false);
        var rect = obj.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 1); rect.anchorMax = new Vector2(0.5f, 1);
        rect.pivot = new Vector2(0.5f, 1);
        rect.anchoredPosition = pos; rect.sizeDelta = new Vector2(width, 20);

        var slider = obj.AddComponent<Slider>();
        var bg = new GameObject("Background").AddComponent<Image>();
        bg.transform.SetParent(obj.transform, false);
        bg.color = new Color(0.1f, 0.1f, 0.1f);
        var bgRect = bg.GetComponent<RectTransform>();
        bgRect.anchorMin = new Vector2(0, 0.25f); bgRect.anchorMax = new Vector2(1, 0.75f);
        bgRect.offsetMin = Vector2.zero; bgRect.offsetMax = Vector2.zero;

        var fillArea = new GameObject("FillArea").AddComponent<RectTransform>();
        fillArea.SetParent(obj.transform, false);
        fillArea.anchorMin = new Vector2(0, 0.25f); fillArea.anchorMax = new Vector2(1, 0.75f);
        fillArea.offsetMin = new Vector2(5, 0); fillArea.offsetMax = new Vector2(-15, 0);

        var fill = new GameObject("Fill").AddComponent<Image>();
        fill.transform.SetParent(fillArea, false);
        fill.color = new Color(0.2f, 0.6f, 1f);
        var fillRect = fill.GetComponent<RectTransform>();
        fillRect.anchorMin = Vector2.zero; fillRect.anchorMax = Vector2.one;
        fillRect.offsetMin = Vector2.zero; fillRect.offsetMax = Vector2.zero;

        slider.fillRect = fillRect;
        slider.minValue = min; slider.maxValue = max; slider.value = val;
        slider.onValueChanged.AddListener(new Action<float>(onValue => { onChanged(onValue); }));

        var handleArea = new GameObject("HandleArea").AddComponent<RectTransform>();
        handleArea.SetParent(obj.transform, false);
        handleArea.anchorMin = Vector2.zero; handleArea.anchorMax = Vector2.one;
        handleArea.offsetMin = new Vector2(10, 0); handleArea.offsetMax = new Vector2(-10, 0);

        var handle = new GameObject("Handle").AddComponent<Image>();
        handle.transform.SetParent(handleArea, false);
        handle.color = Color.white;
        var handleRect = handle.GetComponent<RectTransform>();
        handleRect.sizeDelta = new Vector2(10, 20);

        slider.handleRect = handleRect;
        slider.targetGraphic = handle;

        // 【修复】返回 slider 组件
        return slider;
    }

    private static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
    {
        float t2 = t * t;
        float t3 = t2 * t;
        return 0.5f * (
            (2f * p1) + (-p0 + p2) * t +
            (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 +
            (-p0 + 3f * p1 - 3f * p2 + p3) * t3
        );
    }
}