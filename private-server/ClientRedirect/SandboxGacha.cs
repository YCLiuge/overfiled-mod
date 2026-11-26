using System.Reflection;
using HarmonyLib;
using MelonLoader;

namespace OverField.PrivateServer;

// The server's free sandbox pools outlive the client resource calendar.
internal static class SandboxGacha
{
    private const BindingFlags Members = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    private static readonly HashSet<int> FreePools = new();
    // Index by pool ID: IL2CPP can expose several managed wrappers for one native object.
    private static readonly Dictionary<int, (object Start, object End)> Calendars = new();
    private static PropertyInfo _id = null!, _state = null!, _forceClose = null!, _start = null!, _end = null!, _ticks = null!, _serverId = null!, _serverFree = null!;
    private static object _open = null!, _min = null!, _max = null!;
    private static MethodInfo _check = null!;
    private static bool _reported;

    internal static void Install(HarmonyLib.Harmony harmony)
    {
        var data = Resolve("Il2CppInutan.GachaData");
        var info = Resolve("Il2CppPb.GachaInfo");
        var system = Resolve("Il2CppInutan.GachaSystem");
        _id = Property(data, "GachaID", typeof(int));
        _forceClose = Property(data, "IsForceClose", typeof(bool));
        _state = data.GetProperty("GachaState", Members) ?? throw new MissingMemberException("GachaState");
        if (!_state.CanWrite || !_state.PropertyType.IsEnum) throw new InvalidOperationException("GachaState contract changed");
        _open = Enum.Parse(_state.PropertyType, "Open");
        _start = data.GetProperty("StartTime", Members) ?? throw new MissingMemberException("StartTime");
        _end = Property(data, "EndTime", _start.PropertyType);
        _ticks = Property(_start.PropertyType, "Ticks", typeof(long));
        var date = _start.PropertyType.GetConstructor(new[] { typeof(int), typeof(int), typeof(int) }) ?? throw new MissingMethodException("DateTime(year, month, day)");
        _min = date.Invoke(new object[] { 2000, 1, 1 });
        _max = date.Invoke(new object[] { 2090, 1, 1 });
        _serverId = Property(info, "GachaId", typeof(uint));
        _serverFree = Property(info, "IsFree", typeof(bool));
        _check = Method(data, "CheckState", Type.EmptyTypes);
        var update = Method(data, "UpdateGachaData", new[] { info });
        var clear = Method(system, "Clear", Type.EmptyTypes);
        var model = Resolve("Il2CppInutan.GachaModel");
        var list = model.GetMethods(Members).Single(m => m.Name == "SetGacha" && m.GetParameters().Length == 3);
        // Resolve the whole interop contract before installing any hook.
        harmony.Patch(list, prefix: new HarmonyMethod(typeof(SandboxGacha), nameof(OnServerList)));
        harmony.Patch(_check, prefix: new HarmonyMethod(typeof(SandboxGacha), nameof(RestoreCalendar)),
            postfix: new HarmonyMethod(typeof(SandboxGacha), nameof(KeepAvailable)));
        harmony.Patch(update, postfix: new HarmonyMethod(typeof(SandboxGacha), nameof(OnServerInfo)));
        harmony.Patch(clear, postfix: new HarmonyMethod(typeof(SandboxGacha), nameof(Clear)));
        MelonLogger.Msg("[PrivateServer] Sandbox gacha calendar enabled for server-advertised free pools.");
    }

    private static void OnServerList(object __0)
    {
        FreePools.Clear();
        try
        {
            var type = __0.GetType();
            var count = (int)(type.GetProperty("Count")?.GetValue(__0) ?? 0);
            var item = type.GetProperty("Item") ?? throw new MissingMemberException("RepeatedField.Item");
            for (var i = 0; i < count; i++)
            {
                var info = item.GetValue(__0, new object[] { i })!;
                if ((bool)_serverFree.GetValue(info)!) FreePools.Add(checked((int)(uint)_serverId.GetValue(info)!));
            }
        }
        catch (Exception ex) { FreePools.Clear(); Report(ex); }
    }

    private static void Clear() => FreePools.Clear();

    private static void OnServerInfo(object __instance, object __0)
    {
        try
        {
            var id = checked((int)(uint)_serverId.GetValue(__0)!);
            if ((bool)_serverFree.GetValue(__0)!) FreePools.Add(id); else FreePools.Remove(id);
            _check.Invoke(__instance, null);
        }
        catch (Exception ex) { Report(ex); }
    }

    private static bool SameDate(object value, object expected) =>
        (long)_ticks.GetValue(value)! == (long)_ticks.GetValue(expected)!;

    private static void RestoreCalendar(object __instance)
    {
        try
        {
            var id = (int)_id.GetValue(__instance)!;
            if (!Calendars.TryGetValue(id, out var original)) return;
            // A fresh server/resource calendar wins over our saved values.
            if (SameDate(_start.GetValue(__instance)!, _min) && SameDate(_end.GetValue(__instance)!, _max))
            {
                _start.SetValue(__instance, original.Start);
                _end.SetValue(__instance, original.End);
            }
            Calendars.Remove(id);
        }
        catch (Exception ex) { Report(ex); }
    }

    private static void KeepAvailable(object __instance)
    {
        try
        {
            var id = (int)_id.GetValue(__instance)!;
            if (!FreePools.Contains(id) || (bool)_forceClose.GetValue(__instance)!) return;
            Calendars[id] = (_start.GetValue(__instance)!, _end.GetValue(__instance)!);
            _start.SetValue(__instance, _min);
            _end.SetValue(__instance, _max);
            _state.SetValue(__instance, _open);
        }
        catch (Exception ex) { Report(ex); }
    }

    private static void Report(Exception ex)
    {
        if (_reported) return;
        _reported = true;
        MelonLogger.Warning("[PrivateServer] Sandbox gacha calendar unavailable: " + (ex.InnerException ?? ex).Message);
    }

    private static Type Resolve(string name) => AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(name, false)).FirstOrDefault(t => t != null) ?? throw new TypeLoadException(name);
    private static PropertyInfo Property(Type type, string name, Type expected)
    {
        var p = type.GetProperty(name, Members) ?? throw new MissingMemberException(type.FullName, name);
        if (p.PropertyType != expected) throw new InvalidOperationException(name + " contract changed");
        return p;
    }
    private static MethodInfo Method(Type type, string name, Type[] args) => type.GetMethod(name, Members, null, args, null) ?? throw new MissingMethodException(type.FullName, name);
}
