using System.Reflection;
using Mono.Cecil;
using OverField.PrivateServer;

var harmony = new HarmonyLib.Harmony();
SandboxGacha.Install(harmony);
void Check(bool value, string label) { if (!value) throw new Exception(label); Console.WriteLine("PASS " + label); }
var model = new Il2CppInutan.GachaModel();
var limited = new Il2CppInutan.GachaData { GachaID = 3000 };
limited.CheckState();
Check(limited.GachaState == Il2CppInutan.GachaState.Close, "unadvertised expired pool stays closed");
limited.UpdateGachaData(new() { GachaId = 3000, IsFree = true });
Check(limited.GachaState == Il2CppInutan.GachaState.Open, "advertised free expired pool opens immediately");
Check(limited.StartTime.Year == 2000 && limited.EndTime.Year == 2090, "UI countdown has valid sandbox dates");
limited.CheckState();
Check(limited.GachaState == Il2CppInutan.GachaState.Open, "periodic calendar check preserves open state");
limited.IsForceClose = true;
limited.CheckState();
Check(limited.GachaState == Il2CppInutan.GachaState.Close, "maintenance force close is respected");
limited.IsForceClose = false;
limited.UpdateGachaData(new() { GachaId = 3000, IsFree = false });
Check(limited.GachaState == Il2CppInutan.GachaState.Close, "paid server response revokes exemption");
limited.UpdateGachaData(new() { GachaId = 3000, IsFree = true });
var other = new Il2CppInutan.GachaData { GachaID = 3001 };
other.CheckState();
Check(other.GachaState == Il2CppInutan.GachaState.Close, "other limited pools are not implicitly granted");
new Il2CppInutan.GachaSystem().Clear();
limited.CheckState();
Check(limited.GachaState == Il2CppInutan.GachaState.Close, "logout clears server pool permissions");
model.SetGacha(new() { new() { GachaId = 3000, IsFree = true } }, false, 0);
limited.CheckState();
Check(limited.GachaState == Il2CppInutan.GachaState.Open, "list callback opens pools even when scalar update bypasses protobuf overload");
model.SetGacha(new(), false, 0);
limited.CheckState();
Check(limited.GachaState == Il2CppInutan.GachaState.Close, "replacement list revokes removed pools");
Check(limited.StartTime == new DateTime(2020, 1, 1) && limited.EndTime == new DateTime(2021, 1, 1), "revocation restores original expired calendar");
limited.UpdateGachaData(new() { GachaId = 3000, IsFree = true });
var wrapper = new Il2CppInutan.GachaData(limited) { GachaID = 3000 };
new Il2CppInutan.GachaSystem().Clear();
wrapper.CheckState();
Check(wrapper.GachaState == Il2CppInutan.GachaState.Close && wrapper.EndTime.Year == 2021, "different wrapper with same ID restores shared native calendar after clear");
limited.UpdateGachaData(new() { GachaId = 3000, IsFree = true });
limited.StartTime = new DateTime(2035, 1, 1); limited.EndTime = new DateTime(2036, 1, 1);
limited.CheckState(); // server replaces dates while the pool is still free
limited.UpdateGachaData(new() { GachaId = 3000, IsFree = false });
Check(limited.GachaState == Il2CppInutan.GachaState.Close && limited.StartTime.Year == 2035 && limited.EndTime.Year == 2036, "fresh server calendar survives override and later revocation");
var normal = new Il2CppInutan.GachaData { GachaID = 4000, StartTime = DateTime.UtcNow.AddDays(-1), EndTime = DateTime.UtcNow.AddDays(1) };
normal.UpdateGachaData(new() { GachaId = 4000, IsFree = true });
normal.UpdateGachaData(new() { GachaId = 4000, IsFree = false });
Check(normal.GachaState == Il2CppInutan.GachaState.Open && normal.EndTime.Year != 2090, "revoking override preserves normally open paid calendar");
// Check the shipped IL2CPP metadata independently of the behavior doubles.
var metadataRoot = args.Length > 0 ? args[0] : "libs";
using var game = AssemblyDefinition.ReadAssembly(Path.Combine(metadataRoot, "Assembly-CSharp.dll"));
using var core = AssemblyDefinition.ReadAssembly(Path.Combine(metadataRoot, "Il2Cppmscorlib.dll"));
var data = game.MainModule.Types.Single(t => t.FullName == "Il2CppInutan.GachaData");
foreach (var name in new[] { "GachaID", "GachaState", "IsForceClose", "StartTime", "EndTime" })
    Check(data.Properties.Any(p => p.Name == name && p.GetMethod != null && p.SetMethod != null), "real writable contract " + name);
Check(data.Methods.Any(m => m.Name == "UpdateGachaData" && m.Parameters.Count == 1 && m.Parameters[0].ParameterType.FullName == "Il2CppPb.GachaInfo"), "real response callback signature");
var date = core.MainModule.Types.Single(t => t.FullName == "Il2CppSystem.DateTime");
Check(date.Properties.Any(p => p.Name == "Ticks" && p.GetMethod != null && p.PropertyType.FullName == "System.Int64"), "real date Ticks getter signature");
Check(date.Methods.Any(m => m.IsConstructor && !m.IsStatic && m.Parameters.Count == 3 && m.Parameters.All(p => p.ParameterType.FullName == "System.Int32")), "real date constructor signature");

namespace HarmonyLib {
    public sealed class HarmonyMethod { public string Name; public Type Type; public HarmonyMethod(Type t, string n) { Type=t; Name=n; } }
    public sealed class Harmony {
        static readonly Dictionary<string,(HarmonyMethod? Prefix, HarmonyMethod? Postfix)> Hooks = new();
        public void Patch(MethodInfo method, HarmonyMethod? postfix = null, HarmonyMethod? prefix = null) => Hooks[method.DeclaringType!.Name + "." + method.Name] = (prefix, postfix);
        public static void Fire(string name, params object[] args) => FirePhase(name, false, args);
        public static void FirePrefix(string name, params object[] args) => FirePhase(name, true, args);
        static void FirePhase(string name, bool prefix, object[] args) {
            if (Hooks.TryGetValue(name, out var hooks) && (prefix ? hooks.Prefix : hooks.Postfix) is { } h)
                h.Type.GetMethod(h.Name, BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,args);
        }
    }
}
namespace MelonLoader { public static class MelonLogger { public static void Msg(string text) => Console.WriteLine(text); public static void Warning(string text) => throw new Exception(text); } }
namespace Il2CppPb { public sealed class GachaInfo { public uint GachaId {get;set;} public bool IsFree {get;set;} } }
namespace Il2CppInutan {
    public enum GachaState { Open, Close }
    public sealed class GachaData {
        public int GachaID {get;set;}
        public GachaState GachaState {get;set;} = GachaState.Close;
        public bool IsForceClose {get;set;}
        sealed class Calendar { public DateTime Start = new(2020, 1, 1), End = new(2021, 1, 1); }
        readonly Calendar calendar;
        public GachaData() { calendar = new(); }
        public GachaData(GachaData native) { calendar = native.calendar; }
        public DateTime StartTime { get => calendar.Start; set => calendar.Start = value; }
        public DateTime EndTime { get => calendar.End; set => calendar.End = value; }
        public void CheckState() {
            HarmonyLib.Harmony.FirePrefix("GachaData.CheckState", this);
            GachaState = !IsForceClose && DateTime.UtcNow >= StartTime && DateTime.UtcNow < EndTime ? GachaState.Open : GachaState.Close;
            HarmonyLib.Harmony.Fire("GachaData.CheckState", this);
        }
        public void UpdateGachaData(Il2CppPb.GachaInfo info) { CheckState(); HarmonyLib.Harmony.Fire("GachaData.UpdateGachaData",this,info); }
    }
    public sealed class GachaModel { public void SetGacha(List<Il2CppPb.GachaInfo> info, bool value, int count) => HarmonyLib.Harmony.FirePrefix("GachaModel.SetGacha", info); }
    public sealed class GachaSystem { public void Clear() => HarmonyLib.Harmony.Fire("GachaSystem.Clear"); }
}
