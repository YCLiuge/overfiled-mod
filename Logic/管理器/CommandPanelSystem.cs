using Il2Cpp;
using MelonLoader;
using MelonLoader.Utils;
using mod;
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public static class CommandPanelSystem
{
    public static bool IsActive = false;

    private static GameObject _canvasObj;
    private static GameObject _panelObj;
    private static RectTransform _contentRect;

    private static List<GameObject> _rows = new List<GameObject>();

    // 拖拽状态
    private static bool _isDragging = false;
    private static Vector2 _dragOffset;

    private static string SaveDir => Path.Combine(MelonEnvironment.UserDataDirectory, "mod储存数据");
    private static string SavePath => Path.Combine(SaveDir, "commands.txt");

    public static void StartSystem()
    {
        if (IsActive) return;
        IsActive = true;
        BuildUI();
        LoadCommands();
    }

    public static void StopSystem()
    {
        if (!IsActive) return;
        IsActive = false;
        if (_canvasObj != null) UnityEngine.Object.Destroy(_canvasObj);
        _canvasObj = null;
        _panelObj = null;
        _contentRect = null;
        _rows.Clear();
    }

    public static void Tick()
    {
        if (!IsActive || _canvasObj == null || _panelObj == null) return;

        Vector2 mousePos = Input.mousePosition;
        RectTransform panelRect = _panelObj.GetComponent<RectTransform>();
        float scale = _canvasObj.GetComponent<Canvas>().scaleFactor;

        // 1. 全区域拖拽
        bool isOverPanel = RectTransformUtility.RectangleContainsScreenPoint(panelRect, mousePos, null);
        bool isOverInteractable = false;

        if (isOverPanel && EventSystem.current != null)
        {
            PointerEventData eventData = new PointerEventData(EventSystem.current);
            eventData.position = mousePos;
            var results = new Il2CppSystem.Collections.Generic.List<RaycastResult>();
            EventSystem.current.RaycastAll(eventData, results);

            foreach (var res in results)
            {
                if (res.gameObject.GetComponent<Button>() != null || res.gameObject.GetComponent<InputField>() != null)
                {
                    isOverInteractable = true;
                    break;
                }
            }
        }

        if (Input.GetMouseButtonDown(0) && isOverPanel && !isOverInteractable)
        {
            _isDragging = true;
            _dragOffset = mousePos;
        }

        if (Input.GetMouseButton(0) && _isDragging)
        {
            Vector2 delta = mousePos - _dragOffset;
            panelRect.anchoredPosition += delta / scale;
            _dragOffset = mousePos;
        }

        if (Input.GetMouseButtonUp(0)) _isDragging = false;

        // 2. 滚轮滑动列表逻辑
        if (isOverPanel && !_isDragging)
        {
            float scroll = Input.mouseScrollDelta.y;
            float moveAmount = 0f;

            if (Mathf.Abs(scroll) > 0.01f)
                moveAmount = scroll * 100f;

            // 键盘兼容
            if (Input.GetKeyDown(KeyCode.DownArrow)) moveAmount = -50f;
            if (Input.GetKeyDown(KeyCode.UpArrow)) moveAmount = 50f;

            if (Mathf.Abs(moveAmount) > 0.01f)
            {
                ScrollList(moveAmount);
            }
        }
    }

    // ==========================================
    // 用户自己调好的完美数学逻辑
    // ==========================================
    private static void ScrollList(float moveAmount)
    {
        if (_contentRect == null) return;

        Vector2 pos = _contentRect.anchoredPosition;

        // 向下滚时 moveAmount 为负，Y 减去负数，整体变大（往上推），看到底部
        pos.y -= moveAmount;

        float totalHeight = _rows.Count * 50f;
        float viewHeight = 600f;

        // 因为锚点在顶部，Y 从 0 开始，往上推变成正数，所以范围是 [0, 最大可推高度]
        float maxY = (totalHeight) > 600 ? (totalHeight - viewHeight) + 50 : 0;
        float minY = 0f;

        pos.y = Mathf.Clamp(pos.y, minY, maxY);
        _contentRect.anchoredPosition = pos;
    }

    private static void BuildUI()
    {
        if (UnityEngine.Object.FindObjectOfType<EventSystem>() == null)
        {
            var es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            es.AddComponent<StandaloneInputModule>();
        }

        _canvasObj = new GameObject("CommandUI_Canvas");
        var canvas = _canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 9999;

        var scaler = _canvasObj.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        _canvasObj.AddComponent<GraphicRaycaster>();

        _panelObj = new GameObject("CommandPanel");
        _panelObj.transform.SetParent(_canvasObj.transform, false);
        var panelImg = _panelObj.AddComponent<Image>();
        panelImg.color = new Color(0.1f, 0.1f, 0.1f, 0.95f);

        var panelRect = _panelObj.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0.5f, 0.5f);
        panelRect.anchorMax = new Vector2(0.5f, 0.5f);
        panelRect.pivot = new Vector2(0.5f, 0.5f);
        panelRect.anchoredPosition = Vector2.zero;
        panelRect.sizeDelta = new Vector2(640, 800);

        CreateText(_panelObj.transform, "Title", "快捷指令面板", 20, Color.white, new Vector2(0, 350), new Vector2(500, 40)).fontStyle = FontStyle.Bold;

        // 顶部按钮
        CreateButton(_panelObj.transform, "添加新指令", new Vector2(-180, 290), () => { AddCommandRow(""); }, 200, 40);
        CreateButton(_panelObj.transform, "保存", new Vector2(40, 290), () => { SaveCommands(); }, 100, 40);
        CreateButton(_panelObj.transform, "读取", new Vector2(150, 290), () => { LoadCommands(); }, 100, 40);

        var closeBtn = CreateButton(_panelObj.transform, "X", new Vector2(280, 350), () => { StopSystem(); }, 40, 40);
        closeBtn.GetComponent<Image>().color = new Color(0.7f, 0.2f, 0.2f);

        // 列表可视区
        var listArea = new GameObject("ListArea");
        listArea.transform.SetParent(_panelObj.transform, false);
        var listImg = listArea.AddComponent<Image>();
        listImg.color = new Color(0.05f, 0.05f, 0.05f, 0.5f);

        var lRect = listArea.GetComponent<RectTransform>();
        lRect.anchorMin = new Vector2(0.5f, 0.5f);
        lRect.anchorMax = new Vector2(0.5f, 0.5f);
        lRect.pivot = new Vector2(0.5f, 0.5f);
        lRect.anchoredPosition = new Vector2(0, -30);
        lRect.sizeDelta = new Vector2(600, 600);

        listArea.AddComponent<RectMask2D>();

        // 列表内容容器
        var content = new GameObject("Content");
        content.transform.SetParent(listArea.transform, false);
        _contentRect = content.AddComponent<RectTransform>();
        _contentRect.anchorMin = new Vector2(0.5f, 1f);
        _contentRect.anchorMax = new Vector2(0.5f, 1f);
        _contentRect.pivot = new Vector2(0.5f, 1f);
        _contentRect.sizeDelta = new Vector2(600, 0);
        _contentRect.anchoredPosition = Vector2.zero;

        var cImg = content.AddComponent<Image>();
        cImg.color = new Color(0, 0, 0, 0);
    }

    private static void AddCommandRow(string defaultText)
    {
        if (_contentRect == null) return;

        try
        {
            var row = new GameObject("Row");
            row.transform.SetParent(_contentRect, false);

            var rect = row.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 1f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.sizeDelta = new Vector2(580, 45);

            var inputObj = new GameObject("Input");
            inputObj.transform.SetParent(row.transform, false);
            var iRect = inputObj.AddComponent<RectTransform>();
            iRect.anchorMin = new Vector2(0, 0.5f);
            iRect.anchorMax = new Vector2(0, 0.5f);
            iRect.pivot = new Vector2(0, 0.5f);
            iRect.sizeDelta = new Vector2(380, 32);
            iRect.anchoredPosition = new Vector2(10, 0);

            var img = inputObj.AddComponent<Image>(); img.color = new Color(0.25f, 0.25f, 0.25f);
            var input = inputObj.AddComponent<InputField>();

            var textObj = new GameObject("Text");
            textObj.transform.SetParent(inputObj.transform, false);
            var text = textObj.AddComponent<Text>();
            text.text = defaultText; text.fontSize = 16; text.color = Color.white;
            text.alignment = TextAnchor.MiddleLeft;
            text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            text.raycastTarget = false;

            var textRect = textObj.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero; textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(10, 0); textRect.offsetMax = new Vector2(-10, 0);

            input.textComponent = text;
            input.text = defaultText;

            var execBtn = CreateButton(row.transform, "执行", Vector2.zero, () => {
                try
                {
                    if (!string.IsNullOrEmpty(input.text))
                    {
                        TipMsgCore.i.sendComd(input.text);
                        MelonLogger.Msg($"[Cmd] 执行指令: {input.text}");
                    }
                }
                catch (Exception ex) { MelonLogger.Error($"[Cmd] 执行异常: {ex.Message}"); }
            }, 70, 32);
            var eRect = execBtn.GetComponent<RectTransform>();
            eRect.anchorMin = new Vector2(0, 0.5f); eRect.anchorMax = new Vector2(0, 0.5f);
            eRect.pivot = new Vector2(0, 0.5f); eRect.anchoredPosition = new Vector2(400, 0);

            var delBtn = CreateButton(row.transform, "删", Vector2.zero, () => {
                _rows.Remove(row);
                UnityEngine.Object.Destroy(row);
                RebuildLayout();
            }, 60, 32);
            var dRect = delBtn.GetComponent<RectTransform>();
            dRect.anchorMin = new Vector2(0, 0.5f); dRect.anchorMax = new Vector2(0, 0.5f);
            dRect.pivot = new Vector2(0, 0.5f); dRect.anchoredPosition = new Vector2(480, 0);
            delBtn.GetComponent<Image>().color = new Color(0.7f, 0.2f, 0.2f);

            _rows.Add(row);
            RebuildLayout();
        }
        catch (Exception ex)
        {
            MelonLogger.Error($"[Cmd] 添加行异常: {ex.Message}\n{ex.StackTrace}");
        }
    }

    private static void RebuildLayout()
    {
        if (_contentRect == null) return;

        float startY = -25f;
        float spacing = 50f;

        for (int i = 0; i < _rows.Count; i++)
        {
            if (_rows[i] != null)
            {
                var rect = _rows[i].GetComponent<RectTransform>();
                rect.anchoredPosition = new Vector2(0, startY - (i * spacing));
            }
        }

        _contentRect.sizeDelta = new Vector2(600, (_rows.Count * spacing) + 20f);

        // 如果行数不够，强制回到顶部
        float maxScroll = Mathf.Max(0, (_rows.Count * spacing) - 600f);
        if (_contentRect.anchoredPosition.y > maxScroll)
        {
            _contentRect.anchoredPosition = new Vector2(_contentRect.anchoredPosition.x, maxScroll);
        }
    }

    public static void SaveCommands()
    {
        try
        {
            if (!Directory.Exists(SaveDir)) Directory.CreateDirectory(SaveDir);
            List<string> lines = new List<string>();
            foreach (var row in _rows)
            {
                var input = row.GetComponentInChildren<InputField>();
                if (input != null) lines.Add(input.text);
            }
            File.WriteAllLines(SavePath, lines);
            MelonLogger.Msg($"[Cmd] 已保存 {lines.Count} 条指令到 {SavePath}");
        }
        catch (Exception ex) { MelonLogger.Error($"[Cmd] 保存失败: {ex.Message}"); }
    }

    public static void LoadCommands()
    {
        try
        {
            if (!File.Exists(SavePath))
            {
                MelonLogger.Warning("[Cmd] 未找到保存文件");
                return;
            }

            foreach (var row in _rows) UnityEngine.Object.Destroy(row);
            _rows.Clear();
            if (_contentRect != null) _contentRect.anchoredPosition = Vector2.zero;

            string[] lines = File.ReadAllLines(SavePath);
            foreach (var line in lines) AddCommandRow(line);

            MelonLogger.Msg($"[Cmd] 已读取 {lines.Length} 条指令");
        }
        catch (Exception ex) { MelonLogger.Error($"[Cmd] 读取失败: {ex.Message}"); }
    }

    // ==========================================
    // UI 辅助方法
    // ==========================================
    private static Text CreateText(Transform parent, string name, string content, int fontSize, Color color, Vector2 pos, Vector2 size)
    {
        var obj = new GameObject(name);
        obj.transform.SetParent(parent, false);
        var txt = obj.AddComponent<Text>();
        txt.text = content; txt.fontSize = fontSize; txt.color = color;
        txt.alignment = TextAnchor.MiddleCenter;
        txt.font = Resources.GetBuiltinResource<Font>("Arial.ttf");

        var rect = txt.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
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
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = pos; rect.sizeDelta = new Vector2(width, height);

        var txtObj = new GameObject("Text");
        txtObj.transform.SetParent(obj.transform, false);
        var txt = txtObj.AddComponent<Text>();
        txt.text = label; txt.fontSize = Mathf.Clamp((int)(height * 0.5f), 12, 20);
        txt.color = Color.white; txt.alignment = TextAnchor.MiddleCenter;
        txt.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        txt.raycastTarget = false;

        var txtRect = txtObj.GetComponent<RectTransform>();
        txtRect.anchorMin = Vector2.zero; txtRect.anchorMax = Vector2.one;
        txtRect.offsetMin = Vector2.zero; txtRect.offsetMax = Vector2.zero;
        return obj;
    }
}