using System;
using System.Globalization;
using System.Reflection;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using MelonLoader;
using UnityEngine;

namespace GameOptimizerModule;

/// <summary>Call Tick on the Unity thread with a monotonic clock in seconds.</summary>
internal sealed class FrameDiagnostics
{
    // This game's generated Resolution exposes only refreshRate. Cache discovery of
    // the newer fractional API, without a compile-time dependency on that member.
    private PropertyInfo? refreshRatio;
    private PropertyInfo? refreshValue;
    private bool refreshProbed;
    private Il2CppStructArray<FrameTiming>? timings;
    private bool timingFailed, warned;
    private double nextUpdate = double.NegativeInfinity;
    private double started = double.NaN;
    private double lastGpuAt = double.NegativeInfinity, lastCpuAt = double.NegativeInfinity;
    private ulong lastGpuStamp, lastCpuStamp;
    private double gpuMs, cpuMs;
    private uint latestCount;
    private FrameTiming latest;
    private string timingReason = "waiting";

    public string Status { get; private set; } = "Unity cap N/A | VSync N/A (app setting)\nDisplay N/A | VSync ceiling N/A\nGPU N/A | CPU timing N/A";

    public void Tick(double now)
    {
        // Capture is asynchronous: request every frame, never wait or poll for it.
        // Lazy allocation also keeps unsupported native APIs out of the constructor.
        if (!timingFailed)
        {
            try
            {
                timings ??= new Il2CppStructArray<FrameTiming>(1L);
                FrameTimingManager.CaptureFrameTimings();
            }
            catch (Exception ex)
            {
                timingFailed = true;
                WarnOnce("FrameTiming API 不可用，GPU/CPU 显示 N/A: " + ex.Message);
            }
        }

        if (!Finite(now)) return;
        if (double.IsNaN(started)) started = now;
        if (now < nextUpdate && nextUpdate - now <= .5) return;
        nextUpdate = now + .5;

        if (!timingFailed)
        {
            try
            {
                latestCount = FrameTimingManager.GetLatestTimings(1u, timings!);
                latest = latestCount == 0 ? default : timings![0];
                if (latestCount != 0)
                {
                    FrameTiming frame = latest;
                    // These are native CPU timestamps, not Stopwatch-derived GPU data.
                    // Zero timestamps and repeated/stale frames cannot refresh freshness.
                    ulong stamp = frame.cpuTimeFrameComplete != 0
                        ? frame.cpuTimeFrameComplete : frame.cpuTimePresentCalled;
                    if (stamp > lastGpuStamp && Positive(frame.gpuFrameTime))
                    {
                        lastGpuStamp = stamp;
                        gpuMs = frame.gpuFrameTime;
                        lastGpuAt = now;
                    }
                    if (stamp > lastCpuStamp && Positive(frame.cpuFrameTime))
                    {
                        lastCpuStamp = stamp;
                        cpuMs = frame.cpuFrameTime;
                        lastCpuAt = now;
                    }
                }
            }
            catch (Exception ex)
            {
                timingFailed = true;
                WarnOnce("FrameTiming API 不可用，GPU/CPU 显示 N/A: " + ex.Message);
            }
        }

        bool gpuFresh = !timingFailed && now >= lastGpuAt && now - lastGpuAt <= 2;
        bool cpuFresh = !timingFailed && now >= lastCpuAt && now - lastCpuAt <= 2;
        timingReason = timingFailed ? "API error"
            : latestCount == 0 ? "no samples"
            : !Positive(latest.gpuFrameTime) ? "no GPU data"
            : latest.cpuTimeFrameComplete == 0 && latest.cpuTimePresentCalled == 0 ? "no timestamp"
            : "stale";
        if (!gpuFresh && now - started > 2 && !warned)
        {
            // Frequencies are diagnostic only: nonzero does not prove statistics are enabled.
            string gpuHz = "N/A", cpuHz = "N/A";
            try { gpuHz = FrameTimingManager.GetGpuTimerFrequency().ToString(CultureInfo.InvariantCulture); } catch { }
            try { cpuHz = FrameTimingManager.GetCpuTimerFrequency().ToString(CultureInfo.InvariantCulture); } catch { }
            WarnOnce("GPU FrameTiming unavailable: reason=" + timingReason
                + "; returned=" + latestCount + "; rawGpuMs=" + Number(latest.gpuFrameTime)
                + "; rawCpuMs=" + Number(latest.cpuFrameTime)
                + "; complete=" + latest.cpuTimeFrameComplete + "; present=" + latest.cpuTimePresentCalled
                + "; gpuHz=" + gpuHz + "; cpuHz=" + cpuHz
                + ". No runtime enable API in this game's bindings. No samples may mean Frame Timing Stats is disabled or unsupported; nonzero frequency alone does not prove support. Rendering optimization remains active.");
        }

        string target = "N/A", sync = "N/A", refresh = "N/A", ceiling = "N/A";
        int vsync = -1;
        try
        {
            int cap = Application.targetFrameRate;
            target = cap > 0 ? cap.ToString(CultureInfo.InvariantCulture)
                : "default(" + cap.ToString(CultureInfo.InvariantCulture) + ")";
        }
        catch (Exception ex) { WarnOnce("读取 targetFrameRate 失败: " + ex.Message); }
        try
        {
            vsync = QualitySettings.vSyncCount;
            sync = vsync.ToString(CultureInfo.InvariantCulture);
        }
        catch (Exception ex) { WarnOnce("读取 vSyncCount 失败: " + ex.Message); }
        try
        {
            double hz = RefreshHz();
            if (Positive(hz))
            {
                refresh = Number(hz) + " Hz";
                if (vsync > 0) ceiling = Number(hz / vsync) + " FPS";
            }
        }
        catch (Exception ex) { WarnOnce("读取显示器刷新率失败: " + ex.Message); }
        if (vsync == 0) ceiling = "OFF";

        // Unity settings and the theoretical VSync ceiling do not detect driver caps.
        Status = "Unity cap " + target + " | VSync " + sync + " (app setting)"
            + "\nDisplay " + refresh + " | VSync ceiling " + ceiling
            + "\nGPU " + (gpuFresh ? Number(gpuMs) + " ms" : "N/A (" + timingReason + ")")
            + " | CPU timing " + (cpuFresh ? Number(cpuMs) + " ms" : "N/A");
    }

    private double RefreshHz()
    {
        Resolution resolution = Screen.currentResolution;
        if (!refreshProbed)
        {
            refreshProbed = true;
            refreshRatio = typeof(Resolution).GetProperty("refreshRateRatio", BindingFlags.Public | BindingFlags.Instance);
            refreshValue = refreshRatio?.PropertyType.GetProperty("value", BindingFlags.Public | BindingFlags.Instance);
        }
        if (refreshRatio != null && refreshValue != null)
        {
            try
            {
                object? ratio = refreshRatio.GetValue(resolution);
                object? value = ratio == null ? null : refreshValue.GetValue(ratio);
                if (value is double hz && Positive(hz)) return hz;
            }
            catch
            {
                // A mismatched generated wrapper may expose a missing native member.
                // Stop invoking it and use the legacy, integer-Hz compatibility API.
                refreshRatio = null;
                refreshValue = null;
                WarnOnce("refreshRateRatio 不可用，改用 refreshRate 整数刷新率。");
            }
        }
#pragma warning disable CS0618 // Required by the game's real generated Unity interface.
        return resolution.refreshRate;
#pragma warning restore CS0618
    }

    private void WarnOnce(string message)
    {
        if (warned) return;
        warned = true;
        // Diagnostics must never break the rest of the panel, even if logging fails.
        try { MelonLogger.Warning("[GameOptimizer] FrameDiagnostics: " + message); }
        catch { }
    }

    private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    private static bool Positive(double value) => value > 0 && Finite(value);
    private static string Number(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}
