using HarmonyLib;
using MelonLoader;
using ModLoader;
using mod;

namespace LocalHomeModule;

public sealed class Entry : ILogic
{
    public string Name => "LocalHomeModule";
    public string Version => "1.11.0";
    private static HomePanel? panel;
    internal static void Command(string[] args)
    {
        if(args.Length>1 && args[1]=="ui")
        {
            if(args.Length!=3 || (args[2]!="on" && args[2]!="off")) throw new ArgumentException("格式：lh ui on 或 lh ui off");
            panel?.SetVisible(args[2]=="on");
            MelonLogger.Msg(args[2]=="on"?"[LocalHome] 面板已开启，进入家园后显示":"[LocalHome] 面板已隐藏");
            return;
        }
        Runtime?.Command(args);
    }
    private HarmonyLib.Harmony? patches;
    internal static HomeRuntime? Runtime;
    public void OnLoad()
    {
        if (patches != null) return;
        try
        {
            Runtime=new HomeRuntime();
            panel=new HomePanel(Runtime);
            patches=new HarmonyLib.Harmony("mod.local_home"); patches.PatchAll(typeof(Entry).Assembly);
            MelonLogger.Msg("[LocalHome] 已加载，lh help 查看命令");
        }
        catch { OnUnload(); throw; }
    }
    public void OnUpdate() { Runtime?.Tick(); panel?.Tick(); }
    public void OnUnload()
    {
        patches?.UnpatchSelf(); patches=null;
        try { panel?.Dispose(); }
        finally { panel=null; try { Runtime?.Dispose(); } finally { Runtime=null; } }
    }
}
[HarmonyPatch(typeof(TipMsgCore), nameof(TipMsgCore.command))]
internal static class Commands
{
    [HarmonyPrefix]
    static bool Prefix(string name, string msg)
    {
        if (name != TipMsgCore.i.mainplayername || msg == null || !(msg == "lh" || msg.StartsWith("lh ",StringComparison.Ordinal))) return true;
        try { Entry.Command(msg.Split(' ',StringSplitOptions.RemoveEmptyEntries)); }
        catch(Exception e) { MelonLogger.Warning("[LocalHome] "+e.Message); }
        return false;
    }
}
