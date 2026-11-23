using System.Globalization;
using MelonLoader;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace GameOptimizerModule;

// Changes URP's scene render target scale; never resizes the window or overlay UI.
internal sealed class RenderScaleController : IDisposable
{
    private sealed class UrpSettings : ISettings
    {
        internal readonly UniversalRenderPipelineAsset Asset;
        internal UrpSettings(UniversalRenderPipelineAsset asset) { Asset = asset; }
        public float Get(Setting key) => Asset.renderScale;
        public void Set(Setting key, float value) => Asset.renderScale = value;
    }
    private UrpSettings? settings;
    private SettingsLease? lease;
    private float requested;
    private bool dirty = true, failed;
    private double nextPoll;
    public string Status { get; private set; } = "Render scale: waiting";
    public void Configure(float value)
    {
        Restore();
        requested = value; dirty = true; failed = false; nextPoll = 0;
    }
    public void Tick(double now)
    {
        if (failed || now < nextPoll) return;
        nextPoll = now + 1;
        try
        {
            var pipeline = GraphicsSettings.currentRenderPipeline;
            var asset = pipeline == null ? null : pipeline.TryCast<UniversalRenderPipelineAsset>();
            if (settings != null && (settings.Asset == null || asset == null || settings.Asset.Pointer != asset.Pointer))
                Restore();
            if (asset == null)
            {
                Status = "Render scale: unsupported (non-URP)";
                return;
            }
            if (settings == null)
            {
                settings = new UrpSettings(asset);
                lease = new SettingsLease(settings); dirty = true;
            }
            if (dirty)
            {
                if (requested > 0) lease!.Write(Setting.RenderScale, requested);
                dirty = false;
            }
            float actual = asset.renderScale;
            string mode = requested <= 0 ? "game" : Math.Abs(actual - requested) < .001f ? "applied" : "game override";
            Status = "URP scale " + actual.ToString("0.##", CultureInfo.InvariantCulture)
                + " | request " + (requested <= 0 ? "off" : requested.ToString("0.##", CultureInfo.InvariantCulture)) + " | " + mode;
        }
        catch (Exception ex)
        {
            failed = true; Status = "Render scale: N/A (see log)";
            MelonLogger.Warning("[GameOptimizer] render scale unavailable: " + ex.Message);
            try { Restore(); } catch (Exception restore) { MelonLogger.Warning("[GameOptimizer] render scale restore: " + restore.Message); }
        }
    }
    private void Restore()
    {
        if (settings?.Asset != null) lease?.Restore();
        lease = null; settings = null; dirty = true;
    }
    public void Dispose() => Restore();
}
