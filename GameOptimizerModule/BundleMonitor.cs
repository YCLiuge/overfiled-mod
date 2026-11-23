using System;
using System.Collections;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Threading;
using HarmonyLib;
using MelonLoader;
using UnityEngine;

namespace GameOptimizerModule;

/// <summary>
/// Observes bundle inventory and two internal LoadAsset entry points only.
/// Start, Tick (monotonic seconds), and Dispose must run on Unity's main thread.
/// Async counts submissions, never completion or load duration. No asset is retained.
/// </summary>
internal sealed class BundleMonitor : IDisposable
{
    private const long CountLimit = 999999999;
    private const double MillisecondLimit = 999999.9;
    private static readonly long TickLimit = Math.Max(1,
        (long)(MillisecondLimit * Stopwatch.Frequency / 1000.0));
    private static BundleMonitor? current;
    private readonly HarmonyLib.Harmony harmony = new(
        "mod.game_optimizer.bundle_monitor." + Guid.NewGuid().ToString("N"));
    private bool started, disposed, inventoryReady, inventoryWarning;
    private volatile bool syncReady, asyncReady;
    private double nextSample = double.NegativeInfinity;
    private long loaded, syncRequests, syncCompleted, asyncRequests;
    private long totalTicks, maxTicks, lastTicks;

    // A value type passed by Harmony, with no per-call heap allocation. Keeping
    // the owner in the state prevents a late postfix updating a newer monitor.
    private struct CallState
    {
        internal BundleMonitor? Owner;
        internal long Timestamp;
    }

    public void Start()
    {
        if (started || disposed) return;
        started = true;
        if (Interlocked.CompareExchange(ref current, this, null) != null)
        {
            Warn("load hooks unavailable: another monitor owns them");
            return;
        }

        syncReady = TryPatch("LoadAsset_Internal", typeof(UnityEngine.Object),
            nameof(BeforeSync), nameof(AfterSync));
        asyncReady = TryPatch("LoadAssetAsync_Internal", typeof(AssetBundleRequest),
            nameof(BeforeAsync), null);
    }

    private bool TryPatch(string name, Type returnType, string prefix, string? postfix)
    {
        MethodInfo? target = null;
        try
        {
            // These are the actual IL2CPP wrapper signatures, not System.Type.
            target = AccessTools.Method(typeof(AssetBundle), name,
                new[] { typeof(string), typeof(Il2CppSystem.Type) });
            if (target == null || target.IsStatic || target.ReturnType != returnType)
                throw new MissingMethodException("Unexpected AssetBundle." + name + " signature");
            harmony.Patch(target, prefix: new HarmonyMethod(typeof(BundleMonitor), prefix),
                postfix: postfix == null ? null : new HarmonyMethod(typeof(BundleMonitor), postfix));
            return true;
        }
        catch (Exception ex)
        {
            Warn(name + " unavailable: " + ex.Message);
            // A patch backend may fail after installing part of a hook. Remove
            // only our owner on this method; disabled callbacks are harmless.
            if (target != null)
            {
                try { harmony.Unpatch(target, HarmonyPatchType.All, harmony.Id); }
                catch (Exception cleanup) { Warn("hook cleanup failed: " + cleanup.Message); }
            }
            return false;
        }
    }

    public void Tick(double now)
    {
        if (!started || disposed || double.IsNaN(now) || double.IsInfinity(now) || now < nextSample)
            return;
        nextSample = now + 5.0;
        try
        {
            var bundles = AssetBundle.GetAllLoadedAssetBundles();
            if (bundles == null) throw new InvalidOperationException("null bundle enumeration");
            // Same standard IEnumerator wrapper used by Logic.BundleHelper.
            var enumerator = (IEnumerator)bundles.GetEnumerator();
            long count = 0;
            try
            {
                while (enumerator.MoveNext())
                    if (enumerator.Current is AssetBundle bundle && bundle != null && count < CountLimit)
                        count++;
            }
            finally { (enumerator as IDisposable)?.Dispose(); }
            loaded = count;
            inventoryReady = true;
            inventoryWarning = false;
        }
        catch (Exception ex)
        {
            inventoryReady = false;
            if (!inventoryWarning) Warn("bundle inventory unavailable: " + ex.Message);
            inventoryWarning = true;
        }
    }

    private static void BeforeSync(out CallState __state)
    {
        __state = default;
        var self = Volatile.Read(ref current);
        if (self == null || !self.syncReady) return;
        __state.Owner = self;
        __state.Timestamp = Stopwatch.GetTimestamp();
        AddBounded(ref self.syncRequests, 1, CountLimit);
    }

    private static void AfterSync(CallState __state)
    {
        var self = __state.Owner;
        if (self == null || !self.syncReady) return;
        long elapsed = Stopwatch.GetTimestamp() - __state.Timestamp;
        elapsed = Math.Clamp(elapsed, 0, TickLimit);
        AddBounded(ref self.syncCompleted, 1, CountLimit);
        AddBounded(ref self.totalTicks, elapsed, TickLimit);
        Interlocked.Exchange(ref self.lastTicks, elapsed);
        long previous = Interlocked.Read(ref self.maxTicks);
        while (elapsed > previous)
        {
            long observed = Interlocked.CompareExchange(ref self.maxTicks, elapsed, previous);
            if (observed == previous) break;
            previous = observed;
        }
    }

    private static void BeforeAsync()
    {
        var self = Volatile.Read(ref current);
        if (self != null && self.asyncReady)
            AddBounded(ref self.asyncRequests, 1, CountLimit);
    }

    private static void AddBounded(ref long field, long amount, long limit)
    {
        long previous = Interlocked.Read(ref field);
        while (previous < limit)
        {
            long next = previous + Math.Min(amount, limit - previous);
            long observed = Interlocked.CompareExchange(ref field, next, previous);
            if (observed == previous) return;
            previous = observed;
        }
    }

    private static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);
    private static string Milliseconds(ref long ticks) =>
        Math.Min(MillisecondLimit, Interlocked.Read(ref ticks) * 1000.0 / Stopwatch.Frequency)
            .ToString("F1", CultureInfo.InvariantCulture);

    /// <summary>
    /// Three English lines, each at most 55 characters. Counters saturate at
    /// 999999999 and each ms aggregate at 999999.9; snapshots are approximate.
    /// Postfix counts normal returns (including null), not exceptional exits.
    /// </summary>
    public string Status
    {
        get
        {
            string inventory = inventoryReady && !disposed ? Number(loaded) : "unavailable";
            string asyncCount = asyncReady ? Number(Interlocked.Read(ref asyncRequests)) : "unavailable";
            string sync = syncReady
                ? Number(Interlocked.Read(ref syncRequests)) + "/" + Number(Interlocked.Read(ref syncCompleted))
                : "unavailable";
            string times = syncReady
                ? Milliseconds(ref totalTicks) + "/" + Milliseconds(ref maxTicks) + "/" + Milliseconds(ref lastTicks)
                : "unavailable";
            return "Bundles: " + inventory + " | async req: " + asyncCount
                + "\nSync req/done: " + sync
                + "\nSync ms total/max/last: " + times;
        }
    }

    private static void Warn(string message) => MelonLogger.Warning("[GameOptimizer] BundleMonitor: " + message);

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        syncReady = asyncReady = false;
        try { harmony.UnpatchSelf(); }
        catch (Exception ex) { Warn("unpatch failed: " + ex.Message); }
        finally
        {
            Interlocked.CompareExchange(ref current, null, this);
            inventoryReady = false;
        }
    }
}
