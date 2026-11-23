using System.Reflection;
using MelonLoader;
using ModLoader;

namespace DrawGuessModule;

public sealed class Entry : ILogic
{
    public string Name => "DrawGuessModule";
    public string Version => typeof(Entry).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "1.0.0";
    private MainLogic? _logic;
    public void OnLoad()
    {
        if (_logic != null) return;
        if (typeof(Chat.Client.ChatClient).GetMethod("BroadcastBoardAsync") == null ||
            typeof(Chat.Client.ChatClient).GetEvent("BoardMessageReceived") == null)
            throw new System.InvalidOperationException("频道画架需要新版宿主：请退出游戏，替换配套 mod.dll 并重启；中继服务器也需更新。");
        try
        {
            StartSupportedRuntime();
        }
        catch { OnUnload(); throw; }
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private void StartSupportedRuntime()
    {
        // Retire the known predecessor even when an older host cached its manifest.
        LogicLoader.Instance.UnloadModule("hot_update_demo");
        _logic = new MainLogic(); _logic.Start(); MelonLogger.Msg($"[{Name}] Loaded {Version}");
    }
    public void OnUpdate() => _logic?.Update();
    public void OnUnload() { try { _logic?.Dispose(); } finally { _logic = null; } }
}
