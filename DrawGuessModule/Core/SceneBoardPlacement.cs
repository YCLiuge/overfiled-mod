using System;

namespace DrawGuessModule.Core;

/// <summary>Public scene placement only. Contains no word, drawing permission or score.</summary>
public sealed class SceneBoardPlacement
{
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    public float Yaw { get; set; }

    public bool IsValid => float.IsFinite(X) && float.IsFinite(Y) && float.IsFinite(Z) &&
        float.IsFinite(Yaw) && Math.Abs(X) <= 100000 && Math.Abs(Y) <= 100000 && Math.Abs(Z) <= 100000 && Math.Abs(Yaw) <= 360;

    public SceneBoardPlacement Copy() => new() { X = X, Y = Y, Z = Z, Yaw = Yaw };
}
