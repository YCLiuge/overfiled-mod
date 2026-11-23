using LocalHomeModule;
using System.Numerics;

int checks = 0;
void Assert(bool value, string message) { checks++; if (!value) throw new Exception(message); }
void Near(double actual, double expected, string message, double tolerance = .0001) => Assert(Math.Abs(actual - expected) <= tolerance, $"{message}: {actual} vs {expected}");
void Reject(Action action, string message)
{
    try { action(); } catch (ArgumentException) { checks++; return; } catch (InvalidDataException) { checks++; return; }
    throw new Exception("Expected rejection: " + message);
}
Vector3 V((float X, float Y, float Z) p) => new(p.X, p.Y, p.Z);
// Independent axis-by-axis reference, avoiding the production quaternion implementation.
Vector3 AxisEuler(Vector3 p, float pitch, float yaw, float roll)
{
    double x = pitch * Math.PI / 180, y = yaw * Math.PI / 180, z = roll * Math.PI / 180;
    double a = p.X * Math.Cos(z) - p.Y * Math.Sin(z), b = p.X * Math.Sin(z) + p.Y * Math.Cos(z), c = p.Z;
    double d = b * Math.Cos(x) - c * Math.Sin(x), e = b * Math.Sin(x) + c * Math.Cos(x);
    return new((float)(a * Math.Cos(y) + e * Math.Sin(y)), (float)d, (float)(-a * Math.Sin(y) + e * Math.Cos(y)));
}
void VectorNear(Vector3 actual, Vector3 expected, string name, double tolerance = .0001) => Near(Vector3.Distance(actual, expected), 0, name, tolerance);
var zero = new EditorOrigin();
Assert(zero.Valid && new EditorOrigin(100000, -100000, Pitch: 100000).Valid, "valid bounds");
Assert(!new EditorOrigin(float.NaN).Valid && !new EditorOrigin(Yaw: float.PositiveInfinity).Valid && !new EditorOrigin(100001).Valid, "invalid origins");
var rounded = new EditorOrigin(1.125f, -1.125f, -.001f).Rounded();
Assert(rounded.X == 1.13f && rounded.Y == -1.13f && BitConverter.SingleToInt32Bits(rounded.Z) == 0, "rounding and positive zero");
var line = HomeGeometry.Generate("line", 4, 2, 0, zero);
Assert(line.Select(p => p.X).SequenceEqual(new[] { -3f, -1f, 1f, 3f }), "centered line");
Assert(line.All(p => p.Y == 0 && p.Z == 0), "line on X");
var circle = HomeGeometry.Generate("circle", 4, 0, 3, zero);
VectorNear(V(circle[0]), new(3, 0, 0), "circle first");
VectorNear(V(circle[1]), new(0, 0, 3), "circle quarter");
foreach (int n in new[] { 1, 2, 13, 10000 })
{
    var sphere = HomeGeometry.Generate("sphere", n, 0, 15, zero);
    Assert(sphere.Length == n, "sphere count");
    foreach (var p in sphere) Near(V(p).Length(), 15, "sphere radius", .00001);
    if (n > 1) Assert(sphere.Select(p => p.Y).Distinct().Count() == n, "Fibonacci latitudes unique");
}
var grid = HomeGeometry.Generate("grid", 7, 2, 0, zero);
Assert(grid.Length == 7 && grid.Select(p => p.X).Distinct().Count() == 3 && grid.Select(p => p.Z).Distinct().Count() == 3, "near-square grid");
Near(grid.Max(p => p.X) + grid.Min(p => p.X), 0, "grid X bounds centered");
Near(grid.Max(p => p.Z) + grid.Min(p => p.Z), 0, "grid Z bounds centered");
foreach (string shape in new[] { "line", "circle", "sphere", "grid" })
{
    foreach (int n in new[] { 1, 2, 10000 })
    {
        var points = HomeGeometry.Generate(shape, n, .1f, 1, new(1, 2, 3, 90, -100000, 100000));
        Assert(points.Length == n && points.All(p => float.IsFinite(p.X) && float.IsFinite(p.Y) && float.IsFinite(p.Z)), "boundary generation " + shape);
    }
    var pointsZero = HomeGeometry.Generate(shape, 3, 0, 0, new(100000, -100000, 100000));
    Assert(pointsZero.All(p => p == (100000f, -100000f, 100000f)), "zero geometry at boundary");
}
var origin = new EditorOrigin(10, 20, 30, 23, 47, 69);
var rotatedCircle = HomeGeometry.Generate("circle", 4, 0, 3, origin);
VectorNear(V(rotatedCircle[0]), AxisEuler(new(3, 0, 0), 23, 47, 69) + new Vector3(10, 20, 30), "Unity ZXY generation");
var source = new[] { new Placement(19, 42, 11, 20, 30, 0) };
var turned = HomeGeometry.Transform(source, origin, 1, 2, 3, 0, 90, 0, 2)[0];
Assert(turned.Id == 19 && turned.Item == 42 && turned.Scale == 2, "preserve identifiers and multiply scale");
VectorNear(new(turned.X, turned.Y, turned.Z), new(11, 22, 31), "pivot scale rotate translate");
Assert(source[0].X == 11 && source[0].Scale == 1, "input immutable");
var random = new Random(2917);
for (int i = 0; i < 500; i++)
{
    float A() => (float)(random.NextDouble() * 720 - 360);
    float pitch = i < 4 ? new[] { 90f, -90f, 89.999f, -89.999f }[i] : A();
    float yaw = A(), roll = A(), rx = A(), ry = A(), rz = A();
    if (i < 4) rx = ry = rz = 0;
    var input = new Placement(1, 1, 3, -2, 5, yaw, pitch, roll);
    var output = HomeGeometry.Transform(new[] { input }, zero, 0, 0, 0, rx, ry, rz, 1)[0];
    foreach (var basis in new[] { Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ })
    {
        var expected = AxisEuler(AxisEuler(basis, pitch, yaw, roll), rx, ry, rz);
        VectorNear(AxisEuler(basis, output.Pitch, output.Yaw, output.Roll), expected, "composed Euler orientation", .0004);
    }
}
var original = new[] { new Placement(1, 12, 13, -4, 7, 23, 47, 69, 2) };
var pivot = new EditorOrigin(2, 3, 4);
var moved = HomeGeometry.Transform(original, pivot, 5, -7, 9, 21, 43, 65, 1.25f);
moved = HomeGeometry.Transform(moved, pivot, -5, 7, -9, 0, 0, 0, 1);
moved = HomeGeometry.Transform(moved, pivot, 0, 0, 0, 0, -43, 0, 1);
moved = HomeGeometry.Transform(moved, pivot, 0, 0, 0, -21, 0, 0, 1);
moved = HomeGeometry.Transform(moved, pivot, 0, 0, 0, 0, 0, -65, .8f);
VectorNear(new(moved[0].X, moved[0].Y, moved[0].Z), new(13, -4, 7), "inverse position", .025);
Near(moved[0].Scale, 2, "inverse scale");
foreach (var basis in new[] { Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ })
    VectorNear(AxisEuler(basis, moved[0].Pitch, moved[0].Yaw, moved[0].Roll), AxisEuler(basis, 47, 23, 69), "inverse orientation", .0006);
foreach (float bad in new[] { float.NaN, float.PositiveInfinity, -1, 100001 })
{
    Reject(() => HomeGeometry.Generate("line", 1, bad, 0, zero), "spacing");
    Reject(() => HomeGeometry.Generate("line", 1, 0, bad, zero), "radius");
}
foreach (int n in new[] { -1, 0, 10001 }) Reject(() => HomeGeometry.Generate("line", n, 0, 0, zero), "count");
Reject(() => HomeGeometry.Generate("invalid", 1, 0, 0, zero), "shape");
Reject(() => HomeGeometry.Generate(null!, 1, 0, 0, zero), "null shape");
Reject(() => HomeGeometry.Generate("line", 1, 0, 0, null!), "null origin");
Reject(() => HomeGeometry.Generate("line", 4, 100000, 0, zero), "output coordinate limit");
foreach (float scale in new[] { .009f, 100.01f, float.NaN, float.PositiveInfinity })
    Reject(() => HomeGeometry.Transform(source, zero, 0, 0, 0, 0, 0, 0, scale), "scale");
foreach (float bad in new[] { float.NaN, float.PositiveInfinity, 100001f })
{
    Reject(() => HomeGeometry.Transform(source, zero, bad, 0, 0, 0, 0, 0, 1), "translation");
    Reject(() => HomeGeometry.Transform(source, zero, 0, 0, 0, 0, bad, 0, 1), "rotation");
}
Reject(() => HomeGeometry.Transform(source, zero, 100000, 0, 0, 0, 0, 0, 1), "transformed bounds");
Reject(() => HomeGeometry.Transform(new[] { source[0], source[0] }, zero, 0, 0, 0, 0, 0, 0, 1), "duplicate IDs");
Reject(() => HomeGeometry.Transform(new[] { source[0] with { Scale = 100 } }, zero, 0, 0, 0, 0, 0, 0, 2), "result scale limit");
foreach (float scale in new[] { .01f, 100f })
{
    var valid = HomeGeometry.Transform(new[] { new Placement(1, 1, 0, 0, 0, 100000) }, zero, 0, 0, 0, 100000, -100000, 100000, scale);
    Assert(valid.All(p => p.Valid), "inclusive scale and angle boundaries");
}
var noncanonical = new Placement(7, 23, 13, -4, 7, 725, 120, -540, 2);
foreach (var rotation in new[] { (X: 0f, Y: 0f, Z: 0f), (X: 360f, Y: -720f, Z: 1080f) })
{
    var identity = HomeGeometry.Transform(new[] { noncanonical }, pivot, 0, 0, 0, rotation.X, rotation.Y, rotation.Z, 1)[0];
    Assert(identity == noncanonical, "identity preserves noncanonical Placement exactly");
    var translated = HomeGeometry.Transform(new[] { noncanonical }, pivot, 1, 2, 3, rotation.X, rotation.Y, rotation.Z, 1)[0];
    Assert(translated == noncanonical with { X = 14, Y = -2, Z = 10 }, "translation preserves noncanonical Euler values");
    var scaled = HomeGeometry.Transform(new[] { noncanonical }, pivot, 0, 0, 0, rotation.X, rotation.Y, rotation.Z, 2)[0];
    Assert(scaled == noncanonical with { X = 24, Y = -11, Z = 10, Scale = 4 }, "scaling preserves noncanonical Euler values");
}
var many = Enumerable.Range(1, 10001).Select(id => new Placement(id, 1, 0, 0, 0, 0)).ToArray();
Assert(HomeGeometry.Transform(many, zero, 0, 0, 0, 0, 0, 0, 1).Length == 10001, "no total furniture cap");
Assert(HomeGeometry.Transform(Array.Empty<Placement>(), zero, 0, 0, 0, 0, 0, 0, 1).Length == 0, "empty transform");
checks += MoreShapes.Run();
checks += PatternOrientation.Run();
Console.WriteLine($"PASS: {checks} geometry checks (24 shapes, OrientPattern, FaceOrigin, Unity ZXY, pivot, inverse, validation and finite boundaries).");
