using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using HarmonyLib;
using Il2CppInutan;
using Il2CppPb;
using MelonLoader;
using ModLoader;

namespace ChatGuardModule;

public sealed class Entry : ILogic
{
    private const string Owner = "mod.chat_guard";
    private HarmonyLib.Harmony? harmony;
    private static GuardOptions options = new();
    private static readonly GuardPolicy policy = new(options);
    private static readonly SpellGuardPolicy spellPolicy = new();
    private static long spellReceived, spellDropped, spellErrors;
    private static bool active;
    private static long received, dropped, invalid, errors;
    private double nextConfig, nextLog;
    private static long livePassed;
    private readonly TrafficWindow traffic = new();
    private DateTime configStamp;
    private static readonly string ConfigPath = Path.Combine(Directory.GetCurrentDirectory(), "UserData", "ChatGuard.json");
    private static double Now => (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency;
    public string Name => "ChatGuardModule";
    public string Version => "1.2.1";
    public static string Diagnostics() => $"active={active}, received={Interlocked.Read(ref received)}, dropped={Interlocked.Read(ref dropped)}, invalid={Interlocked.Read(ref invalid)}, errors={Interlocked.Read(ref errors)}, tracked={policy.TrackedSenders}, spellReceived={Interlocked.Read(ref spellReceived)}, spellDropped={Interlocked.Read(ref spellDropped)}, spellErrors={Interlocked.Read(ref spellErrors)}, spellTracked={spellPolicy.Tracked}";

    public void OnLoad()
    {
        if (harmony != null) return;
        try
        {
            options = new GuardOptions();
            policy.Reset(options);
            spellPolicy.Reset(options.Spell);
            spellReceived = spellDropped = spellErrors = 0;
            nextConfig = nextLog = 0;
            configStamp = default;
            received = dropped = invalid = errors = livePassed = 0;
            traffic.Reset(Now);
            Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
            if (!File.Exists(ConfigPath)) File.WriteAllText(ConfigPath, JsonSerializer.Serialize(options, new JsonSerializerOptions { WriteIndented = true }));
            ReadConfig();
            harmony = new HarmonyLib.Harmony(Owner);
            // Explicit signatures fail load visibly on incompatible game versions.
            Patch("OnChatMsgNotice", new[] { typeof(ChatMsgNotice) }, nameof(Receive));
            Patch("ShowChatMessage", new[] { typeof(ChatChannelType), typeof(ChatMsgData), typeof(long), typeof(bool) }, nameof(Display));
            Patch("OnChatMsgRecordInitNotice", new[] { typeof(ChatMsgRecordInitNotice) }, nameof(History));
            var spellTarget = AccessTools.Method(typeof(Il2Cpp.SpellSystem), "ForceCastSpell", new[]
            {
                typeof(Il2Cpp.Unit), typeof(int), typeof(int), typeof(int), typeof(Il2Cpp.Unit),
                typeof(UnityEngine.Vector3), typeof(Il2Cpp.SpellCustomParameter), typeof(bool), typeof(Il2Cpp.SpellData).MakeByRefType()
            }) ?? throw new MissingMethodException("SpellSystem.ForceCastSpell(cacheSpellData, out spell)");
            harmony.Patch(spellTarget, prefix: new HarmonyMethod(typeof(Entry), nameof(CastSpell)) { priority = Priority.First });
            active = true;
            MelonLogger.Msg("[ChatGuard] loaded: receive rate limit + payload filter + bounded history + remote spell guard; " + ConfigPath);
        }
        catch { OnUnload(); throw; }
    }
    private void Patch(string method, Type[] parameters, string prefix)
    {
        MethodInfo target = AccessTools.Method(typeof(ChatSystem), method, parameters)
            ?? throw new MissingMethodException(typeof(ChatSystem).FullName, method);
        harmony!.Patch(target, prefix: new HarmonyMethod(typeof(Entry), prefix) { priority = Priority.First });
    }
    private void ReadConfig()
    {
        try
        {
            DateTime stamp = File.GetLastWriteTimeUtc(ConfigPath);
            if (stamp == configStamp) return;
            if (new FileInfo(ConfigPath).Length > 16384) throw new InvalidDataException("配置文件超过 16KB");
            var value = JsonSerializer.Deserialize<GuardOptions>(File.ReadAllText(ConfigPath), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new InvalidDataException("配置不能为空");
            value.Validate();
            policy.Reset(value);
            spellPolicy.Reset(value.Spell);
            options = value;
            configStamp = stamp;
            MelonLogger.Msg("[ChatGuard] configuration loaded");
        }
        catch (Exception ex)
        {
            // Remember invalid edit to avoid logging it every frame; next edit retries.
            configStamp = File.GetLastWriteTimeUtc(ConfigPath);
            MelonLogger.Warning("[ChatGuard] configuration rejected: " + ex.Message);
        }
    }
    public void OnUpdate()
    {
        double now = Now;
        if (now >= nextConfig) { nextConfig = now + 2; ReadConfig(); }
        if (now >= nextLog)
        {
            nextLog = now + 10;
            string rates = traffic.Sample(now, Interlocked.Read(ref received), Interlocked.Read(ref livePassed), Interlocked.Read(ref spellReceived), Interlocked.Read(ref spellDropped));
            if (options.LogStats && Interlocked.Read(ref dropped) + Interlocked.Read(ref invalid) + Interlocked.Read(ref errors) + Interlocked.Read(ref spellDropped) + Interlocked.Read(ref spellErrors) > 0)
                MelonLogger.Msg("[ChatGuard] " + rates + " totals: " + Diagnostics());
        }
    }
    public void OnUnload()
    {
        active = false;
        // Do not remove patches belonging to other modules.
        harmony?.UnpatchSelf();
        harmony = null;
        policy.Reset(new GuardOptions());
        spellPolicy.Reset(new SpellGuardOptions());
    }
    private static bool CastSpell(Il2Cpp.Unit __0, ref bool __result, ref Il2Cpp.SpellData __8)
    {
        if (!active || !options.Enabled || !options.Spell.Enabled) return true;
        try
        {
            // IsNetUnit is the game's own classification; do not assume a spell ID identifies an attacker.
            if (__0 == null || __0.Property == null || !__0.Property.IsNetUnit) return true;
            Interlocked.Increment(ref spellReceived);
            if (spellPolicy.Allow(__0.Pointer.ToInt64(), true, Now)) return true;
            Interlocked.Increment(ref spellDropped);
            __result = false;
            __8 = null!; // Preserve the failed-cast bool/out contract when skipping native code.
            return false;
        }
        catch
        {
            // Unknown unit identity / wrapper errors must not disable normal combat.
            Interlocked.Increment(ref spellErrors);
            return true;
        }
    }
    private static bool Receive(ChatMsgNotice __0)
    {
        if (!active || !options.Enabled) return true;
        try
        {
            var msg = __0?.Msg;
            // Non-message status notices must still reach the game handler.
            if (msg == null) return true;
            Interlocked.Increment(ref received);
            bool allow = policy.AllowLazy(msg.PlayerId, msg, static data => (data.Text, data.Name, data.Expression), Now);
            if (allow) Interlocked.Increment(ref livePassed);
            else Interlocked.Increment(ref dropped);
            return allow;
        }
        catch { Interlocked.Increment(ref errors); return false; }
    }
    private static bool Display(ChatMsgData __1)
    {
        if (!active || !options.Enabled) return true;
        try
        {
            // No second rate charge: the live notice already passed Receive.
            // This also validates content coming from history/private paths.
            bool allow = __1 != null && policy.ValidContent(__1.Text, __1.Name);
            if (!allow) Interlocked.Increment(ref invalid);
            return allow;
        }
        catch { Interlocked.Increment(ref errors); return false; }
    }
    private static bool History(ChatMsgRecordInitNotice __0)
    {
        if (!active || !options.Enabled) return true;
        try
        {
            var list = __0?.Msg;
            if (list == null || list.Count <= options.MaxHistoryBatch) return true;
            // Clear + append a bounded tail: avoid quadratic repeated RemoveAt(0).
            var tail = new List<ChatMsgData>(options.MaxHistoryBatch);
            int count = list.Count;
            for (int i = count - options.MaxHistoryBatch; i < count; i++) tail.Add(list[i]);
            list.Clear();
            foreach (var item in tail) list.Add(item);
            Interlocked.Add(ref dropped, count - tail.Count);
            return true;
        }
        catch { Interlocked.Increment(ref errors); return false; }
    }
}
