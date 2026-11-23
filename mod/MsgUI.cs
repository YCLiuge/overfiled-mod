using MelonLoader;
using mod;
using mod.st;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class MsgUI
{
    public static MsgUI i = new();
    MsgUI() { if (i == null) i = this; }
    // UI引用
    private GameObject _consoleRoot;
    private ScrollRect _logScrollRect;
    private RectTransform _logContentRect;
    private RectTransform _logViewportRect;
    private Text _logText; // 你已有的，保留
    private RectTransform _logAreaRect; // 用于判断鼠标是否在日志区域
    private InputField _inputField;

    // 命令存储 key=指令名, value=回调
    private readonly Dictionary<string, System.Action<string[]>> _commands = new();
    private bool _consoleVisible;

    // 日志缓存
   // private readonly StringBuilder _logBuilder = new();
    private Queue<string> _logQueue = new Queue<string>();
    private const int MaxLogLines = 200;

    public void OnInitializeMelon()
    {
        // 注册内置命令
        RegisterCommand("help", Cmd_Help);
        RegisterCommand("clear", Cmd_Clear);
        RegisterCommand("print", Cmd_Print);

        MelonLogger.Msg("[控制台] Mod加载完成，按Alt + 9 打开/关闭控制台");
    }

    public void OnUpdate()
    {
        //// F9 开关控制台
        bool a = Input.GetKey(KeyCode.LeftAlt);
        if (Input.GetKeyDown(KeyCode.Alpha9)&&a)
        {
            ToggleConsole();
        }
        UpdateConsoleDrag();
        UpdateConsoleScroll();
    }

    #region 控制台创建
    private void ToggleConsole()
    {
        if (_consoleRoot == null)
        {
            CreateConsoleUI();
        }

        _consoleVisible = !_consoleVisible;
        _consoleRoot.SetActive(_consoleVisible);

        if (_consoleVisible && _inputField != null)
        {
            _inputField.ActivateInputField();
        }
    }

    // ---------- 位置控制字段（类成员） ----------
    private float _consoleAnchorX = 0.02f;   // 水平位置（0=左, 1=右）
    private float _consoleAnchorY = 0.02f;   // 垂直位置（0=下, 1=上）
    private float _consoleWidth = 0.43f;    // 面板宽度
    private float _consoleHeight = 0.53f;    // 面板高度
    // ---------- 字段 ----------
    private static bool _isDragging = false;
    private Vector2 _dragStartMouse;
    private Vector2 _dragStartAnchorMin;
    private Vector2 _dragStartAnchorMax;
    private RectTransform _canvasRect;
    private void CreateConsoleUI()
    {
        // 创建Canvas
        var canvasObj = new GameObject("ConsoleCanvas");
        var canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvasObj.AddComponent<CanvasScaler>();
        canvasObj.AddComponent<GraphicRaycaster>();
        UnityEngine.Object.DontDestroyOnLoad(canvasObj);

        // 根面板
        _consoleRoot = new GameObject("ConsolePanel");
        _consoleRoot.transform.SetParent(canvasObj.transform, false);
        var rect = _consoleRoot.AddComponent<RectTransform>();

        // ---------- 关键：设置高排序值 ----------
        canvas.overrideSorting = true;       // 强制参与排序
        canvas.sortingOrder = 9999;          // 数值越大越靠上
        canvas.sortingLayerID = 0;           // 0 = Default 层（通常够用）

        // ---------- 使用位置和尺寸字段 ----------
        rect.anchorMin = new Vector2(_consoleAnchorX, _consoleAnchorY);
        rect.anchorMax = new Vector2(_consoleAnchorX + _consoleWidth, _consoleAnchorY + _consoleHeight);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        var panelImage = _consoleRoot.AddComponent<Image>();
        panelImage.color = new Color(0, 0, 0, 0.75f);

        // 日志文本区域
        GameObject logObj = new GameObject("LogText");
        logObj.transform.SetParent(_consoleRoot.transform, false);
        var logRect = logObj.AddComponent<RectTransform>();
        logRect.anchorMin = new Vector2(0.02f, 0.12f);
        logRect.anchorMax = new Vector2(0.98f, 0.98f);
        logRect.offsetMin = Vector2.zero;
        logRect.offsetMax = Vector2.zero;

        // ---------- 日志区域（带 ScrollRect + Mask） ----------
        GameObject logArea = new GameObject("LogArea");
        logArea.transform.SetParent(_consoleRoot.transform, false);
        _logAreaRect = logArea.AddComponent<RectTransform>();
        _logAreaRect.anchorMin = new Vector2(0.02f, 0.12f);
        _logAreaRect.anchorMax = new Vector2(0.98f, 0.98f);
        _logAreaRect.offsetMin = Vector2.zero;
        _logAreaRect.offsetMax = Vector2.zero;

        // ScrollRect
        _logScrollRect = logArea.AddComponent<ScrollRect>();
        _logScrollRect.horizontal = false;
        _logScrollRect.vertical = true;
        _logScrollRect.movementType = ScrollRect.MovementType.Clamped;
        _logScrollRect.scrollSensitivity = 30f;

        // Viewport（带 Mask，负责裁剪）
        GameObject viewport = new GameObject("Viewport");
        viewport.transform.SetParent(logArea.transform, false);
        _logViewportRect = viewport.AddComponent<RectTransform>();
        _logViewportRect.anchorMin = Vector2.zero;
        _logViewportRect.anchorMax = Vector2.one;
        _logViewportRect.offsetMin = Vector2.zero;
        _logViewportRect.offsetMax = Vector2.zero;

        viewport.AddComponent<Mask>().showMaskGraphic = false; // 不显示遮罩本身
        var vpImage = viewport.AddComponent<Image>();
        vpImage.color = new Color(0, 0, 0, 0.01f); // 需要 Image 才能被 Mask 使用

        // Content（文本容器，会随文本增长）
        GameObject content = new GameObject("Content");
        content.transform.SetParent(viewport.transform, false);
        _logContentRect = content.AddComponent<RectTransform>();
        _logContentRect.anchorMin = new Vector2(0, 1);
        _logContentRect.anchorMax = new Vector2(1, 1);
        _logContentRect.pivot = new Vector2(0.5f, 1);
        _logContentRect.offsetMin = new Vector2(0, 0);
        _logContentRect.offsetMax = new Vector2(0, 0);

        // Text 组件
        _logText = content.AddComponent<Text>();
        _logText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        _logText.fontSize = 14;
        _logText.color = Color.green;
        _logText.verticalOverflow = VerticalWrapMode.Overflow;
        _logText.horizontalOverflow = HorizontalWrapMode.Wrap;
        _logText.alignment = TextAnchor.UpperLeft;

        // ContentSizeFitter 让 Content 高度随文本增长
        var fitter = content.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // 绑定 ScrollRect
        _logScrollRect.viewport = _logViewportRect;
        _logScrollRect.content = _logContentRect;

        // 输入框
        GameObject inputObj = new GameObject("InputField");
        inputObj.transform.SetParent(_consoleRoot.transform, false);
        var inputRect = inputObj.AddComponent<RectTransform>();
        inputRect.anchorMin = new Vector2(0.02f, 0.02f);
        inputRect.anchorMax = new Vector2(0.98f, 0.10f);
        inputRect.offsetMin = Vector2.zero;
        inputRect.offsetMax = Vector2.zero;

        var inputBg = inputObj.AddComponent<Image>();
        inputBg.color = new Color(0.15f, 0.15f, 0.15f);

        _inputField = inputObj.AddComponent<InputField>();
        var textCompObj = new GameObject("Text");
        textCompObj.transform.SetParent(inputObj.transform, false);
        var textRect = textCompObj.AddComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(8, 2);
        textRect.offsetMax = new Vector2(-8, -2);

        var inputText = textCompObj.AddComponent<Text>();
        inputText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        inputText.fontSize = 14;
        inputText.color = Color.white;
        _inputField.textComponent = inputText;

        var placeholderObj = new GameObject("Placeholder");
        placeholderObj.transform.SetParent(inputObj.transform, false);
        var placeRect = placeholderObj.AddComponent<RectTransform>();
        placeRect.anchorMin = Vector2.zero;
        placeRect.anchorMax = Vector2.one;
        placeRect.offsetMin = new Vector2(8, 2);
        placeRect.offsetMax = new Vector2(-8, -2);

        var placeText = placeholderObj.AddComponent<Text>();
        placeText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        placeText.text = "输入命令回车执行...";
        placeText.fontSize = 14;
        placeText.color = new Color(0.6f, 0.6f, 0.6f);
        _inputField.placeholder = placeText;

        // 回车提交事件
        _inputField.onEndEdit.AddListener((UnityEngine.Events.UnityAction<string>)OnCommandSubmit);

        // ---------- 拖动相关 ----------
        _canvasRect = canvasObj.GetComponent<RectTransform>();
        AddDragHandler(_consoleRoot);

        _consoleRoot.SetActive(false);
        Log("[系统] 控制台创建完成 | F12打开");
    }

    // ---------- 提供外部调整位置的方法 ----------
    public void SetConsolePosition(float anchorX, float anchorY)
    {
        _consoleAnchorX = Mathf.Clamp01(anchorX);
        _consoleAnchorY = Mathf.Clamp01(anchorY);
        UpdateConsoleRect();
    }

    public void SetConsoleSize(float width, float height)
    {
        _consoleWidth = Mathf.Clamp01(width);
        _consoleHeight = Mathf.Clamp01(height);
        UpdateConsoleRect();
    }

    public void MoveConsole(float deltaX, float deltaY)
    {
        _consoleAnchorX = Mathf.Clamp01(_consoleAnchorX + deltaX);
        _consoleAnchorY = Mathf.Clamp01(_consoleAnchorY + deltaY);
        UpdateConsoleRect();
    }

    private void UpdateConsoleRect()
    {
        if (_consoleRoot == null) return;
        var rect = _consoleRoot.GetComponent<RectTransform>();
        if (rect == null) return;
        rect.anchorMin = new Vector2(_consoleAnchorX, _consoleAnchorY);
        rect.anchorMax = new Vector2(
            Mathf.Clamp01(_consoleAnchorX + _consoleWidth),
            Mathf.Clamp01(_consoleAnchorY + _consoleHeight));
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private void AddDragHandler(GameObject target)
    {
        var trigger = target.GetComponent<EventTrigger>();
        if (trigger == null)
            trigger = target.AddComponent<EventTrigger>();

        var beginEntry = new EventTrigger.Entry();
        beginEntry.eventID = EventTriggerType.BeginDrag;
        beginEntry.callback.AddListener((UnityEngine.Events.UnityAction<BaseEventData>)(OnBeginDrag));
        trigger.triggers.Add(beginEntry);

        var dragEntry = new EventTrigger.Entry();
        dragEntry.eventID = EventTriggerType.Drag;
        dragEntry.callback.AddListener((UnityEngine.Events.UnityAction<BaseEventData>)(OnDrag));
        trigger.triggers.Add(dragEntry);

        var endEntry = new EventTrigger.Entry();
        endEntry.eventID = EventTriggerType.EndDrag;
        endEntry.callback.AddListener((UnityEngine.Events.UnityAction<BaseEventData>)(OnEndDrag));
        trigger.triggers.Add(endEntry);
    }

    private void OnBeginDrag(BaseEventData data)
    {
        //MelonLogger.Msg("[拖动] OnBeginDrag 触发");
        if (_consoleRoot == null) return;

        var rect = _consoleRoot.GetComponent<RectTransform>();
        if (rect == null) return;

        _isDragging = true;
        _dragStartMouse = Input.mousePosition;   // ← 用 Input，不用 pointerData
        _dragStartAnchorMin = rect.anchorMin;
        _dragStartAnchorMax = rect.anchorMax;

        //MelonLogger.Msg($"[拖动] 记录起点 mouse={_dragStartMouse}, anchorMin={_dragStartAnchorMin}");
    }
    private void OnDrag(BaseEventData data)
    {
        //MelonLogger.Msg("[拖动] OnDrag 触发");
        if (!_isDragging || _consoleRoot == null) return;

        var rect = _consoleRoot.GetComponent<RectTransform>();
        if (rect == null) return;

        Vector2 delta = (Vector2)Input.mousePosition - _dragStartMouse;  // ← 用 Input
        float deltaX = delta.x / Screen.width;
        float deltaY = delta.y / Screen.height;

        float width = _dragStartAnchorMax.x - _dragStartAnchorMin.x;
        float height = _dragStartAnchorMax.y - _dragStartAnchorMin.y;

        float newMinX = Mathf.Clamp(_dragStartAnchorMin.x + deltaX, 0f, 1f - width);
        float newMinY = Mathf.Clamp(_dragStartAnchorMin.y + deltaY, 0f, 1f - height);

       // MelonLogger.Msg($"[拖动] delta={delta}, 新锚点=({newMinX:F3},{newMinY:F3})");

        rect.anchorMin = new Vector2(newMinX, newMinY);
        rect.anchorMax = new Vector2(newMinX + width, newMinY + height);
    }
    private void OnEndDrag(BaseEventData data)
    {
        _isDragging = false;
    }
    #endregion

    #region 命令处理
    private void OnCommandSubmit(string inputStr)
    {
        if (string.IsNullOrWhiteSpace(inputStr)) return;

        TipMsgCore.i.command(TipMsgCore.i.mainplayername, inputStr,Il2Cpp.UI_Chat_Define.eMessageType.Text);
        TipMsgCore.i.updatecommand(TipMsgCore.i.mainplayername, inputStr);

        Log($"> {inputStr}");
        var parts = inputStr.Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
        string cmd = parts[0].ToLower();
        string[] args = parts.Length > 1 ? parts[1..] : System.Array.Empty<string>();

        if (_commands.ContainsKey(cmd))
        {
            try
            {
                _commands[cmd].Invoke(args);
            }
            catch (System.Exception e)
            {
                //LogError($"命令异常: {e.Message}");
            }
        }
        _inputField.text = string.Empty;
        // 保持输入框焦点
        _inputField.ActivateInputField();
    }

    /// <summary>注册自定义控制台命令</summary>
    public void RegisterCommand(string name, System.Action<string[]> callback)
    {
        _commands[name.ToLower()] = callback;
    }
    #endregion

    #region 内置命令实现
    private void Cmd_Help(string[] args)
    {
        Log("===可用命令列表===");
        foreach (var kv in _commands)
        {
            Log($"- {kv.Key}");
        }
        foreach (var kv in Tips.a)
        {
            Log($"- {kv.tip}:{kv.js}");
        }
    }

    private void Cmd_Clear(string[] args)
    {
        //_logBuilder.Clear();
        //RefreshLogDisplay();
    }

    private void Cmd_Print(string[] args)
    {
        Log(string.Join(" ", args));
    }
    #endregion

    #region 日志输出封装
    public void Log(string msg)
    {
        _logQueue.Enqueue(msg);
        while (_logQueue.Count > MaxLogLines)
            _logQueue.Dequeue();

        if (_logText != null)
            _logText.text = string.Join("\n", _logQueue);

        MelonCoroutines.Start(ScrollToBottomNextFrame());
    }
    private System.Collections.IEnumerator ScrollToBottomNextFrame()
    {
        yield return null; // 等待一帧，让布局刷新
        if (_logScrollRect != null)
            _logScrollRect.verticalNormalizedPosition = 0f; // 0 = 底部
    }
    private void UpdateConsoleDrag()
    {
        if (_consoleRoot == null || !_consoleRoot.activeSelf)
        {
            _isDragging = false;
            return;
        }

        var rect = _consoleRoot.GetComponent<RectTransform>();
        if (rect == null) return;

        // 获取 Canvas 的相机（ScreenSpaceOverlay 时为 null）
        var canvas = _consoleRoot.GetComponentInParent<Canvas>();
        Camera uiCam = (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
                       ? canvas.worldCamera : null;

        // ---------- 1. 鼠标按下：判断是否点在面板范围内 ----------
        if (Input.GetMouseButtonDown(0) && !_isDragging)
        {
            // RectangleContainsScreenPoint 会考虑 CanvasScaler 缩放和子区域
            if (RectTransformUtility.RectangleContainsScreenPoint(rect, Input.mousePosition, uiCam))
            {
                _isDragging = true;
                _dragStartMouse = Input.mousePosition;
                _dragStartAnchorMin = rect.anchorMin;
                _dragStartAnchorMax = rect.anchorMax;
                MelonLogger.Msg("[拖动] 开始");
            }
        }

        // ---------- 2. 拖动中 ----------
        if (_isDragging && Input.GetMouseButton(0))
        {
            Vector2 delta = (Vector2)Input.mousePosition - _dragStartMouse;
            float deltaX = delta.x / Screen.width;
            float deltaY = delta.y / Screen.height;

            float width = _dragStartAnchorMax.x - _dragStartAnchorMin.x;
            float height = _dragStartAnchorMax.y - _dragStartAnchorMin.y;

            float newMinX = Mathf.Clamp(_dragStartAnchorMin.x + deltaX, 0f, 1f - width);
            float newMinY = Mathf.Clamp(_dragStartAnchorMin.y + deltaY, 0f, 1f - height);

            rect.anchorMin = new Vector2(newMinX, newMinY);
            rect.anchorMax = new Vector2(newMinX + width, newMinY + height);
        }

        // ---------- 3. 鼠标松开 ----------
        if (Input.GetMouseButtonUp(0) && _isDragging)
        {
            _isDragging = false;
            MelonLogger.Msg("[拖动] 结束");
        }
    }
    private void UpdateConsoleScroll()
    {
        if (_consoleRoot == null || !_consoleRoot.activeSelf) return;
        if (_logScrollRect == null || _logAreaRect == null) return;

        float scroll = Input.mouseScrollDelta.y;
        if (scroll == 0f) return;

        // 判断鼠标是否在日志区域内
        Vector2 localPoint;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
            _logAreaRect, Input.mousePosition, null, out localPoint))
            return;

        if (!_logAreaRect.rect.Contains(localPoint)) return;

        // 调整滚动位置
        float current = _logScrollRect.verticalNormalizedPosition;
        float delta = scroll * 0.1f; // 每格滚轮滚动 10%
        _logScrollRect.verticalNormalizedPosition = Mathf.Clamp01(current + delta);
    }
    //public void LogError(string msg)
    //{
    //    AppendLine($"[ERROR] {msg}");
    //}

    //private void AppendLine(string line)
    //{
    //    _logBuilder.AppendLine(line);
    //    // 限制最大行数，防止内存暴涨
    //    var lines = _logBuilder.ToString().Split('\n');
    //    if (lines.Length > MaxLogLines)
    //    {
    //        _logBuilder.Clear();
    //        for (int i = lines.Length - MaxLogLines; i < lines.Length; i++)
    //        {
    //            if (!string.IsNullOrEmpty(lines[i]))
    //                _logBuilder.AppendLine(lines[i]);
    //        }
    //    }
    //    RefreshLogDisplay();
    //}

    //private void RefreshLogDisplay()
    //{
    //    if (_logText != null)
    //        _logText.text = _logBuilder.ToString();
    //}
    #endregion
}