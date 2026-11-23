using System;

// Deterministic camera path, independent of Unity and suitable for offline verification.
internal sealed class OrbitShotPlan
{
    public readonly float Duration, Radius, Height, SweepDegrees, StartRadians;
    public readonly float CenterX, CenterY, CenterZ;
    public const float LookHeight = 1.2f;

    public OrbitShotPlan(float duration, float radius, float height, float sweepDegrees,
        float startRadians, float centerX, float centerY, float centerZ)
    {
        RequireRange(duration, 2f, 120f, "拍摄时长（秒）");
        RequireRange(radius, 1f, 50f, "环绕半径");
        RequireRange(height, 0.5f, 20f, "镜头离地高度");
        RequireRange(sweepDegrees, -360f, 360f, "环绕角度");
        if (Math.Abs(sweepDegrees) < 1f) throw new ArgumentException("环绕角度绝对值至少为 1 度");
        if (!Finite(startRadians) || !Finite(centerX) || !Finite(centerY) || !Finite(centerZ))
            throw new ArgumentException("镜头中心和起始角度必须是有限数字");
        Duration = duration; Radius = radius; Height = height; SweepDegrees = sweepDegrees;
        StartRadians = startRadians; CenterX = centerX; CenterY = centerY; CenterZ = centerZ;
    }

    public (float x, float y, float z) Sample(float elapsed)
    {
        if (!Finite(elapsed)) throw new ArgumentException("镜头时间必须是有限数字");
        double t = Math.Clamp((double)elapsed / Duration, 0d, 1d);
        // Smoothstep has zero angular velocity at both ends, with no overshoot.
        double eased = t * t * (3d - 2d * t);
        double angle = StartRadians + SweepDegrees * (Math.PI / 180d) * eased;
        return (CenterX + (float)Math.Sin(angle) * Radius, CenterY + Height,
            CenterZ + (float)Math.Cos(angle) * Radius);
    }

    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    private static void RequireRange(float value, float min, float max, string name)
    {
        if (!Finite(value) || value < min || value > max)
            throw new ArgumentException($"{name}必须在 {min} 到 {max} 之间");
    }
}
