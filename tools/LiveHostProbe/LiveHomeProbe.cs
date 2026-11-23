using System.Reflection;
using System.Text.Json;

namespace Verification;

/// <summary>Main-thread integration probe. Writes evidence even when a game command fails.</summary>
public static class LiveHomeProbe
{
    private const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    private static System.Collections.IList? _clipboard;
    private static object?[]? _savedItems;
    public static void Run(string action, string outputPath, string modulePath)
    {
        var report = new Dictionary<string, object?> { ["Action"] = action, ["TimestampUtc"] = DateTime.UtcNow };
        try
        {
            var loaderType = Find("ModLoader.LogicLoader");
            object loader = loaderType.GetProperty("Instance", Flags)!.GetValue(null)!;
            if (action == "baseline")
            {
                var auto = Find("mod.AutoHomeItem");
                object owner = auto.GetField("i", Flags)!.GetValue(null)!;
                _clipboard = (System.Collections.IList)Member(owner, "items")!;
                _savedItems = _clipboard.Cast<object?>().ToArray();
                report["SavedItems"] = _savedItems.Length;
            }
            else if (action == "restore")
            {
                if (_clipboard != null && _savedItems != null)
                {
                    _clipboard.Clear();
                    foreach (var item in _savedItems) _clipboard.Add(item);
                    report["RestoredItems"] = _clipboard.Count;
                }
                _clipboard = null; _savedItems = null;
            }
            else if (action == "load")
            {
                bool loaded = (bool)loaderType.GetMethod("LoadModuleFromFile", new[] { typeof(string), typeof(string), typeof(int) })!
                    .Invoke(loader, new object[] { "home_editor", modulePath, 80 })!;
                if (!loaded) throw new InvalidOperationException("Home editor load failed; inspect game log");
            }
            else if (action == "unload")
                loaderType.GetMethod("UnloadModule", new[] { typeof(string) })!.Invoke(loader, new object[] { "home_editor" });
            else if (action == "command")
            {
                var coreType = Find("mod.TipMsgCore");
                object core = coreType.GetField("i", Flags)!.GetValue(null)!;
                var command = coreType.GetMethods(Flags).Single(m => m.Name == "command" && m.GetParameters().Length == 3);
                command.Invoke(core, new[] { Member(core, "mainplayername"), Uri.UnescapeDataString(modulePath), Enum.Parse(command.GetParameters()[2].ParameterType, "Text") });
            }
            else if (action != "snapshot") throw new ArgumentException("Use load, unload, snapshot, command");
            var instance = loaderType.GetMethod("GetModule", new[] { typeof(string) })!.Invoke(loader, new object[] { "home_editor" });
            var entry = instance?.GetType();
            report["ModuleLoaded"] = instance != null;
            report["EntryAssembly"] = entry?.Assembly.FullName;
            report["Diagnostics"] = entry?.GetMethod("Diagnostics", Flags)?.Invoke(null, null)?.ToString();
            report["SceneObjects"] = SceneObjects();
            report["Success"] = true;
        }
        catch (Exception e) { report["Success"] = false; report["Error"] = e.ToString(); throw; }
        finally
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
            File.WriteAllText(outputPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        }
    }
    private static Type Find(string name) => AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(name, false)).FirstOrDefault(t => t != null)
        ?? throw new TypeLoadException(name);
    private static object? Member(object? obj, string name) => obj == null ? null : obj.GetType().GetProperty(name, Flags)?.GetValue(obj) ?? obj.GetType().GetField(name, Flags)?.GetValue(obj);
    private static object? Vector(object? obj) => obj == null ? null : new { x = Member(obj, "x"), y = Member(obj, "y"), z = Member(obj, "z") };
    private static object SceneObjects()
    {
        var gameObject = Find("UnityEngine.GameObject");
        var find = gameObject.GetMethod("Find", new[] { typeof(string) })!;
        return new[] { "-------------------------------------------场景/---------------------主建筑/主城_喷泉_B", "放置网格/PlacementGrid_9999(Clone)/Grid_1" }
            .Select(name =>
            {
                object? obj = find.Invoke(null, new object[] { name });
                object? tr = Member(obj, "transform");
                return new { name, found = obj != null, position = Vector(Member(tr, "position")), scale = Vector(Member(tr, "localScale")), active = Member(obj, "activeSelf") };
            }).ToArray();
    }
}
