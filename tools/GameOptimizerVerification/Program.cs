using GameOptimizerModule;

int checks = 0;
void Check(bool condition, string message) { if (!condition) throw new Exception(message); checks++; }
var options = new OptimizerOptions { SampleSeconds = 1, CooldownSeconds = 3, SlowWindows = 2, FastWindows = 5 };
var policy = new AdaptivePolicy();
void Run(double fps, int frames, bool focused = true) { for (int i = 0; i < frames; i++) policy.Tick(1 / fps, focused); }
policy.Reset(options);
Run(30, 60); Check(policy.Tier == 1, "cooldown must block initial changes");
Run(30, 150); Check(policy.Tier == 2, "sustained slow frames degrade once");
Run(30, 240); Check(policy.Tier == 3, "degrade to configured maximum");
Run(30, 600); Check(policy.Tier == 3, "never exceed maximum");
Run(60, 540); Check(policy.Tier == 2, "sustained recovery upgrades one step");
policy.Reset(options); Run(60, 240); Run(30, 35); Run(55, 65); Run(30, 35);
Check(policy.Tier == 1, "neutral window breaks slow streak");
policy.Reset(options); Run(15, 1000, false); Check(policy.Tier == 1 && policy.LastFps == 0, "background fps must not lower quality");
for (int i = 0; i < 100; i++) policy.Tick(3, true);
Check(policy.Tier == 1, "loading stalls ignored");
Check(!policy.Tick(double.NaN, true) && !policy.Tick(-1, true), "invalid clocks ignored");
options.AdaptiveQuality = false; policy.Reset(options); Run(20, 2000); Check(policy.Tier == 1, "fixed quality stays fixed");
foreach (var bad in new[] { new OptimizerOptions { TargetFps = 0 }, new OptimizerOptions { InitialTier = -1 }, new OptimizerOptions { MaxTier = -1 }, new OptimizerOptions { InitialTier = 3, MaxTier = 2 }, new OptimizerOptions { SampleSeconds = double.NaN }, new OptimizerOptions { CooldownSeconds = double.PositiveInfinity }, new OptimizerOptions { BackgroundFps = 120 }, new OptimizerOptions { FastWindows = 0 } })
{
    bool rejected = false; try { bad.Validate(); } catch (ArgumentException) { rejected = true; }
    Check(rejected, "invalid options rejected");
}
var settings = new FakeSettings(); var lease = new SettingsLease(settings);
var baseline = new Dictionary<Setting, float>(settings.Values);
options = new();
QualityPlan.Apply(lease, options, 1, true);
Check(settings.Get(Setting.TargetFps) == 60 && settings.Get(Setting.VSync) == 0, "foreground cap effective");
Check(settings.Get(Setting.ShadowDistance) == 60 && settings.Get(Setting.AntiAliasing) == 2, "balanced graphics");
QualityPlan.Apply(lease, options, 3, true);
Check(settings.Get(Setting.LodBias) == 0.45f && settings.Get(Setting.ShadowDistance) == 15, "low graphics");
QualityPlan.Apply(lease, options, 0, true);
Check(settings.Get(Setting.LodBias) == baseline[Setting.LodBias] && settings.Get(Setting.ShadowDistance) == baseline[Setting.ShadowDistance], "tier zero restores baseline graphics");
QualityPlan.Apply(lease, options, 1, false);
Check(settings.Get(Setting.TargetFps) == 15 && settings.Get(Setting.VSync) == 0, "background cap");
lease.Restore(); Check(settings.Values.All(x => x.Value == baseline[x.Key]), "unload restores baseline");
lease.Restore(); Check(settings.Values.All(x => x.Value == baseline[x.Key]), "unload idempotent");
QualityPlan.Apply(lease, options, 3, true); settings.Set(Setting.ShadowDistance, 22); lease.Restore();
Check(settings.Get(Setting.ShadowDistance) == 22, "external edits survive unload");
settings.Set(Setting.LodBias, 0.2f); settings.Set(Setting.AntiAliasing, 0); settings.Set(Setting.ShadowResolution, 0);
QualityPlan.Apply(lease, options, 1, true);
Check(settings.Get(Setting.LodBias) == 0.2f && settings.Get(Setting.AntiAliasing) == 0 && settings.Get(Setting.ShadowResolution) == 0, "never increase existing low settings");
lease.Restore();
options.DisableVSync = false; QualityPlan.Apply(lease, options, 1, true);
Check(settings.Get(Setting.VSync) == 1, "preserve foreground vsync when configured");
QualityPlan.Apply(lease, options, 1, false); QualityPlan.Apply(lease, options, 1, true);
Check(settings.Get(Setting.VSync) == 1 && settings.Get(Setting.TargetFps) == 60, "restore vsync after refocus");
lease.Restore();
// Restore must continue after one setter fails and retain that key for retry.
lease.Write(Setting.TargetFps, 60); lease.Write(Setting.ShadowDistance, 10); settings.Fail = Setting.TargetFps;
bool failed = false; try { lease.Restore(); } catch (InvalidOperationException) { failed = true; }
Check(failed && settings.Get(Setting.ShadowDistance) == 22, "partial restore still restores other keys");
settings.Fail = null; lease.Restore(); Check(settings.Get(Setting.TargetFps) == -1, "failed restore can retry");
Check(settings.Get(Setting.LoadingPriority) == 2 && settings.Get(Setting.UploadTimeSlice) == 4, "async settings restored");
QualityPlan.Apply(lease, new OptimizerOptions(), 1, true);
Check(settings.Get(Setting.LoadingPriority) == 0 && settings.Get(Setting.UploadTimeSlice) == 2, "async loading budget reduced");
lease.Restore();
var cache = new BundleMetadataCache();
Check(!cache.TryGet(100, 1, 0, out _), "cache cold miss");
string[] source = { "a", "b" }; cache.Store(100, 1, 0, source); source[0] = "mutated";
Check(cache.TryGet(100, 1, 1, out var names) && names[0] == "a", "cache copies input");
names[0] = "caller";
Check(cache.TryGet(100, 1, 2, out names) && names[0] == "a", "cache copies output");
Check(!cache.TryGet(100, 2, 2, out _), "instance reuse isolated");
Check(!cache.TryGet(100, 1, 5, out _) && cache.Count == 0, "TTL expires at boundary");
for (int i = 0; i < 200; i++) cache.Store(i, i, i, new[] { "name" });
Check(cache.Count <= 64, "bundle count bounded");
cache.Store(300, 300, 0, new string[32769]); Check(cache.NameCount <= 32768, "oversize rejected");
cache.Store(301, 301, 0, new string[20000]); cache.Store(302, 302, 0, new string[20000]);
Check(cache.NameCount <= 32768, "total names bounded");
cache.Clear(); Check(cache.Count == 0 && cache.NameCount == 0, "unload invalidation");
var metrics = new FrameMetrics();
Check(metrics.Snapshot().SampleCount == 0, "empty frame window");
metrics.Add(double.NaN); metrics.Add(-1); metrics.Add(0); metrics.Add(double.PositiveInfinity);
Check(metrics.Snapshot().SampleCount == 0, "invalid frames excluded");
for (int i = 0; i < 18; i++) metrics.Add(1.0 / 60);
metrics.Add(1); metrics.Add(1);
var snap = metrics.Snapshot();
Check(Math.Abs(snap.AverageFrameMilliseconds - 115) < 0.001, "stalls included in frame mean");
Check(snap.P95FrameMilliseconds == 1000, "P95 nearest rank");
Check(Math.Abs(snap.Fps - 1000 / 115.0) < 0.001, "FPS uses total frame time");
for (int i = 0; i < 600; i++) metrics.Add(0.01);
snap = metrics.Snapshot();
Check(snap.SampleCount == 512 && Math.Abs(snap.Fps - 100) < 0.001, "frame ring drops old stalls");
using (var core = Mono.Cecil.AssemblyDefinition.ReadAssembly("libs/UnityEngine.CoreModule.dll"))
{
    var priority = core.MainModule.Types.Single(t => t.FullName == "UnityEngine.ThreadPriority");
    Check(Convert.ToInt32(priority.Fields.Single(f => f.Name == "Low").Constant) == 0, "real Unity loading priority enum");
}
foreach (float scale in new[] { 0f, .5f, .75f, 1f })
{
    new OptimizerOptions { RenderScale = scale }.Validate();
    Check(true, "valid render scale " + scale);
}
foreach (float scale in new[] { -.1f, .49f, 1.01f, float.NaN, float.PositiveInfinity })
{
    bool rejected = false;
    try { new OptimizerOptions { RenderScale = scale }.Validate(); } catch (ArgumentException) { rejected = true; }
    Check(rejected, "invalid render scale " + scale);
}
settings.Values[Setting.RenderScale] = .9f;
lease.Write(Setting.RenderScale, .7f); lease.Restore();
Check(settings.Get(Setting.RenderScale) == .9f, "render scale restores baseline");
lease.Write(Setting.RenderScale, .7f); settings.Values[Setting.RenderScale] = .8f; lease.Restore();
Check(settings.Get(Setting.RenderScale) == .8f, "render scale preserves game override");
Console.WriteLine($"PASS: {checks} checks");

sealed class FakeSettings : ISettings
{
    public Dictionary<Setting, float> Values = new() { [Setting.LoadingPriority] = 2, [Setting.UploadTimeSlice] = 4, [Setting.TargetFps] = -1, [Setting.VSync] = 1, [Setting.ShadowDistance] = 120, [Setting.ShadowResolution] = 3, [Setting.AntiAliasing] = 8, [Setting.LodBias] = 2 };
    public Setting? Fail;
    public float Get(Setting key) => Values[key];
    public void Set(Setting key, float value) { if (key == Fail) throw new Exception("simulated setter failure"); Values[key] = value; }
}
