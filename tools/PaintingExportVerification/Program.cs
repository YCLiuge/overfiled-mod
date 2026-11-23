using System.Text.Json;
using DrawGuessModule;
using DrawGuessModule.Core;

int checks = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
    checks++;
}
void Reject<T>(Action action) where T : Exception
{
    try { action(); }
    catch (T) { checks++; return; }
    throw new Exception("Expected " + typeof(T).Name);
}

Reject<ArgumentNullException>(() => PngWriter.EncodeRgba(1, 1, null!));
foreach (int invalid in new[] { int.MinValue, -1, 0, 16385, int.MaxValue })
{
    Reject<ArgumentOutOfRangeException>(() => PngWriter.EncodeRgba(invalid, 1, Array.Empty<byte>()));
    Reject<ArgumentOutOfRangeException>(() => PngWriter.EncodeRgba(1, invalid, Array.Empty<byte>()));
}
Reject<ArgumentOutOfRangeException>(() => PngWriter.EncodeRgba(16384, 16384, Array.Empty<byte>()));
Reject<ArgumentOutOfRangeException>(() => PngWriter.EncodeRgba(8193, 8192, Array.Empty<byte>()));
foreach (int length in new[] { 0, 3, 5, 7, 15, 17 })
    Reject<ArgumentException>(() => PngWriter.EncodeRgba(2, 2, new byte[length]));

string fixtureDirectory = Path.Combine(AppContext.BaseDirectory, "fixtures");
Directory.CreateDirectory(fixtureDirectory);
var fixtures = new List<object>();
void AddFixture(string label, int width, int height, byte[] bottomUp, string? path = null)
{
    if (path == null)
    {
        path = Path.Combine(fixtureDirectory, label + ".png");
        File.WriteAllBytes(path, PngWriter.EncodeRgba(width, height, bottomUp));
    }
    fixtures.Add(new { label, width, height, path, bottomUp = Convert.ToBase64String(bottomUp) });
}
byte[] corners = { 255, 0, 0, 255, 0, 255, 0, 128, 0, 0, 255, 0, 17, 34, 51, 67 };
AddFixture("corners", 2, 2, corners);
AddFixture("one_pixel", 1, 1, new byte[] { 13, 27, 39, 55 });
var random = new Random(7821);
foreach (var size in new[] { (1, 7), (9, 1), (3, 2), (17, 11), (512, 512), (16384, 1), (1, 16384) })
{
    var bytes = new byte[size.Item1 * size.Item2 * 4];
    random.NextBytes(bytes);
    AddFixture("random_" + size.Item1 + "x" + size.Item2, size.Item1, size.Item2, bytes);
}

PaintingSystem.AcceptedPixels = new[]
{
    new TestColor32(255, 0, 0, 255), new TestColor32(0, 255, 0, 128),
    new TestColor32(0, 0, 255, 0), new TestColor32(17, 34, 51, 67)
};
string expectedDirectory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "UserData", "DrawGuess", "Paintings"));
var names = new string?[] { null, "", "   ", "../../escape", @"..\..\escape", "C:\\temp\\bad:name?.png", "CON", "..", "\0\r\n", "星空画作", new string('x', 1000) };
var saved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
foreach (string? name in names)
{
    int before = PaintingSystem.CopyCount;
    string path = PaintingSystem.SaveLocalPainting(name);
    Check(PaintingSystem.CopyCount == before + 1, "Must take exactly one detached accepted-buffer snapshot");
    Check(Path.IsPathFullyQualified(path), "Export path must be absolute");
    Check(Path.GetDirectoryName(path) == expectedDirectory, "Export escaped its fixed directory");
    Check(Path.GetExtension(path) == ".png" && File.Exists(path), "Missing PNG");
    string filename = Path.GetFileNameWithoutExtension(path);
    // Prefix <=64 plus _yyyyMMdd_HHmmss_fff_<32-digit GUID> (53 characters).
    Check(filename.Length <= 64 + 53, "Name prefix is not bounded");
    Check(!filename.Contains('.') && !filename.Contains('/') && !filename.Contains('\\') && !filename.Contains(':'), "Unsafe filename");
    Check(saved.Add(path), "Save overwrote an existing result");
    AddFixture("export_" + saved.Count, 2, 2, corners, path);
}

// A subsequent accepted-buffer change must be saved immediately, with no texture/upload step.
PaintingSystem.AcceptedPixels[0] = new TestColor32(9, 8, 7, 6);
byte[] changed = (byte[])corners.Clone();
changed[0] = 9; changed[1] = 8; changed[2] = 7; changed[3] = 6;
string changedPath = PaintingSystem.SaveLocalPainting("fresh");
AddFixture("accepted_change", 2, 2, changed, changedPath);

var parallelPaths = new string[32];
Parallel.For(0, parallelPaths.Length, i => parallelPaths[i] = PaintingSystem.SaveLocalPainting("same_name"));
Check(parallelPaths.Distinct(StringComparer.OrdinalIgnoreCase).Count() == parallelPaths.Length, "Concurrent saves collided");
foreach (string path in parallelPaths) Check(File.Exists(path), "Concurrent save is missing");

string manifest = Path.Combine(fixtureDirectory, "manifest.json");
File.WriteAllText(manifest, JsonSerializer.Serialize(fixtures));
Console.WriteLine($"PASS: {checks} C# validation/export checks; {fixtures.Count} PNG fixtures; 32 concurrent saves.");
Console.WriteLine("MANIFEST " + manifest);
