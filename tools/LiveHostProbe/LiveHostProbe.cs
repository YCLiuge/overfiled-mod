using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace Verification;

/// <summary>Invoke on the game's command/main thread. No game dependencies are referenced.</summary>
public static class LiveHostProbe
{
    private const BindingFlags Members = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    private static readonly object Gate = new();
    private static string? _drawGuessPath;
    private static int _drawGuessPriority = 100;

    /// <summary>Actions: snapshot, unload (draw_guess), reload (draw_guess). Always writes a JSON report.</summary>
    public static void Run(string action, string outputPath)
    {
        lock (Gate)
        {
            var report = new Dictionary<string, object?>
            {
                ["Action"] = action,
                ["TimestampUtc"] = DateTime.UtcNow.ToString("O"),
                ["Success"] = false,
                ["ProcessId"] = Environment.ProcessId
            };
            object? host = null, loader = null;
            Exception? failure = null;
            try
            {
                host = Get(FindType("mod.st.NetChater"), "Instance")
                    ?? throw new InvalidOperationException("NetChater.Instance is null");
                loader = Get(FindType("ModLoader.LogicLoader"), "Instance")
                    ?? throw new InvalidOperationException("LogicLoader.Instance is null");
                report["Before"] = Capture(host, loader);
                string verb = (action ?? "").Trim().ToLowerInvariant();
                // Also accept 'unload draw_guess' / 'reload draw_guess' for direct callers.
                if (verb.EndsWith(" draw_guess", StringComparison.Ordinal)) verb = verb[..^11].Trim();
                switch (verb)
                {
                    case "snapshot":
                        report["Success"] = true;
                        break;
                    case "reset":
                        Invoke(host, "Disconnect", Type.EmptyTypes);
                        host.GetType().GetField("Host", Members)!.SetValue(host, Get(host.GetType(), "PublicHost"));
                        host.GetType().GetField("Port", Members)!.SetValue(host, Get(host.GetType(), "PublicPort"));
                        report["Success"] = true;
                        break;
                    case "unload":
                        report["Success"] = Invoke(loader, "UnloadModule", new[] { typeof(string) }, "draw_guess");
                        break;
                    case "reload":
                        string path = ResolveReloadPath();
                        report["ReloadPath"] = path;
                        report["Priority"] = _drawGuessPriority;
                        // LoadModuleFromFile itself unloads an existing module. Call explicitly so the report records it.
                        report["UnloadResult"] = Invoke(loader, "UnloadModule", new[] { typeof(string) }, "draw_guess");
                        report["Success"] = Invoke(loader, "LoadModuleFromFile",
                            new[] { typeof(string), typeof(string), typeof(int) }, "draw_guess", path, _drawGuessPriority);
                        break;
                    default:
                        throw new ArgumentException("Supported actions: snapshot, unload, reload (draw_guess only)", nameof(action));
                }
            }
            catch (Exception error)
            {
                report["Success"] = false;
                failure = Unwrap(error);
                report["Error"] = failure.ToString();
            }
            finally
            {
                if (host != null && loader != null)
                {
                    try
                    {
                        var snapshot = Capture(host, loader);
                        report["Snapshot"] = snapshot;
                        foreach (var pair in snapshot) report[pair.Key] = pair.Value;
                    }
                    catch (Exception error)
                    {
                        failure ??= Unwrap(error);
                        report["Success"] = false;
                        report["SnapshotError"] = failure.ToString();
                    }
                }
                report["RememberedDrawGuessPath"] = _drawGuessPath;
                report["Note"] = "Immediate main-thread snapshot; asynchronous room cleanup/channel synchronization may require a later snapshot.";
                string fullPath = Path.GetFullPath(outputPath);
                Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
                File.WriteAllText(fullPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
            }
            if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    private static Dictionary<string, object?> Capture(object host, object loader)
    {
        var modules = new List<Dictionary<string, object?>>();
        var all = Invoke(loader, "GetAllModules", Type.EmptyTypes) as IEnumerable
            ?? throw new InvalidOperationException("GetAllModules did not return IEnumerable");
        foreach (object module in all)
        {
            string? id = Get(module, "Id")?.ToString();
            string? path = Get(module, "Path")?.ToString();
            string? dllPath = Get(module, "DllPath")?.ToString();
            string? sourcePath = Get(module, "SourcePath")?.ToString();
            var priority = Get(module, "Priority");
            modules.Add(new()
            {
                ["Id"] = id, ["Path"] = path, ["DllPath"] = dllPath, ["SourcePath"] = sourcePath,
                ["Priority"] = priority, ["Assembly"] = (Get(module, "Assembly") as Assembly)?.FullName,
                ["LogicType"] = Get(module, "Logic")?.GetType().FullName
            });
            if (string.Equals(id, "draw_guess", StringComparison.OrdinalIgnoreCase))
            {
                if (priority is int number) _drawGuessPriority = number;
                foreach (string? candidate in new[] { path, dllPath, sourcePath })
                {
                    if (string.IsNullOrWhiteSpace(candidate) || !File.Exists(candidate)) continue;
                    _drawGuessPath = Path.GetFullPath(candidate);
                    break;
                }
            }
        }
        object? client = Get(host, "Client");
        return new()
        {
            ["HostAssembly"] = host.GetType().Assembly.FullName,
            ["HostHash"] = RuntimeHelpers.GetHashCode(host),
            ["ClientId"] = client == null ? 0 : RuntimeHelpers.GetHashCode(client),
            ["ClientHash"] = client == null ? null : RuntimeHelpers.GetHashCode(client),
            ["ClientType"] = client?.GetType().AssemblyQualifiedName,
            ["SessionId"] = client == null ? "" : Get(client, "SessionId")?.ToString() ?? "",
            ["IsConnected"] = client == null ? false : Get(client, "IsConnected"),
            ["ChannelReady"] = Get(host, "ChannelReady"),
            ["DesiredChannel"] = Get(host, "DesiredChannel"),
            ["ChannelRevision"] = Get(host, "ChannelRevision"),
            ["Modules"] = modules
        };
    }

    private static string ResolveReloadPath()
    {
        if (_drawGuessPath != null && File.Exists(_drawGuessPath)) return _drawGuessPath;
        foreach (string root in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory }.Distinct())
        {
            string fallback = Path.GetFullPath(Path.Combine(root, "Mods", "cache", "draw_guess.dll"));
            if (File.Exists(fallback)) return fallback;
        }
        throw new FileNotFoundException("No saved draw_guess module path or game Mods/cache/draw_guess.dll exists; no unload performed.", _drawGuessPath);
    }

    private static Type FindType(string name)
    {
        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies().OrderByDescending(a => a.GetName().Name == "mod"))
        {
            Type? type = assembly.GetType(name, throwOnError: false);
            if (type != null) return type;
        }
        throw new TypeLoadException("Type not found in already loaded assemblies: " + name);
    }

    private static object? Get(object target, string name)
    {
        Type type = target as Type ?? target.GetType();
        object? instance = target is Type ? null : target;
        PropertyInfo? property = type.GetProperty(name, Members);
        if (property != null) return property.GetValue(instance);
        return type.GetField(name, Members)?.GetValue(instance);
    }

    private static object? Invoke(object target, string name, Type[] signature, params object?[] args)
    {
        MethodInfo method = target.GetType().GetMethod(name, Members, null, signature, null)
            ?? throw new MissingMethodException(target.GetType().FullName, name);
        return method.Invoke(target, args);
    }

    private static Exception Unwrap(Exception error)
    {
        while (error is TargetInvocationException && error.InnerException != null) error = error.InnerException;
        return error;
    }
}
