using System.Diagnostics;
using System.Reflection;
using HarmonyLib;
using MelonLoader;
using ModLoader;
using UnityEngine;

namespace GameOptimizerModule;

internal sealed class BundleOptimizer : IDisposable
{
    private const string Owner = "mod.game_optimizer.bundles";
    private readonly HarmonyLib.Harmony harmony = new(Owner);
    private static BundleOptimizer? current;
    private readonly BundleMetadataCache cache = new();
    private readonly object gate = new();
    private MethodInfo? listAssets;
    private bool enabled, hooksReady;
    private long generation;
    private static double Now => (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency;
    public string Status { get { lock (gate) return $"Helper cache {(enabled && hooksReady && listAssets != null ? "ON" : "OFF")}: hit {cache.Hits} / miss {cache.Misses}\nCached names {cache.NameCount}/32768 ({cache.Count}/64 bundles)"; } }
    public void Start()
    {
        current = this;
        try
        {
            // Invalidate metadata before any known public unload entry point.
            PatchUnload("Unload", new[] { typeof(bool) });
            PatchUnload("UnloadAllAssetBundles", new[] { typeof(bool) });
            var asyncUnload = AccessTools.Method(typeof(AssetBundle), "UnloadAsync", new[] { typeof(bool) });
            if (asyncUnload != null) harmony.Patch(asyncUnload, prefix: new HarmonyMethod(typeof(BundleOptimizer), nameof(Invalidate)));
            hooksReady = true;
        }
        catch (Exception ex)
        {
            harmony.UnpatchSelf(); hooksReady = false;
            MelonLogger.Warning("[GameOptimizer] bundle cache unavailable: " + ex.Message);
        }
        LogicLoader.Instance.OnModuleLoaded += Loaded;
        LogicLoader.Instance.OnModuleUnloaded += Unloaded;
        Attach(LogicLoader.Instance.GetModuleInfo("logic_main")?.Assembly);
    }
    public void Configure(bool value) { enabled = value; Invalidate(); }
    private void PatchUnload(string name, Type[] args)
    {
        var method = AccessTools.Method(typeof(AssetBundle), name, args) ?? throw new MissingMethodException(name);
        harmony.Patch(method, prefix: new HarmonyMethod(typeof(BundleOptimizer), nameof(Invalidate)));
    }
    private static void Invalidate() { var self = current; if (self != null) lock (self.gate) { self.generation++; self.cache.Clear(); } }
    private void Loaded(string id, ILogic logic) { if (id == "logic_main") Attach(LogicLoader.Instance.GetModuleInfo(id)?.Assembly); }
    private void Unloaded(string id)
    {
        if (id != "logic_main") return;
        if (listAssets != null) harmony.Unpatch(listAssets, HarmonyPatchType.All, Owner);
        listAssets = null; Invalidate();
    }
    private void Attach(Assembly? assembly)
    {
        if (!hooksReady || assembly == null || listAssets != null) return;
        try
        {
            var method = assembly.GetType("Logic.BundleHelper")?.GetMethod("ListAssets", new[] { typeof(AssetBundle), typeof(bool) });
            if (method == null || method.ReturnType != typeof(string[])) return;
            harmony.Patch(method, new HarmonyMethod(typeof(BundleOptimizer), nameof(Before)), new HarmonyMethod(typeof(BundleOptimizer), nameof(After)));
            listAssets = method;
        }
        catch (Exception ex) { MelonLogger.Warning("[GameOptimizer] bundle helper patch failed: " + ex.Message); }
    }
    private static bool Before(AssetBundle __0, bool __1, ref string[] __result, out long __state)
    {
        __state = -1;
        var self = current;
        if (self == null || !self.enabled || !self.hooksReady || __0 == null) return true;
        try
        {
            lock (self.gate)
            {
            if (!__1 && self.cache.TryGet(__0.Pointer.ToInt64(), __0.GetInstanceID(), Now, out var names))
            { __result = names; return false; }
            __state = self.generation;
            }
        }
        catch { Invalidate(); }
        return true;
    }
    private static void After(AssetBundle __0, string[] __result, long __state)
    {
        var self = current;
        if (self == null || !self.enabled || __state < 0 || __state != self.generation || __0 == null || __result == null || __result.Length == 0) return;
        try { lock (self.gate) { if (__state == self.generation && self.enabled) self.cache.Store(__0.Pointer.ToInt64(), __0.GetInstanceID(), Now, __result); } }
        catch { Invalidate(); }
    }
    public void Dispose()
    {
        enabled = false;
        LogicLoader.Instance.OnModuleLoaded -= Loaded;
        LogicLoader.Instance.OnModuleUnloaded -= Unloaded;
        try { harmony.UnpatchSelf(); }
        finally { listAssets = null; lock (gate) cache.Clear(); if (current == this) current = null; }
    }
}
