using System.Reflection;
using Mono.Cecil;
using OverField.PrivateServer;

internal static class Program
{
    static int passed, failed;
    internal static readonly int MainThread = Environment.CurrentManagedThreadId;
    static readonly BindingFlags StaticPrivate = BindingFlags.Static | BindingFlags.NonPublic;
    static void Check(bool ok, string label)
    {
        Console.WriteLine((ok ? "PASS " : "FAIL ") + label);
        if (ok) passed++; else failed++;
    }
    static int Main(string[] args)
    {
        try { Metadata(args.Length > 0 ? args[0] : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../libs/Assembly-CSharp.dll"))); }
        catch (Exception e) { Check(false, "metadata inspection: " + e); }
        try { Behavior(); }
        catch (Exception e) { Check(false, "behavior test exception: " + e); }
        Console.WriteLine($"RESULT {passed} passed, {failed} failed");
        return failed == 0 ? 0 : 1;
    }
    static void Metadata(string path)
    {
        using var assembly = AssemblyDefinition.ReadAssembly(path);
        Console.WriteLine("METADATA " + Path.GetFullPath(path));
        TypeDefinition Type(string name) => assembly.MainModule.GetType(name) ?? throw new Exception("Missing type " + name);
        PropertyDefinition Prop(TypeDefinition type, string name, bool write = false, string? valueType = null)
        {
            var p = type.Properties.FirstOrDefault(p => p.Name == name);
            Check(p?.GetMethod != null && (!write || p.SetMethod != null) && (valueType == null || p.PropertyType.FullName == valueType), $"metadata {type.FullName}.{name} readable{(write ? "/writable" : "")} type={p?.PropertyType.FullName}");
            return p!;
        }
        void Method(TypeDefinition type, string name, string returns, params string[] parameters)
        {
            var found = type.Methods.Where(m => m.Name == name && m.Parameters.Select(p => p.ParameterType.FullName).SequenceEqual(parameters)).ToArray();
            Check(found.Length == 1 && found[0].ReturnType.FullName == returns, $"metadata {type.FullName}.{name}({string.Join(",", parameters)}) -> {returns}");
        }
        var sdk = Type("Il2Cpp.MainSDK");
        Prop(sdk, "IsInit", true, "System.Boolean");
        foreach (var name in new[] { "SDKUid", "SDKToken", "SDKUserName" }) Prop(sdk, name, true, "System.String");
        Method(sdk, "OnLogoutDone", "System.Void");
        var handler = Type("Il2Cpp.SDKHandler");
        var instance = Prop(handler, "Instance");
        Check(instance?.GetMethod?.IsStatic == true, "metadata SDKHandler.Instance static getter");
        Prop(handler, "_mainSDK", valueType: "Il2Cpp.MainSDK");
        Method(handler, "LoginSDK", "System.Void");
        var ui = Type("Il2CppInutan.UILoginSystem");
        Prop(ui, "RegionInfo");
        foreach (var name in new[] { "OnReady", "Close", "OnSDKLoginDone" }) Method(ui, name, "System.Void");
        Method(ui, "OnStartGame", "System.Void", "System.String");
        var quick = Type("Il2Cpp.QuickSDK_cn_win");
        var iterator = quick.NestedTypes.SingleOrDefault(t => t.Name == "_Init_d__2");
        Check(iterator != null, "metadata QuickSDK_cn_win nested _Init_d__2");
        if (iterator != null)
        {
            Prop(iterator, "__4__this", valueType: quick.FullName);
            Method(iterator, "MoveNext", "System.Boolean");
        }
        Type("Il2Cpp.GameRoleInfo");
        Method(quick, "UploadRoleInfo", "System.Void", "Il2Cpp.GameRoleInfo");
        foreach (var name in new[] { "OpenUserCenter", "LogoutSDK" }) Method(quick, name, "System.Void");
        // Reflection on QuickSDK also needs to see MainSDK's setters and callback.
        Check(quick.BaseType.FullName == sdk.FullName, "metadata QuickSDK_cn_win inherits MainSDK");
    }
    static void Reset()
    {
        AutoLogin.Stop();
        foreach (var name in new[] { "_pending", "_ui" }) typeof(AutoLogin).GetField(name, StaticPrivate)!.SetValue(null, null);
        foreach (var name in new[] { "_applied", "_entered", "_entering" }) typeof(AutoLogin).GetField(name, StaticPrivate)!.SetValue(null, false);
        LocalLoginClient.Reset();
        MelonLoader.MelonLogger.Messages.Clear();
        Il2Cpp.SDKHandler.Current = new();
        GameThread.Accesses.Clear();
        AutoLogin.Install(new HarmonyLib.Harmony(), "http://localhost", "unused-test-account.json");
    }
    static void WaitPending()
    {
        if (!SpinWait.SpinUntil(() => ((Task?)typeof(AutoLogin).GetField("_pending", StaticPrivate)!.GetValue(null))?.IsCompleted == true, 5000)) throw new TimeoutException("Login task did not settle");
    }
    static void WaitCalls(int count)
    {
        if (!SpinWait.SpinUntil(() => LocalLoginClient.Calls == count, 5000)) throw new TimeoutException("Login did not begin");
    }
    static void Behavior()
    {
        Reset();
        Check(HarmonyLib.Harmony.Hooks.Count == 8, "all eight Harmony targets registered");
        Check(HarmonyLib.Harmony.Hooks.Values.Count(p => p.Postfix) == 1, "only OnReady uses postfix");
        var ui = new Il2CppInutan.UILoginSystem();
        ui.OnReady(); WaitCalls(1);
        for (int i = 0; i < 20; i++) { ui.OnStartGame("clicked"); Il2Cpp.SDKHandler.Current.LoginSDK(); AutoLogin.Tick(); }
        Check(LocalLoginClient.Calls == 1 && ui.GatewayCalls == 0, "repeated Start/Login clicks keep one pending authentication and no gateway");
        LocalLoginClient.Complete(new("uid-test", "name-test", "gateway-test")); WaitPending();
        Check(ui.DoneCalls == 0 && Il2Cpp.SDKHandler.Current._mainSDK.SDKUid == "", "task completion alone cannot mutate game state");
        AutoLogin.Tick();
        Check(ui.DoneCalls == 1 && ui.GatewayCalls == 0, "main-thread Tick applies login but waits for non-null RegionInfo");
        var sdk = (Il2Cpp.QuickSDK_cn_win)Il2Cpp.SDKHandler.Current._mainSDK;
        Check(sdk.SDKUid == "uid-test" && sdk.SDKToken == "gateway-test" && sdk.SDKUserName == "name-test", "login result copied into all SDK identity properties");
        ui.RegionInfo = new(); AutoLogin.Tick(); AutoLogin.Tick();
        Check(ui.GatewayCalls == 1 && ui.LastAddress == "uid-test", "region availability enters gateway exactly once with authenticated account");
        ui.OnStartGame("retry"); AutoLogin.Tick();
        Check(ui.GatewayCalls == 2 && LocalLoginClient.Calls == 1, "explicit gateway retry reuses successful authentication");
        Check(GameThread.Accesses.All(t => t == MainThread), "all game reflection property/callback access stays on main thread");
        Check(LocalLoginClient.Threads.All(t => t != MainThread), "authentication runs off main thread");
        sdk.UploadRoleInfo(new()); sdk.OpenUserCenter(); sdk.LogoutSDK();
        Check(sdk.NativeCalls == 0 && sdk.LogoutCalls == 1 && sdk.SDKUid == "" && sdk.SDKToken == "" && sdk.SDKUserName == "", "native SDK callbacks suppressed and logout clears identity");
        var iterator = new Il2Cpp.QuickSDK_cn_win._Init_d__2 { __4__this = sdk };
        Check(!iterator.MoveNext() && sdk.IsInit && iterator.NativeCalls == 0, "Init iterator marks IsInit and skips original SDK MoveNext");
        Reset(); ui = new() { RegionInfo = new() }; ui.OnReady(); WaitCalls(1);
        LocalLoginClient.Fail(new InvalidOperationException("secret request body")); WaitPending(); AutoLogin.Tick();
        Check(ui.GatewayCalls == 0 && ui.DoneCalls == 0, "authentication failure never enters gateway or emits login success");
        Check(MelonLoader.MelonLogger.Messages.Any(s => s.Contains("InvalidOperationException")) && !MelonLoader.MelonLogger.Messages.Any(s => s.Contains("secret request body")), "error logging includes type and excludes exception message");
        ui.OnStartGame("retry"); WaitCalls(2); LocalLoginClient.Complete(new("retry-uid", "retry-name", "retry-token")); WaitPending(); AutoLogin.Tick();
        Check(ui.GatewayCalls == 1 && ui.DoneCalls == 1, "Start retries failed authentication and enters after success");
        Reset(); ui = new() { RegionInfo = new() }; ui.OnReady(); WaitCalls(1); ui.Close();
        LocalLoginClient.Complete(new("closed", "closed", "closed")); WaitPending(); AutoLogin.Tick();
        Check(ui.GatewayCalls == 0 && ui.DoneCalls == 0, "Close while login pending prevents late completion entering gateway");
        Reset(); ui = new(); ui.OnReady(); WaitCalls(1); LocalLoginClient.Complete(new("closed", "closed", "closed")); WaitPending(); AutoLogin.Tick(); ui.Close(); ui.RegionInfo = new(); AutoLogin.Tick();
        Check(ui.GatewayCalls == 0, "Close after authentication but before region prevents entry");
        AutoLogin.Stop();
    }
}

internal static class GameThread
{
    internal static readonly List<int> Accesses = new();
    internal static void Record() => Accesses.Add(Environment.CurrentManagedThreadId);
}
