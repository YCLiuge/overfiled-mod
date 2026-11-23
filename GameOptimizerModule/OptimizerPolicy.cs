namespace GameOptimizerModule;

public sealed class OptimizerOptions
{
    public bool Enabled { get; set; } = true;
    public bool BundleMetadataCache { get; set; } = true;
    public bool OptimizeAsyncLoading { get; set; } = true;
    // 0 preserves game control; otherwise URP render scale, independently of adaptive tiers.
    public float RenderScale { get; set; } = 0;
    public int TargetFps { get; set; } = 60;
    public int BackgroundFps { get; set; } = 15;
    public bool DisableVSync { get; set; } = true;
    public bool AdaptiveQuality { get; set; } = true;
    public int InitialTier { get; set; } = 1;
    public int MaxTier { get; set; } = 3;
    public double SampleSeconds { get; set; } = 3;
    public double CooldownSeconds { get; set; } = 10;
    public int SlowWindows { get; set; } = 2;
    public int FastWindows { get; set; } = 5;
    public void Validate()
    {
        if (!float.IsFinite(RenderScale) || (RenderScale != 0 && (RenderScale < .5f || RenderScale > 1f)) ||
            TargetFps < 30 || TargetFps > 240 || BackgroundFps < 1 || BackgroundFps > TargetFps ||
            InitialTier < 0 || InitialTier > MaxTier || MaxTier > 3 ||
            !double.IsFinite(SampleSeconds) || SampleSeconds < 1 || SampleSeconds > 30 ||
            !double.IsFinite(CooldownSeconds) || CooldownSeconds < 3 || CooldownSeconds > 300 ||
            SlowWindows < 1 || SlowWindows > 20 || FastWindows < 1 || FastWindows > 60)
            throw new ArgumentException("配置超出范围，请参阅 README");
    }
}

// Pure policy: no Unity objects or allocations on the per-frame path.
public sealed class AdaptivePolicy
{
    private OptimizerOptions options = new();
    private double seconds, cooldown;
    private int frames, slow, fast;
    public int Tier { get; private set; }
    public double LastFps { get; private set; }
    public void Reset(OptimizerOptions value)
    {
        value.Validate(); options = value; Tier = value.InitialTier; cooldown = value.CooldownSeconds;
        LastFps = 0; ClearWindow();
    }
    public void ClearWindow() { seconds = 0; frames = slow = fast = 0; }
    public bool Tick(double delta, bool focused)
    {
        // Exclude focus transitions, loading stalls, pauses, and invalid clocks.
        if (!focused || !double.IsFinite(delta) || delta <= 0 || delta > 0.5)
        { ClearWindow(); return false; }
        cooldown = Math.Max(0, cooldown - delta);
        seconds += delta; frames++;
        if (seconds < options.SampleSeconds) return false;
        LastFps = frames / seconds; seconds = 0; frames = 0;
        if (!options.AdaptiveQuality || cooldown > 0) { slow = fast = 0; return false; }
        if (LastFps < options.TargetFps * 0.8) { slow++; fast = 0; }
        else if (LastFps >= options.TargetFps * 0.95) { fast++; slow = 0; }
        else { slow = fast = 0; }
        int next = Tier;
        if (slow >= options.SlowWindows) next = Math.Min(options.MaxTier, Tier + 1);
        if (fast >= options.FastWindows) next = Math.Max(0, Tier - 1);
        if (next == Tier) return false;
        Tier = next; ClearWindow(); cooldown = options.CooldownSeconds; return true;
    }
}

public enum Setting { TargetFps, VSync, ShadowDistance, ShadowResolution, AntiAliasing, LodBias, LoadingPriority, UploadTimeSlice, RenderScale }
public interface ISettings
{
    float Get(Setting key);
    void Set(Setting key, float value);
}

// Only restore values still equal to our last write, preserving later game/user changes.
public sealed class SettingsLease
{
    private readonly ISettings settings;
    private readonly Dictionary<Setting, (float Original, float Written)> owned = new();
    public SettingsLease(ISettings settings) { this.settings = settings; }
    public void Write(Setting key, float value)
    {
        float current = settings.Get(key);
        if (owned.TryGetValue(key, out var prior) && current != prior.Written) owned.Remove(key);
        if (current == value) return;
        var original = owned.TryGetValue(key, out prior) ? prior.Original : current;
        // Register before writing so a setter that partially succeeds can also be restored.
        owned[key] = (original, value);
        settings.Set(key, value);
    }
    public void Restore()
    {
        Exception? failure = null;
        foreach (var item in owned.ToArray())
        {
            try
            {
                if (settings.Get(item.Key) == item.Value.Written) settings.Set(item.Key, item.Value.Original);
                owned.Remove(item.Key);
            }
            catch (Exception ex) { failure ??= ex; }
        }
        if (failure != null) throw new InvalidOperationException("部分画质设置恢复失败", failure);
    }
    public float Original(Setting key)
    {
        float current = settings.Get(key);
        return owned.TryGetValue(key, out var item) && current == item.Written ? item.Original : current;
    }
}

public static class QualityPlan
{
    public static void Apply(SettingsLease lease, OptimizerOptions options, int tier, bool focused)
    {
        lease.Write(Setting.LoadingPriority, options.OptimizeAsyncLoading ? 0 : lease.Original(Setting.LoadingPriority));
        lease.Write(Setting.UploadTimeSlice, options.OptimizeAsyncLoading ? Math.Min(2, lease.Original(Setting.UploadTimeSlice)) : lease.Original(Setting.UploadTimeSlice));
        lease.Write(Setting.TargetFps, focused ? options.TargetFps : options.BackgroundFps);
        // VSync takes precedence over targetFrameRate on desktop, including in the background.
        lease.Write(Setting.VSync, options.DisableVSync || !focused ? 0 : lease.Original(Setting.VSync));
        float distance = lease.Original(Setting.ShadowDistance);
        float resolution = lease.Original(Setting.ShadowResolution);
        float aa = lease.Original(Setting.AntiAliasing);
        float lod = lease.Original(Setting.LodBias);
        lease.Write(Setting.ShadowDistance, tier == 0 ? distance : Math.Min(distance, tier == 1 ? 60 : tier == 2 ? 35 : 15));
        lease.Write(Setting.ShadowResolution, tier == 0 ? resolution : Math.Min(resolution, tier == 1 ? 2 : tier == 2 ? 1 : 0));
        lease.Write(Setting.AntiAliasing, tier == 0 ? aa : tier == 1 ? Math.Min(aa, 2) : 0);
        lease.Write(Setting.LodBias, tier == 0 ? lod : Math.Min(lod, tier == 1 ? 1 : tier == 2 ? 0.7f : 0.45f));
    }
}
