using System.Globalization;
using System.Text;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace GameOptimizerModule;

// Main-thread UI driven by ILogic. No EventSystem, raycaster, or custom IL2CPP behaviour.
public sealed class PerformancePanel : IDisposable
{
    private readonly FrameMetrics metrics = new();
    private readonly StringBuilder text = new(1024);
    private GameObject? root;
    private Text? label;
    private Font? font;
    private RectTransform? panelRect;
    private CanvasScaler? scaler;
    private bool ownsFont, disposed, visible = false, details;
    private double nextRefresh;

    public void Tick(double now, double delta, string status, string bundleStatus, bool enabled, string renderStatus = "")
    {
        if (disposed) return;
        metrics.Add(delta);
        if (Input.GetKeyDown(KeyCode.F9))
        {
            visible = !visible;
            if (root != null) root.SetActive(visible);
            nextRefresh = 0;
        }
        if (Input.GetKeyDown(KeyCode.F10)) { details = !details; nextRefresh = 0; }
        if (!visible) return;
        if (root == null) { Build(); nextRefresh = 0; }
        if (now < nextRefresh) return;
        nextRefresh = now + .5;
        // Fixed pixel scaling prevents game resolution/UI scale from enlarging the panel.
        scaler!.scaleFactor = Math.Clamp(Screen.height / 900f, .65f, 1f);
        panelRect!.sizeDelta = new Vector2(430, details ? 430 : 300);
        Refresh(status, bundleStatus, enabled, renderStatus);
    }
    private Font ResolveFont()
    {
        if (font != null) return font;
        try { font = Resources.GetBuiltinResource<Font>("Arial.ttf"); } catch { }
        if (font == null) try { font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); } catch { }
        if (font == null)
        {
            font = Font.CreateDynamicFontFromOSFont("Arial", 16);
            ownsFont = font != null;
            if (font != null) Object.DontDestroyOnLoad(font);
        }
        return font ?? throw new InvalidOperationException("Performance panel font unavailable");
    }
    private void Build()
    {
        var resolved = ResolveFont();
        root = new GameObject("GameOptimizer_PerformancePanel");
        Object.DontDestroyOnLoad(root);
        var canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 22000;
        scaler = root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
        scaler.scaleFactor = Math.Clamp(Screen.height / 900f, .65f, 1f);
        var panel = new GameObject("Panel");
        panel.transform.SetParent(root.transform, false);
        panelRect = panel.AddComponent<RectTransform>();
        panelRect.anchorMin = panelRect.anchorMax = panelRect.pivot = new Vector2(1, 1);
        panelRect.anchoredPosition = new Vector2(-12, -12);
        var background = panel.AddComponent<Image>();
        background.color = new Color(.025f, .035f, .05f, .93f);
        background.raycastTarget = false;
        var content = new GameObject("Metrics");
        content.transform.SetParent(panel.transform, false);
        label = content.AddComponent<Text>();
        label.font = resolved; label.fontSize = 15;
        label.color = new Color(.9f, .95f, 1, 1);
        label.alignment = TextAnchor.UpperLeft;
        label.supportRichText = false; label.raycastTarget = false;
        label.horizontalOverflow = HorizontalWrapMode.Wrap;
        label.verticalOverflow = VerticalWrapMode.Truncate;
        var rect = content.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(10, 8); rect.offsetMax = new Vector2(-10, -8);
    }
    private void Refresh(string status, string bundleStatus, bool enabled, string renderStatus = "")
    {
        var snapshot = metrics.Snapshot();
        text.Clear();
        text.Append("Optimizer ").Append(enabled ? "ON" : "OFF").Append(" | F9 hide | F10 details");
        text.Append("\nFPS "); Number(snapshot.Fps);
        text.Append(" | avg "); Number(snapshot.AverageFrameMilliseconds);
        text.Append(" ms | P95 "); Number(snapshot.P95FrameMilliseconds); text.Append(" ms");
        text.Append("\n"); Status(status);
        text.Append("\n"); Status(renderStatus);
        if (details)
        {
            text.Append("\nSamples ").Append(snapshot.SampleCount).Append("/512 (includes stalls)");
            text.Append("\n.NET heap "); MiB(GC.GetTotalMemory(false));
        }
        text.Append("\nUnity alloc ");
        try { MiB(Profiler.GetTotalAllocatedMemoryLong()); } catch { text.Append("N/A"); }
        if (details)
        {
            text.Append("\nUnity reserved ");
            try { MiB(Profiler.GetTotalReservedMemoryLong()); } catch { text.Append("N/A"); }
            text.Append("\n.NET GC 0/1/2: ").Append(GC.CollectionCount(0)).Append('/').Append(GC.CollectionCount(1)).Append('/').Append(GC.CollectionCount(2));
        }
        text.Append('\n'); Status(bundleStatus);
        if (details) text.Append("\nMemory counters are not total process RAM.");
        label!.text = text.ToString();
    }
    private void Number(double value) => text.Append(value.ToString("F1", CultureInfo.InvariantCulture));
    private void MiB(long bytes) { if (bytes <= 0) text.Append("N/A"); else { Number(bytes / (1024.0 * 1024)); text.Append(" MiB"); } }
    private void Status(string value)
    {
        if (string.IsNullOrEmpty(value)) { text.Append('-'); return; }
        int count = Math.Min(value.Length, 620);
        for (int i = 0; i < count; i++) text.Append(value[i] == '\n' ? '\n' : char.IsControl(value[i]) ? ' ' : value[i]);
        if (count < value.Length) text.Append("...");
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        if (root != null) { root.SetActive(false); Object.Destroy(root); }
        if (ownsFont && font != null) Object.Destroy(font);
        root = null; label = null; font = null; scaler = null; panelRect = null;
    }
}
