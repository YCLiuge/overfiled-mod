using System.Reflection;
using HarmonyLib;
using MelonLoader;
using mod;
using ModLoader;
using HomeEditorModule.Core;
using AutoHomeItem = HomeEditorModule.Core.AutoHomeItem;
namespace HomeEditorModule;

public sealed class Entry : ILogic
{
    private HarmonyLib.Harmony? harmony;
    private readonly List<(MethodBase original, MethodInfo patch, string owner)> legacy = new();
    public string Name => "HomeEditorModule";
    public string Version => "1.0.0";
    public static string Diagnostics() => EditorRuntime.Snapshot();
    public void OnLoad()
    {
        if (harmony != null) return;
        try
        {
            harmony = new HarmonyLib.Harmony(EditorRuntime.Owner);
            // Retire only the known legacy home prefix; retain all other host tools.
            var target = AccessTools.Method(typeof(Il2CppInutan.UIHomeSystem), "OnClickPutdown");
            var info = HarmonyLib.Harmony.GetPatchInfo(target);
            if (info != null) foreach (var p in info.Prefixes)
                if (p.PatchMethod.DeclaringType?.FullName == "mod.FreeSetHomeItem+comUIHomeSystem_OnClickPutdown")
                { legacy.Add((target,p.PatchMethod,p.owner)); harmony.Unpatch(target,p.PatchMethod); }
            harmony.PatchAll(typeof(Entry).Assembly);
            MelonLogger.Msg("[HomeEditor] loaded; ~home activates editing");
        }
        catch { OnUnload(); throw; }
    }
    public void OnUpdate() => EditorRuntime.Tick();
    public void OnUnload()
    {
        try { EditorRuntime.Recover(); }
        finally
        {
            harmony?.UnpatchSelf(); harmony=null;
            foreach (var p in legacy) new HarmonyLib.Harmony(p.owner).Patch(p.original,prefix:new HarmonyMethod(p.patch));
            legacy.Clear();
        }
    }
}

[HarmonyPatch(typeof(TipMsgCore), nameof(TipMsgCore.command))]
internal static class HomeCommands
{
    [HarmonyPrefix]
    private static bool Prefix(string name, string msg)
    {
        if (string.IsNullOrWhiteSpace(msg) || name != TipMsgCore.i.mainplayername) return true;
        if (msg == "~home")
        {
            try { EditorRuntime.Enter(); }
            catch (Exception ex) { EditorRuntime.LastError=ex.Message; MelonLogger.Warning("[HomeEditor] " + ex.Message); }
            return false;
        }
        if (msg == "~stop" && (EditorRuntime.Active || TipMsgCore.Stute == Stute_tokensys.None)) { EditorRuntime.Recover(); return false; }
        if (!EditorRuntime.Active) return true;
        if (!EditorRuntime.Ready) { EditorRuntime.Recover(); return true; }
        if (msg == "-end") { EditorRuntime.Recover(); try { EditorRuntime.Enter(); } catch(Exception ex) { EditorRuntime.LastError=ex.Message; MelonLogger.Warning(ex.Message); } return false; }
        string verb = msg.Split(' ')[0];
        if (msg.StartsWith("-",StringComparison.Ordinal) || new[]{"use","setstep","setrostep","setbp","sq"}.Contains(verb))
        {
            try { AutoHomeItem.i.Update(msg); } catch (Exception ex) { EditorRuntime.LastError=ex.Message; EditorRuntime.Recover(); MelonLogger.Error(ex); }
            return false;
        }
        return true;
    }
}
