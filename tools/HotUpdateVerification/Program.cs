using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using ModLoader;
using Newtonsoft.Json;

public static class Probe
{
    public static int NextId;
    public static bool FailNextLoad, FailNextConstructor, FailNextUnload;
    public static int FailUnloadId = -1;
    public static readonly List<string> Events = new();
    public static readonly HashSet<int> Active = new();
    public static readonly Dictionary<int, int> LoadThreads = new();
    public static Action<int> Updating;
    public static int Construct()
    {
        if (FailNextConstructor) { FailNextConstructor = false; throw new Exception("fixture constructor"); }
        return ++NextId;
    }
    public static void Load(int id)
    {
        Events.Add($"load:{id}"); Active.Add(id);
        LoadThreads[id] = Environment.CurrentManagedThreadId;
        if (FailNextLoad) { FailNextLoad = false; throw new Exception("fixture partial OnLoad"); }
    }
    public static void Unload(int id)
    {
        Events.Add($"unload:{id}");
        if (FailNextUnload || id == FailUnloadId) { FailNextUnload = false; throw new Exception("fixture OnUnload"); }
        Active.Remove(id);
    }
    public static void Update(int id) { Events.Add($"update:{id}"); Updating?.Invoke(id); }
    public static void Reset()
    {
        FailNextLoad = FailNextConstructor = FailNextUnload = false;
        FailUnloadId = -1;
        Updating = null; Events.Clear(); Active.Clear(); LoadThreads.Clear();
    }
}

internal static class Program
{
    static byte[] Fixture;
    static readonly LogicLoader Loader = LogicLoader.Instance;
    static int Passed;
    static readonly string Root = Path.Combine(AppContext.BaseDirectory, "verification-runs", Guid.NewGuid().ToString("N"));
    static void Main(string[] args)
    {
        Fixture = File.ReadAllBytes(args.Length == 0
            ? Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../Fixtures/bin/Release/net6.0/Fixtures.dll")) : args[0]);
        Run("loader bad DLL and constructor failure preserve old instance", PreserveOld);
        Run("loader partial OnLoad cleanup and old instance recovery", Rollback);
        Run("loader cleanup failure prevents old reactivation", CleanupFailure);
        Run("loader old OnUnload failure prevents activation and recovery", OldCleanupFailure);
        Run("loader Tick unload does not skip surviving modules", TickUnload);
        Run("TCP negative length rejected", () => BadResponse(-1, Array.Empty<byte>(), typeof(InvalidDataException)));
        Run("TCP oversized length rejected", () => BadResponse(1025, Array.Empty<byte>(), typeof(InvalidDataException)));
        Run("TCP truncated body rejected", () => BadResponse(4, new byte[] { 1 }, typeof(IOException)));
        Run("TCP transfer timeout bounded", Timeout);
        Run("cache disabled entries, bad hashes and missing DLL skipped", CacheValidation);
        Run("failed load preserves cache, manifest and version", FailedCommit);
        Run("single/bulk mutual exclusion in both directions", MutualExclusion);
        Run("Tick commits successful update on caller thread", TickCommit);
        Run("cache disabled repeat check does not redownload", RepeatWithoutCache);
        Console.WriteLine($"PASS {Passed}/{Passed} scenarios; artifacts: {Root}");
    }
    static void Run(string name, Action action)
    {
        Loader.UnloadAll(); Probe.Reset();
        try { action(); Passed++; Console.WriteLine("PASS " + name); }
        catch (Exception e) { Console.Error.WriteLine("FAIL " + name + "\n" + e); Environment.ExitCode = 1; throw; }
        finally { Probe.Updating = null; Loader.UnloadAll(); }
    }
    static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
    static int Id(ILogic logic) => (int)logic.GetType().GetField("_id", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(logic);
    static void PreserveOld()
    {
        Assert(Loader.LoadModuleFromBytes("old", Fixture), "initial load");
        var old = Loader.GetModule("old"); int id = Id(old); Probe.Events.Clear();
        Assert(!Loader.LoadModuleFromBytes("old", new byte[] { 1, 2, 3 }), "bad dll accepted");
        Assert(ReferenceEquals(old, Loader.GetModule("old")) && Probe.Active.Contains(id), "bad DLL removed old");
        Probe.FailNextConstructor = true;
        Assert(!Loader.LoadModuleFromBytes("old", Fixture), "bad constructor accepted");
        Assert(ReferenceEquals(old, Loader.GetModule("old")) && Probe.Events.Count == 0, "constructor failure touched old lifecycle");
    }
    static void Rollback()
    {
        Assert(Loader.LoadModuleFromBytes("old", Fixture), "initial load");
        var old = Loader.GetModule("old"); int id = Id(old); Probe.Events.Clear();
        Probe.FailNextLoad = true;
        Assert(!Loader.LoadModuleFromBytes("old", Fixture), "partial load accepted");
        int candidate = Probe.NextId;
        Assert(ReferenceEquals(old, Loader.GetModule("old")), "old instance not restored");
        Assert(Probe.Events.SequenceEqual(new[] { $"unload:{id}", $"load:{candidate}", $"unload:{candidate}", $"load:{id}" }), "wrong rollback lifecycle: " + string.Join(",", Probe.Events));
        Assert(Probe.Active.SetEquals(new[] { id }), "candidate side effects remain");
    }
    static void CleanupFailure()
    {
        Assert(Loader.LoadModuleFromBytes("new", Fixture), "initial load");
        int oldId = Id(Loader.GetModule("new"));
        Probe.Events.Clear(); Probe.FailNextLoad = true; Probe.FailUnloadId = Probe.NextId + 1;
        Assert(!Loader.LoadModuleFromBytes("new", Fixture), "failed partial load accepted");
        Assert(!Loader.HasModule("new"), "failed instance registered");
        Assert(!Probe.Events.Contains($"load:{oldId}"), "old reactivated while candidate cleanup failed");
        Assert(!Probe.Active.Contains(oldId), "old side effects remain active");
    }
    static void OldCleanupFailure()
    {
        Assert(Loader.LoadModuleFromBytes("old-cleanup", Fixture), "initial load");
        Probe.Events.Clear(); Probe.FailNextUnload = true;
        Assert(!Loader.LoadModuleFromBytes("old-cleanup", Fixture), "replacement accepted despite old cleanup failure");
        Assert(!Probe.Events.Any(x => x.StartsWith("load:")), "old or new OnLoad ran after old cleanup failure");
        Assert(!Loader.HasModule("old-cleanup"), "uncertain old module still registered");
    }
    static void TickUnload()
    {
        foreach (var id in new[] { "a", "b", "c" }) Assert(Loader.LoadModuleFromBytes(id, Fixture), "fixture load");
        int a = Id(Loader.GetModule("a")), b = Id(Loader.GetModule("b")), c = Id(Loader.GetModule("c"));
        Probe.Events.Clear(); Probe.Updating = id => { if (id == a) Loader.UnloadModule("a"); };
        Loader.Tick();
        Assert(Probe.Events.Where(x => x.StartsWith("update:")).SequenceEqual(new[] { $"update:{a}", $"update:{b}", $"update:{c}" }), "Tick skipped a survivor");
    }
    static LogicUpdater Updater(string label)
    {
        var updater = new LogicUpdater { LogVerbose = false, ConnectTimeoutMs = 1000, TransferTimeoutMs = 1000 };
        Set(updater, "_cacheDir", Path.Combine(Root, label));
        return updater;
    }
    static object Get(object obj, string name) => obj.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(obj);
    static void Set(object obj, string name, object value) => obj.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(obj, value);
    static Task<byte[]> Fetch(LogicUpdater updater, int port) => (Task<byte[]>)typeof(LogicUpdater)
        .GetMethod("FetchTcpFromAsync", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(updater, new object[] { "127.0.0.1", port, "fixture.dll" });
    static void BadResponse(int length, byte[] body, Type expected)
    {
        using var server = new Server(async (_, stream, _) => { await stream.WriteAsync(Header(length)); await stream.WriteAsync(body); });
        var updater = Updater("bad"); updater.MaxDownloadBytes = 1024;
        try { Fetch(updater, server.Port).GetAwaiter().GetResult(); throw new Exception("invalid response accepted"); }
        catch (Exception ex) when (expected.IsInstanceOfType(ex)) { }
    }
    static void Timeout()
    {
        using var server = new Server(async (_, _, token) => await Task.Delay(2000, token));
        var updater = Updater("timeout"); updater.TransferTimeoutMs = 100;
        var timer = Stopwatch.StartNew();
        try { Fetch(updater, server.Port).GetAwaiter().GetResult(); throw new Exception("timeout not enforced"); }
        catch (OperationCanceledException) { }
        Assert(timer.ElapsedMilliseconds < 1500, "transfer timeout too slow");
    }
    static RemoteModuleInfo Remote(string id, byte[] bytes, bool enabled = true) => new()
        { Id = id, Version = "2.0.0", Enabled = enabled, Hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), Priority = 7, Url = id + ".dll" };
    static byte[] Manifest(params RemoteModuleInfo[] modules) => Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new ModuleManifest { Modules = modules.ToList() }));
    static void CacheValidation()
    {
        var updater = Updater("cache-validation"); Directory.CreateDirectory(updater.CacheDir);
        var disabled = Remote("disabled", Fixture, false); var bad = Remote("bad", Fixture); bad.Hash = new string('0', 64);
        var missing = Remote("missing", Fixture); var good = Remote("good", Fixture);
        foreach (var id in new[] { "disabled", "bad", "good" }) File.WriteAllBytes(updater.GetModuleCachePath(id), Fixture);
        File.WriteAllBytes(updater.ManifestCachePath, Manifest(disabled, bad, missing, good));
        updater.LoadLocalCache();
        Assert(Loader.ModuleCount == 1 && Loader.HasModule("good"), "cache filters failed");
        var versions = (Dictionary<string, string>)Get(updater, "_localVersions");
        Assert(versions.Count == 1 && versions["good"] == "2.0.0", "invalid cache versions committed");
        Loader.UnloadAll(); updater.EnableCache = false; updater.LoadLocalCache();
        Assert(Loader.ModuleCount == 0, "EnableCache=false ignored");
    }
    static void FailedCommit()
    {
        var updater = Updater("failed-commit"); var remote = Remote("rollback", Fixture);
        Directory.CreateDirectory(updater.CacheDir);
        byte[] oldCache = { 42, 17 }; byte[] oldManifest = Encoding.UTF8.GetBytes("{\"modules\":[]}");
        File.WriteAllBytes(updater.GetModuleCachePath(remote.Id), oldCache); File.WriteAllBytes(updater.ManifestCachePath, oldManifest);
        updater.SetLocalVersion(remote.Id, "1.0.0");
        Assert(Loader.LoadModuleFromBytes(remote.Id, Fixture), "initial load"); var old = Loader.GetModule(remote.Id);
        using var server = Files(remote, Fixture); updater.ServerEndpoints = new[] { server.Endpoint };
        Probe.FailNextLoad = true;
        var pending = updater.CheckSingleModuleAsync(remote.Id); Pump(updater, pending);
        Assert(!pending.GetAwaiter().GetResult(), "failed load reported success");
        Assert(ReferenceEquals(old, Loader.GetModule(remote.Id)), "old module lost");
        Assert(File.ReadAllBytes(updater.GetModuleCachePath(remote.Id)).SequenceEqual(oldCache), "DLL cache changed");
        Assert(File.ReadAllBytes(updater.ManifestCachePath).SequenceEqual(oldManifest), "manifest changed");
        Assert(((Dictionary<string, string>)Get(updater, "_localVersions"))[remote.Id] == "1.0.0", "version changed");
    }
    static void MutualExclusion()
    {
        foreach (bool bulkFirst in new[] { true, false })
        {
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var server = new Server(async (_, stream, token) => { await release.Task.WaitAsync(token); await Reply(stream, Manifest()); });
            var updater = Updater("gate"); updater.ServerEndpoints = new[] { server.Endpoint };
            Task first = bulkFirst ? updater.CheckAndUpdateAllAsync() : updater.CheckSingleModuleAsync("none");
            Assert(server.Accepted.Wait(2000), "server never received initial request");
            Assert(updater.IsChecking, "busy not visible");
            if (bulkFirst) Assert(!updater.CheckSingleModuleAsync("none").GetAwaiter().GetResult(), "single bypassed bulk gate");
            else updater.CheckAndUpdateAllAsync().GetAwaiter().GetResult();
            Assert(server.RequestCount == 1 && !first.IsCompleted, "concurrent request entered server");
            release.TrySetResult(true); first.WaitAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult();
            Assert(!updater.IsChecking, "gate not released");
        }
    }
    static void TickCommit()
    {
        var updater = Updater("tick-commit"); var remote = Remote("mainthread", Fixture);
        var releaseDll = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var server = new Server(async (name, stream, token) =>
        {
            if (name != "version.json") await releaseDll.Task.WaitAsync(token);
            await Reply(stream, name == "version.json" ? Manifest(remote) : Fixture);
        });
        updater.ServerEndpoints = new[] { server.Endpoint };
        int mainThread = Environment.CurrentManagedThreadId;
        var pending = updater.CheckSingleModuleAsync(remote.Id);
        Wait(() => ((ConcurrentQueue<Action>)Get(updater, "_mainThreadActions")).Count > 0, "main thread action not queued");
        updater.Tick(); // First queued action only reads state; network download follows.
        Wait(() => server.RequestCount == 2, "DLL request not received");
        releaseDll.TrySetResult(true);
        Wait(() => ((ConcurrentQueue<Action>)Get(updater, "_mainThreadActions")).Count > 0, "commit action not queued");
        Assert(!pending.IsCompleted && !Loader.HasModule(remote.Id) && !File.Exists(updater.ManifestCachePath), "commit happened before Tick");
        Assert(!updater.CheckSingleModuleAsync(remote.Id).GetAwaiter().GetResult(), "gate released before Tick");
        updater.Tick(); pending.WaitAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult();
        Assert(pending.Result && Loader.HasModule(remote.Id), "Tick did not activate module");
        Assert(Probe.LoadThreads[Id(Loader.GetModule(remote.Id))] == mainThread, "OnLoad ran on worker thread");
        Assert(File.ReadAllBytes(updater.GetModuleCachePath(remote.Id)).SequenceEqual(Fixture), "cache not committed");
        Assert(((Dictionary<string, string>)Get(updater, "_localVersions"))[remote.Id] == remote.Version, "version not committed");
        Assert(JsonConvert.DeserializeObject<ModuleManifest>(File.ReadAllText(updater.ManifestCachePath)).Modules.Single().Version == remote.Version, "manifest not committed");
    }
    static void RepeatWithoutCache()
    {
        var updater = Updater("without-cache"); updater.EnableCache = false;
        var remote = Remote("nocache", Fixture);
        using var server = Files(remote, Fixture); updater.ServerEndpoints = new[] { server.Endpoint };
        var first = updater.CheckSingleModuleAsync(remote.Id); Pump(updater, first);
        Assert(first.Result && server.RequestCount == 2, "initial download failed");
        var instance = Loader.GetModule(remote.Id); Probe.Events.Clear();
        var second = updater.CheckSingleModuleAsync(remote.Id); Pump(updater, second);
        Assert(!second.Result && server.RequestCount == 3, "unchanged DLL downloaded again without cache");
        Assert(ReferenceEquals(instance, Loader.GetModule(remote.Id)) && Probe.Events.Count == 0, "unchanged module reloaded");
        Assert(!Directory.Exists(updater.CacheDir), "cache disabled but files written");
    }
    static void Pump(LogicUpdater updater, Task task) => Wait(() => { updater.Tick(); return task.IsCompleted; }, "update did not finish");
    static void Wait(Func<bool> condition, string failure)
    {
        var timer = Stopwatch.StartNew();
        while (!condition()) { if (timer.ElapsedMilliseconds > 5000) throw new Exception(failure); Thread.Sleep(2); }
    }
    static byte[] Header(int length) => new[] { (byte)0, (byte)(length >> 24), (byte)(length >> 16), (byte)(length >> 8), (byte)length };
    static async Task Reply(NetworkStream stream, byte[] body) { await stream.WriteAsync(Header(body.Length)); await stream.WriteAsync(body); }
    static Server Files(RemoteModuleInfo remote, byte[] dll) => new(async (name, stream, _) => await Reply(stream, name == "version.json" ? Manifest(remote) : dll));

    sealed class Server : IDisposable
    {
        readonly TcpListener listener = new(IPAddress.Loopback, 0);
        readonly CancellationTokenSource stop = new();
        readonly Task worker;
        public readonly ManualResetEventSlim Accepted = new(false);
        public int RequestCount;
        public int Port => ((IPEndPoint)listener.LocalEndpoint).Port;
        public string Endpoint => "127.0.0.1:" + Port;
        public Server(Func<string, NetworkStream, CancellationToken, Task> respond)
        {
            listener.Start();
            worker = Task.Run(async () =>
            {
                try
                {
                    while (!stop.IsCancellationRequested)
                    {
                        using var client = await listener.AcceptTcpClientAsync(stop.Token);
                        using var stream = client.GetStream();
                        byte[] header = new byte[4]; await Exact(stream, header, stop.Token);
                        int size = (header[0] << 24) | (header[1] << 16) | (header[2] << 8) | header[3];
                        byte[] name = new byte[size]; await Exact(stream, name, stop.Token);
                        Interlocked.Increment(ref RequestCount); Accepted.Set();
                        await respond(Encoding.UTF8.GetString(name), stream, stop.Token);
                    }
                }
                catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
                catch (SocketException) when (stop.IsCancellationRequested) { }
            });
        }
        static async Task Exact(NetworkStream stream, byte[] bytes, CancellationToken token)
        {
            int offset = 0;
            while (offset < bytes.Length)
            {
                int n = await stream.ReadAsync(bytes.AsMemory(offset), token);
                if (n == 0) throw new IOException("truncated request"); offset += n;
            }
        }
        public void Dispose()
        {
            stop.Cancel(); listener.Stop(); worker.WaitAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult();
            stop.Dispose(); Accepted.Dispose();
        }
    }
}

namespace MelonLoader
{
    public static class MelonLogger
    {
        public static void Msg(string message) { }
        public static void Warning(string message) { }
        public static void Error(string message) { }
    }
}
namespace UnityEngine { public static class Time { public static float deltaTime => 1f / 60f; } }
