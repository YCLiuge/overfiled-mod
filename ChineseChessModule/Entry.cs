using System;
using System.Reflection;
using ModLoader;
using MelonLoader;

namespace ChineseChessModule;
public sealed class Entry : ILogic
{
    public string Name => "ChineseChessModule";
    public string Version => typeof(Entry).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "1.0.0";
    private ChessRuntime? _runtime;
    private ChessPresentation? _display;
    public void OnLoad()
    {
        if (_runtime != null) return;
        try { _runtime = new ChessRuntime(); _runtime.Start(); _display = new ChessPresentation(_runtime); MelonLogger.Msg("[ChineseChess] 已加载，F9 打开中国象棋"); }
        catch { OnUnload(); throw; }
    }
    public void OnUpdate() { _runtime?.Update(); _display?.Update(); }
    public void OnUnload() { try { _display?.Dispose(); } finally { _display = null; _runtime?.Dispose(); _runtime = null; } }
}
