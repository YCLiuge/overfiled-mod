using System.Reflection;

namespace HarmonyLib
{
    public sealed class HarmonyMethod(Type type, string name)
    {
        internal MethodInfo Method => type.GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)!;
    }
    public sealed class Harmony
    {
        internal sealed record Hook(HarmonyMethod Patch, bool Postfix);
        internal static readonly Dictionary<string, Hook> Hooks = new();
        internal static string Key(MethodInfo method) => method.DeclaringType!.FullName + "." + method.Name;
        public void Patch(MethodInfo method, HarmonyMethod? prefix = null, HarmonyMethod? postfix = null)
            => Hooks[Key(method)] = new(postfix ?? prefix!, postfix != null);
        internal static bool Prefix(Type type, string name, object? instance, ref bool result)
        {
            if (!Hooks.TryGetValue(type.FullName + "." + name, out var hook) || hook.Postfix) return true;
            var method = hook.Patch.Method;
            var args = method.GetParameters().Select(p => p.Name == "__instance" ? instance : p.Name == "__result" ? (object)false : throw new Exception("Unsupported test hook parameter " + p.Name)).ToArray();
            var run = method.Invoke(null, args) is not false;
            var index = Array.FindIndex(method.GetParameters(), p => p.Name == "__result");
            if (index >= 0) result = (bool)args[index]!;
            return run;
        }
        internal static bool Prefix(Type type, string name, object? instance)
        {
            bool result = false;
            return Prefix(type, name, instance, ref result);
        }
        internal static void Postfix(Type type, string name, object instance)
        {
            if (Hooks.TryGetValue(type.FullName + "." + name, out var hook) && hook.Postfix) hook.Patch.Method.Invoke(null, new[] { instance });
        }
    }
}
namespace MelonLoader
{
    public static class MelonLogger
    {
        internal static readonly List<string> Messages = new();
        public static void Msg(string text) => Messages.Add(text);
        public static void Error(string text) => Messages.Add(text);
    }
}
namespace OverField.PrivateServer
{
    public sealed class LocalLoginResult(string uid, string userName, string gatewayToken)
    {
        public string Uid { get; } = uid;
        public string UserName { get; } = userName;
        public string GatewayToken { get; } = gatewayToken;
    }
    public sealed class LocalLoginClient
    {
        public LocalLoginClient(string endpoint, string accountFile) { _ = endpoint; _ = accountFile; }
        static TaskCompletionSource<LocalLoginResult> completion = NewCompletion();
        static int calls;
        internal static int Calls => Volatile.Read(ref calls);
        internal static readonly System.Collections.Concurrent.ConcurrentBag<int> Threads = new();
        static TaskCompletionSource<LocalLoginResult> NewCompletion() => new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal static void Reset() { completion = NewCompletion(); Volatile.Write(ref calls, 0); Threads.Clear(); }
        public Task<LocalLoginResult> LoginAsync(CancellationToken cancellationToken = default)
        {
            var task = completion.Task;
            Threads.Add(Environment.CurrentManagedThreadId);
            Interlocked.Increment(ref calls);
            return task;
        }
        internal static void Complete(LocalLoginResult result) => completion.SetResult(result);
        internal static void Fail(Exception error)
        {
            var old = completion;
            completion = NewCompletion();
            old.SetException(error);
        }
    }
}
namespace Il2Cpp
{
    public class MainSDK
    {
        string uid = "", token = "", name = "";
        bool init;
        public bool IsInit { get { GameThread.Record(); return init; } set { GameThread.Record(); init = value; } }
        public string SDKUid { get { GameThread.Record(); return uid; } set { GameThread.Record(); uid = value; } }
        public string SDKToken { get { GameThread.Record(); return token; } set { GameThread.Record(); token = value; } }
        public string SDKUserName { get { GameThread.Record(); return name; } set { GameThread.Record(); name = value; } }
        public int LogoutCalls;
        public void OnLogoutDone() { GameThread.Record(); LogoutCalls++; }
    }
    public sealed class SDKHandler
    {
        internal static SDKHandler Current = new();
        readonly QuickSDK_cn_win sdk = new();
        public static SDKHandler Instance { get { GameThread.Record(); return Current; } }
        public MainSDK _mainSDK { get { GameThread.Record(); return sdk; } }
        public int NativeCalls;
        public void LoginSDK() { if (HarmonyLib.Harmony.Prefix(typeof(SDKHandler), nameof(LoginSDK), this)) NativeCalls++; }
    }
    public sealed class GameRoleInfo { }
    public sealed class RegionInfo { }
    public sealed class QuickSDK_cn_win : MainSDK
    {
        public int NativeCalls;
        public void UploadRoleInfo(GameRoleInfo info) { if (HarmonyLib.Harmony.Prefix(typeof(QuickSDK_cn_win), nameof(UploadRoleInfo), this)) NativeCalls++; }
        public void OpenUserCenter() { if (HarmonyLib.Harmony.Prefix(typeof(QuickSDK_cn_win), nameof(OpenUserCenter), this)) NativeCalls++; }
        public void LogoutSDK() { if (HarmonyLib.Harmony.Prefix(typeof(QuickSDK_cn_win), nameof(LogoutSDK), this)) NativeCalls++; }
        public sealed class _Init_d__2
        {
            QuickSDK_cn_win owner = null!;
            public QuickSDK_cn_win __4__this { get { GameThread.Record(); return owner; } set { GameThread.Record(); owner = value; } }
            public int NativeCalls;
            public bool MoveNext()
            {
                bool result = false;
                if (HarmonyLib.Harmony.Prefix(typeof(_Init_d__2), nameof(MoveNext), this, ref result)) { NativeCalls++; return true; }
                return result;
            }
        }
    }
}
namespace Il2CppInutan
{
    public sealed class UILoginSystem
    {
        Il2Cpp.RegionInfo? region;
        public Il2Cpp.RegionInfo? RegionInfo { get { GameThread.Record(); return region; } set { GameThread.Record(); region = value; } }
        public int GatewayCalls, DoneCalls, ReadyCalls, CloseCalls;
        public string? LastAddress;
        public void OnReady() { GameThread.Record(); ReadyCalls++; HarmonyLib.Harmony.Postfix(typeof(UILoginSystem), nameof(OnReady), this); }
        public void Close() { GameThread.Record(); if (HarmonyLib.Harmony.Prefix(typeof(UILoginSystem), nameof(Close), this)) CloseCalls++; }
        public void OnSDKLoginDone() { GameThread.Record(); DoneCalls++; }
        public void OnStartGame(string address)
        {
            GameThread.Record();
            if (HarmonyLib.Harmony.Prefix(typeof(UILoginSystem), nameof(OnStartGame), this)) { GatewayCalls++; LastAddress = address; }
        }
    }
}
