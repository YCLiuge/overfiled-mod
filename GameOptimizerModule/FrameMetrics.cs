using System;

namespace GameOptimizerModule;

/// <summary>Last 512 valid frame durations, in seconds. Call on one thread.</summary>
public sealed class FrameMetrics
{
    public const int Capacity = 512;
    private readonly double[] samples = new double[Capacity];
    private readonly double[] sorted = new double[Capacity];
    private int next, count;

    /// <summary>Accepts every finite positive duration, including loading stalls.</summary>
    public void Add(double delta)
    {
        if (double.IsNaN(delta) || double.IsInfinity(delta) || delta <= 0) return;
        samples[next] = delta;
        next = (next + 1) % Capacity;
        if (count < Capacity) count++;
    }

    /// <summary>
    /// Computes FPS as 1 / mean duration and P95 by nearest rank (ceil(.95 * N)).
    /// No new sample arrays are allocated; sorting happens only here, never in Add.
    /// An empty window returns zero values. Durations in the result are milliseconds.
    /// </summary>
    public FrameMetricsSnapshot Snapshot()
    {
        if (count == 0) return default;
        Array.Copy(samples, sorted, count);
        Array.Sort(sorted, 0, count);
        // Incremental mean avoids overflowing a sum of long but valid durations.
        double mean = 0;
        for (int i = 0; i < count; i++) mean += (sorted[i] - mean) / (i + 1);
        int rank = (int)Math.Ceiling(count * .95) - 1;
        return new FrameMetricsSnapshot(count, 1 / mean, mean * 1000, sorted[rank] * 1000);
    }
}

public readonly struct FrameMetricsSnapshot
{
    public int SampleCount { get; }
    public double Fps { get; }
    public double AverageFrameMilliseconds { get; }
    public double P95FrameMilliseconds { get; }

    public FrameMetricsSnapshot(int sampleCount, double fps, double averageFrameMilliseconds, double p95FrameMilliseconds)
    {
        SampleCount = sampleCount;
        Fps = fps;
        AverageFrameMilliseconds = averageFrameMilliseconds;
        P95FrameMilliseconds = p95FrameMilliseconds;
    }
}
