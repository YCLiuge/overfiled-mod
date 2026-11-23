using MelonLoader;
using mod;
using mod.st;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static class CoordinateDisplaySystem
{
    public static bool IsVisible = false;
    private static GameObject _canvasObj;
    private static Text _coordText;

    public static void Init()
    {
        // 【防呆设计】如果已经存在，先删掉旧的，防止多开
        if (_canvasObj != null) Object.Destroy(_canvasObj);

        _canvasObj = new GameObject("CoordUI_Canvas");
        var canvas = _canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 30000;

        var scaler = _canvasObj.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);

        var textObj = new GameObject("CoordText");
        textObj.transform.SetParent(_canvasObj.transform, false);

        _coordText = textObj.AddComponent<Text>();
        _coordText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        _coordText.fontSize = 22;
        _coordText.fontStyle = FontStyle.Bold;
        _coordText.color = Color.cyan;
        _coordText.alignment = TextAnchor.UpperLeft;
        _coordText.raycastTarget = false;

        var rect = _coordText.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0, 0.5f);
        rect.anchorMax = new Vector2(0, 0.5f);
        rect.pivot = new Vector2(0, 0.5f);
        rect.anchoredPosition = new Vector2(30, 0);

        rect.sizeDelta = new Vector2(800, 80);

        // 【核心修复】保证 Canvas 不会随场景切换而销毁
        Object.DontDestroyOnLoad(_canvasObj);

        MelonLogger.Msg("[Coord] 坐标与人数显示系统已初始化（跨场景保留）");
    }

    /// <summary>
    /// 每帧更新坐标（在你的主 Mod 的 OnUpdate 里调用）
    /// </summary>
    public static void Tick()
    {
        if (Input.GetKeyDown(KeyCode.F9))
        {
            IsVisible = !IsVisible;
            if (_canvasObj != null) _canvasObj.SetActive(IsVisible);
            else
            {
                Init();
            }
            MelonLogger.Msg($"[Coord] 显示状态: {(IsVisible ? "开启" : "关闭")}");
        }

        if (!IsVisible || _coordText == null) return;

        // 1. 获取坐标（加 try-catch 防止切场景瞬间报错）
        string coordStr = "等待玩家加载...";
        try
        {
            if (TipMsgCore.i != null && TipMsgCore.i.mainplayer != null)
            {
                Vector3 pos = TipMsgCore.i.mainplayer.transform.position;
                coordStr = $"X: {pos.x:F2}  |  Y: {pos.y:F2}  |  Z: {pos.z:F2}";
            }
        }
        catch { }

        // 2. 获取游戏内频道人数
        int gamePlayerCount = 0;
        try
        {
            if (TipMsgCore.i != null && TipMsgCore.i.players != null)
                gamePlayerCount = TipMsgCore.i.players.Count;
        }
        catch { }

        // 3. 获取 Mod 频道在线人数
        int modPlayerCount = 0;
        try
        {
            if (NetPlayer.Instance != null && NetPlayer.Instance.players != null)
                modPlayerCount = NetPlayer.Instance.players.Count;
        }
        catch { }

        // 4. 拼接显示
        _coordText.text = $"{coordStr}\n游戏频道人数: {gamePlayerCount}  |  Mod频道人数: {modPlayerCount}";
    }

    /// <summary>
    /// 销毁 UI（可选，主 Mod 卸载时调用）
    /// </summary>
    public static void Destroy()
    {
        if (_canvasObj != null) Object.Destroy(_canvasObj);
    }
}