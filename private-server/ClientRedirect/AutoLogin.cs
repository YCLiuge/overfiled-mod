using System.Reflection;
using HarmonyLib;
using MelonLoader;

namespace OverField.PrivateServer;

// All game object access stays on Melon's main-thread update. HTTP runs on the pool.
internal static class AutoLogin
{
    private const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    private static PropertyInfo _handler = null!, _main = null!, _owner = null!, _init = null!, _uid = null!, _token = null!, _name = null!, _region = null!;
    private static MethodInfo _done = null!, _start = null!;
    private static LocalLoginClient? _client;
    private static Task<LocalLoginResult>? _pending;
    private static object? _ui;
    private static bool _applied, _entered, _entering;

    internal static void Install(HarmonyLib.Harmony harmony, string endpoint, string accountFile)
    {
        var sdk = Resolve("Il2Cpp.MainSDK");
        var handler = Resolve("Il2Cpp.SDKHandler");
        var ui = Resolve("Il2CppInutan.UILoginSystem");
        var iterator = Resolve("Il2Cpp.QuickSDK_cn_win+_Init_d__2");
        _handler = Property(handler, "Instance"); _main = Property(handler, "_mainSDK");
        _owner = Property(iterator, "__4__this"); _init = Property(sdk, "IsInit");
        _uid = Property(sdk, "SDKUid"); _token = Property(sdk, "SDKToken"); _name = Property(sdk, "SDKUserName");
        _region = Property(ui, "RegionInfo");
        _done = Method(ui, "OnSDKLoginDone"); _start = Method(ui, "OnStartGame", typeof(string));
        foreach (var p in new[] { _init, _uid, _token, _name })
            if (!p.CanWrite) throw new InvalidOperationException("Auto-login game contract changed");
        _client = new LocalLoginClient(endpoint, accountFile);
        Patch(harmony, Method(iterator, "MoveNext"), nameof(Initialize));
        Patch(harmony, Method(handler, "LoginSDK"), nameof(Login));
        Patch(harmony, _start, nameof(Start));
        Patch(harmony, Method(ui, "OnReady"), nameof(Ready), true);
        Patch(harmony, Method(ui, "Close"), nameof(Close));
        // These native callbacks would otherwise contact the original SDK after login.
        var quick = Resolve("Il2Cpp.QuickSDK_cn_win");
        Patch(harmony, Method(quick, "UploadRoleInfo", Resolve("Il2Cpp.GameRoleInfo")), nameof(Suppress));
        Patch(harmony, Method(quick, "OpenUserCenter"), nameof(Suppress));
        Patch(harmony, Method(quick, "LogoutSDK"), nameof(Logout));
        MelonLogger.Msg("[PrivateServer] Auto-login installed: local authentication, no original SDK login UI.");
    }
    private static void Patch(HarmonyLib.Harmony h, MethodInfo m, string name, bool postfix = false)
    {
        var patch = new HarmonyMethod(typeof(AutoLogin), name);
        h.Patch(m, prefix: postfix ? null : patch, postfix: postfix ? patch : null);
    }
    private static bool Initialize(object __instance, ref bool __result)
    {
        _init.SetValue(_owner.GetValue(__instance), true);
        __result = false;
        return false;
    }
    private static bool Suppress() => false;
    private static bool Logout(object __instance)
    {
        _uid.SetValue(__instance, ""); _token.SetValue(__instance, ""); _name.SetValue(__instance, "");
        Method(Resolve("Il2Cpp.MainSDK"), "OnLogoutDone").Invoke(__instance, null);
        return false;
    }
    private static void Ready(object __instance)
    {
        _ui = __instance; _applied = false; _entered = false;
        Begin();
    }
    private static void Close() { _ui = null; }
    private static bool Login() { Begin(); return false; }
    private static bool Start(object __instance)
    {
        if (_entering) return true;
        _ui = __instance;
        if (!_applied) Begin();
        else _entered = false; // explicit retry of the game's gateway connection
        return false;
    }
    private static void Begin()
    {
        if (_client == null || _pending != null || _applied) return;
        _pending = Task.Run(() => _client.LoginAsync());
        MelonLogger.Msg("[PrivateServer] Authenticating local account...");
    }
    internal static void Tick()
    {
        if (_client == null || _ui == null) return;
        try
        {
            if (_pending?.IsCompleted == true)
            {
                var task = _pending; _pending = null;
                var result = task.GetAwaiter().GetResult();
                var sdk = _main.GetValue(_handler.GetValue(null)) ?? throw new InvalidOperationException();
                _uid.SetValue(sdk, result.Uid); _token.SetValue(sdk, result.GatewayToken); _name.SetValue(sdk, result.UserName);
                _applied = true;
                _done.Invoke(_ui, null);
                MelonLogger.Msg("[PrivateServer] Local account authenticated; waiting for region information.");
            }
            if (_applied && !_entered && _region.GetValue(_ui) != null)
            {
                _entered = true; _entering = true;
                try
                {
                    var sdk = _main.GetValue(_handler.GetValue(null));
                    _start.Invoke(_ui, new object[] { (string?)_uid.GetValue(sdk) ?? throw new InvalidOperationException() });
                }
                finally { _entering = false; }
            }
        }
        catch (Exception ex)
        {
            // SDK/network exceptions may contain request bodies; only log their type.
            MelonLogger.Error("[PrivateServer] Auto-login stopped (" + (ex.InnerException ?? ex).GetType().Name + "). Check server/account file, then click Start to retry.");
        }
    }
    internal static void Stop() { _client = null; _ui = null; }
    private static Type Resolve(string name) => AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(name, false)).FirstOrDefault(t => t != null) ?? throw new TypeLoadException(name);
    private static PropertyInfo Property(Type t, string n) => t.GetProperty(n, Flags) ?? throw new MissingMemberException(t.FullName, n);
    private static MethodInfo Method(Type t, string n, params Type[] args) => t.GetMethod(n, Flags, null, args, null) ?? throw new MissingMethodException(t.FullName, n);
}
