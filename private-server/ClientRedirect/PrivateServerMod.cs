using System.Reflection;
using System.Text.Json;
using HarmonyLib;
using MelonLoader;

[assembly: MelonInfo(typeof(OverField.PrivateServer.PrivateServerMod), "OverField Local Server Redirect", "0.3.1", "Local")]
[assembly: MelonPlatformDomain(MelonPlatformDomainAttribute.CompatibleDomains.IL2CPP)]

namespace OverField.PrivateServer;

public sealed class PrivateServerMod : MelonMod
{
    private static Uri? _target;
    private static string? _gatewayHost;
    private static ushort _gatewayPort;
    private static ushort _logPort;
    private TunnelClient? _tunnel;
    private sealed class Settings
    {
        public bool Enabled { get; set; }
        public string BaseUrl { get; set; } = "http://127.0.0.1:18080";
        public string GatewayHost { get; set; } = "127.0.0.1";
        public ushort GatewayPort { get; set; } = 11000;
        public ushort LogPort { get; set; } = 12000;
        public bool TunnelEnabled { get; set; }
        public string TunnelRemote { get; set; } = "relay.example.com:50505";
        public string TunnelKey { get; set; } = "";
        public bool SkipHotUpdate { get; set; }
    }

    public override void OnInitializeMelon()
    {
        var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "UserData", "private-server.json");
        try
        {
            if (!File.Exists(path))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, JsonSerializer.Serialize(new Settings(), new JsonSerializerOptions { WriteIndented = true }));
                MelonLogger.Msg("[PrivateServer] Created disabled config: " + path);
                return;
            }
            var settings = JsonSerializer.Deserialize<Settings>(File.ReadAllText(path))
                ?? throw new InvalidDataException("Config is null");
            if (!settings.Enabled) { MelonLogger.Msg("[PrivateServer] Disabled in config."); return; }
            if (settings.TunnelEnabled)
            {
                _tunnel = new TunnelClient(settings.TunnelRemote, settings.TunnelKey, text => MelonLogger.Msg("[PrivateServer] " + text));
                _tunnel.Start();
                settings.BaseUrl = "http://127.0.0.1:18081";
                settings.GatewayHost = "127.0.0.1";
                settings.GatewayPort = 11001;
                settings.LogPort = 12001;
            }
            _target = RedirectPolicy.ValidateTarget(settings.BaseUrl);
            if (Uri.CheckHostName(settings.GatewayHost) == UriHostNameType.Unknown || settings.GatewayPort == 0 || settings.LogPort == 0)
                throw new ArgumentException("Invalid local gateway host or port");
            _gatewayHost = settings.GatewayHost;
            _gatewayPort = settings.GatewayPort;
            _logPort = settings.LogPort;
            var prefix = new HarmonyMethod(typeof(PrivateServerMod).GetMethod(nameof(RewriteUrl), BindingFlags.Static | BindingFlags.NonPublic)!);
            int count = 0;
            // Patch the game HTTP entrypoints, and Unity's native SetUrl for other callers.
            foreach (var item in new[] {
                ("Il2Cpp.HttpUtils", "Post"), ("Il2Cpp.HttpUtils", "Get"),
                ("Il2Cpp.HttpUtils", "SendPostRequest"), ("Il2Cpp.HttpUtils", "SendGetRequest"),
                ("Il2Cpp.WebRequestHelper", "WebRequest_get"), ("Il2Cpp.WebRequestHelper", "WebRequest_post"),
                ("UnityEngine.Networking.UnityWebRequest", "set_url"),
                ("UnityEngine.Networking.UnityWebRequest", "SetUrl") })
            {
                var type = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(item.Item1, false)).FirstOrDefault(t => t != null);
                var methods = type?.GetMethods(BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .Where(m => m.Name == item.Item2 && m.GetParameters().Length > 0 && m.GetParameters()[0].ParameterType == typeof(string))
                    .ToArray() ?? Array.Empty<MethodInfo>();
                foreach (var method in methods)
                {
                    try { HarmonyInstance.Patch(method, prefix: prefix); count++; }
                    catch (Exception e) { MelonLogger.Warning($"[PrivateServer] Patch failed: {item.Item1}.{item.Item2}: {e.GetType().Name}"); }
                }
            }
            if (count == 0) throw new InvalidOperationException("No compatible HTTP methods found; redirection is NOT active.");
            var networkType = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("Il2CppInutan.NetworkSystem", false)).FirstOrDefault(t => t != null)
                ?? throw new InvalidOperationException("NetworkSystem missing; gateway routing is NOT active.");
            int tcpHooks = 0;
            foreach (var name in new[] { "SetConnectInfo", "Connect", "SetLogConnectInfo", "ConnectLog" })
            {
                var method = networkType.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                    null, new[] { typeof(string), typeof(ushort) }, null)
                    ?? throw new MissingMethodException(networkType.FullName, name);
                var handler = name.Contains("Log") ? nameof(RewriteLogGateway) : nameof(RewriteGateway);
                HarmonyInstance.Patch(method, prefix: new HarmonyMethod(typeof(PrivateServerMod).GetMethod(handler, BindingFlags.Static | BindingFlags.NonPublic)!));
                tcpHooks++;
            }
            if (settings.SkipHotUpdate) SkipHotUpdate.Install(HarmonyInstance);
            MelonLogger.Msg($"[PrivateServer] {tcpHooks} game/log gateway hooks installed: {_gatewayHost}:{_gatewayPort}/{_logPort}");
            MelonLogger.Msg($"[PrivateServer] {count} HTTP hooks installed. Target: {_target.GetLeftPart(UriPartial.Authority)}. Restart game after config changes.");
        }
        catch (Exception e)
        {
            HarmonyInstance.UnpatchSelf();
            _tunnel?.Dispose();
            _tunnel = null;
            _target = null;
            _gatewayHost = null;
            MelonLogger.Error("[PrivateServer] Redirect disabled: " + e.Message);
        }
    }

    public override void OnDeinitializeMelon()
    {
        _tunnel?.Dispose();
        _tunnel = null;
    }

    private static void RewriteGateway(ref string __0, ref ushort __1)
    {
        if (_gatewayHost == null) return;
        __0 = _gatewayHost;
        __1 = _gatewayPort;
        MelonLogger.Msg($"[PrivateServer] Game gateway -> {__0}:{__1}");
    }

    private static void RewriteLogGateway(ref string __0, ref ushort __1)
    {
        if (_gatewayHost == null) return;
        __0 = _gatewayHost;
        __1 = _logPort;
        MelonLogger.Msg($"[PrivateServer] Log gateway -> {__0}:{__1}");
    }

    private static void RewriteUrl(ref string __0)
    {
        if (_target == null || !RedirectPolicy.TryRewrite(__0, _target, out var rewritten)) return;
        // Domain only: never print query strings, tokens, passwords or POST bodies.
        var host = new Uri(__0).Host;
        __0 = rewritten;
        MelonLogger.Msg($"[PrivateServer] Redirect {host} -> {_target.GetLeftPart(UriPartial.Authority)}");
    }
}
