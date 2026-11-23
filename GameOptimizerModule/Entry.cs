using System.Diagnostics;
using System.Text.Json;
using MelonLoader;
using ModLoader;
using UnityEngine;

namespace GameOptimizerModule;

internal sealed class UnitySettings : ISettings
{
    public float Get(Setting key) => key switch
    {
        Setting.LoadingPriority => (int)Application.backgroundLoadingPriority,
        Setting.UploadTimeSlice => QualitySettings.asyncUploadTimeSlice,
        Setting.TargetFps => Application.targetFrameRate,
        Setting.VSync => QualitySettings.vSyncCount,
        Setting.ShadowDistance => QualitySettings.shadowDistance,
        Setting.ShadowResolution => (int)QualitySettings.shadowResolution,
        Setting.AntiAliasing => QualitySettings.antiAliasing,
        Setting.LodBias => QualitySettings.lodBias,
        _ => throw new ArgumentOutOfRangeException(nameof(key))
    };
    public void Set(Setting key, float value)
    {
        switch (key)
        {
            case Setting.LoadingPriority: Application.backgroundLoadingPriority = (UnityEngine.ThreadPriority)(int)value; break;
            case Setting.UploadTimeSlice: QualitySettings.asyncUploadTimeSlice = (int)value; break;
            case Setting.TargetFps: Application.targetFrameRate = (int)value; break;
            case Setting.VSync: QualitySettings.vSyncCount = (int)value; break;
            case Setting.ShadowDistance: QualitySettings.shadowDistance = value; break;
            case Setting.ShadowResolution: QualitySettings.shadowResolution = (ShadowResolution)(int)value; break;
            case Setting.AntiAliasing: QualitySettings.antiAliasing = (int)value; break;
            case Setting.LodBias: QualitySettings.lodBias = value; break;
            default: throw new ArgumentOutOfRangeException(nameof(key));
        }
    }
}

public sealed class Entry : ILogic
{
    private readonly AdaptivePolicy policy = new();
    private readonly BundleOptimizer bundles = new();
    private readonly BundleMonitor bundleMonitor = new();
    private readonly FrameDiagnostics frameDiagnostics = new();
    private readonly RenderScaleController renderScale = new();
    private PerformancePanel? panel;
    private bool panelFaulted;
    private double nextPanelStatus;
    private string panelStatus = "", bundleStatus = "", renderStatus = "";
    private readonly SettingsLease lease = new(new UnitySettings());
    private OptimizerOptions options = new();
    private readonly string configPath = Path.Combine(Directory.GetCurrentDirectory(), "UserData", "GameOptimizer.json");
    private DateTime configStamp;
    private bool loaded, focused, faulted;
    private double previous, nextConfig;
    private static double Now => (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency;
    public string Name => "GameOptimizerModule";
    public string Version => "1.2.1";
    public string Diagnostics() => $"loaded={loaded}, enabled={options.Enabled}, faulted={faulted}, tier={policy.Tier}, fps={policy.LastFps:F1}, focused={focused}";

    public void OnLoad()
    {
        if (loaded) return;
        try
        {
            options = new(); faulted = false; configStamp = default;
            Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
            if (!File.Exists(configPath)) File.WriteAllText(configPath, JsonSerializer.Serialize(options, new JsonSerializerOptions { WriteIndented = true }));
            focused = Application.isFocused;
            policy.Reset(options);
            loaded = true;
            panel = new PerformancePanel(); panelFaulted = false; nextPanelStatus = 0;
            bundles.Start();
            bundleMonitor.Start();
            ReadConfig();
            renderScale.Configure(options.Enabled && !faulted ? options.RenderScale : 0);
            bundles.Configure(options.Enabled && options.BundleMetadataCache);
            if (options.Enabled && !faulted) QualityPlan.Apply(lease, options, policy.Tier, focused);
            previous = Now; nextConfig = previous + 2;
            MelonLogger.Msg("[GameOptimizer] loaded; config=" + configPath + "; " + Diagnostics());
        }
        catch { OnUnload(); throw; }
    }

    private void ReadConfig()
    {
        // A rejected edit is retried only after its timestamp changes.
        OptimizerOptions value;
        try
        {
            if (!File.Exists(configPath)) return;
            DateTime stamp = File.GetLastWriteTimeUtc(configPath);
            if (stamp == configStamp) return;
            configStamp = stamp;
            if (new FileInfo(configPath).Length > 16384) throw new InvalidDataException("配置超过 16KB");
            value = JsonSerializer.Deserialize<OptimizerOptions>(File.ReadAllText(configPath), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new InvalidDataException("配置不能为空");
            value.Validate();
        }
        catch (Exception ex)
        {
            MelonLogger.Warning("[GameOptimizer] configuration rejected: " + ex.Message);
            return;
        }
        // Unity write failures propagate to the lifecycle handler for suspension and rollback.
        // Restore before recapturing: repeated edits must not compound reductions.
        lease.Restore();
        options = value; policy.Reset(value); faulted = false;
        bundles.Configure(options.Enabled && options.BundleMetadataCache);
        renderScale.Configure(options.Enabled ? options.RenderScale : 0);
        if (options.Enabled) QualityPlan.Apply(lease, options, policy.Tier, focused);
        MelonLogger.Msg("[GameOptimizer] configuration loaded; " + Diagnostics());
    }

    public void OnUpdate()
    {
        if (!loaded) return;
        double now = Now, delta = now - previous; previous = now;
        renderScale.Tick(now);
        if (!panelFaulted)
        {
            try
            {
                frameDiagnostics.Tick(now);
                if (now >= nextPanelStatus)
                {
                    nextPanelStatus = now + 0.5;
                    panelStatus = $"Tier {policy.Tier}/3 | target {options.TargetFps} | {(focused ? "foreground" : "background")} | {(faulted ? "SUSPENDED" : "OK")}";
                    bundleMonitor.Tick(now);
                    bundleStatus = bundleMonitor.Status + "\n" + bundles.Status;
                    renderStatus = frameDiagnostics.Status + "\n" + renderScale.Status;
                }
                panel?.Tick(now, delta, panelStatus, bundleStatus, options.Enabled, renderStatus);
            }
            catch (Exception ex)
            {
                panelFaulted = true;
                panel?.Dispose(); panel = null;
                MelonLogger.Warning("[GameOptimizer] panel disabled: " + ex.Message);
            }
        }
        try
        {
            bool currentFocus = Application.isFocused;
            bool focusChanged = focused != currentFocus;
            focused = currentFocus;
            if (now >= nextConfig) { nextConfig = now + 2; ReadConfig(); }
            if (!options.Enabled || faulted) return;
            if (focusChanged)
            {
                policy.ClearWindow();
                QualityPlan.Apply(lease, options, policy.Tier, focused);
                return;
            }
            if (policy.Tick(delta, focused))
            {
                QualityPlan.Apply(lease, options, policy.Tier, focused);
                MelonLogger.Msg("[GameOptimizer] quality adjusted; " + Diagnostics());
            }
        }
        catch (Exception ex)
        {
            faulted = true;
            MelonLogger.Warning("[GameOptimizer] suspended after error: " + ex.Message);
            try { renderScale.Configure(0); }
            catch (Exception restore) { MelonLogger.Warning("[GameOptimizer] render scale restore failed: " + restore.Message); }
            try { lease.Restore(); }
            catch (Exception restore) { MelonLogger.Warning("[GameOptimizer] restore failed: " + restore.Message); }
        }
    }

    public void OnUnload()
    {
        loaded = false;
        try { panel?.Dispose(); }
        finally
        {
            panel = null;
            try { bundles.Dispose(); }
            finally
            {
                try { bundleMonitor.Dispose(); }
                finally
                {
                    try { renderScale.Dispose(); }
                    finally { lease.Restore(); policy.ClearWindow(); }
                }
            }
        }
        MelonLogger.Msg("[GameOptimizer] unloaded; settings restored where still owned");
    }
}
