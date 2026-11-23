using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading;
using Il2Cpp;
using Il2CppInutan;
using Logic;
using Logic.Control;
using MelonLoader;
using mod;
using ModLoader;
using UnityEngine.SceneManagement;

public static class AIControlServer
{
    private static readonly object LifecycleGate = new object();
    private static AIControlHttpServer _server;
    private static string ModuleVersion => typeof(AIControlServer).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";

    // Names match the existing handlers. No Unity objects are accessed when serving this catalog.
    private static readonly Dictionary<string, string> LogicCommands = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["up"] = "检查所有模块的热更新（异步）",
        ["pic"] = "pic <文件名>：显示本地图片",
        ["picw"] = "picw <文件名>：在世界中显示图片",
        ["get"] = "get <cjw|cjwx|bx|moon>：调用现有采集功能",
        ["sm"] = "调用现有扫描功能",
        ["fish"] = "启动自动钓鱼",
        ["mj"] = "mj [start|stop|play|pause|status]；mj shot [秒数 半径 高度 环绕角度] 准备环绕镜头；mj shot clear；mj clean on|off；mj seek 0..1；mj fov 20..100；state.orbit.cinematic 返回拍摄状态",
        ["tb"] = "tb <动作ID>：播放动作",
        ["tbs"] = "tbs <动作ID> <t|f>：播放本机动作",
        ["api"] = "确认本机控制服务已启动",
        ["box"] = "切换碰撞箱及准星探测显示",
        ["light"] = "切换现有灯光对象",
        ["ttlf"] = "ttlf [子节点编号串]：切换对象显示",
        ["ttlm"] = "ttlm ...：沿用现有对象位置指令",
        ["paint"] = "打开 DrawGuessModule 绘画小游戏面板",
        ["dg"] = "你画我猜：connect <host> <port> <nick> | host <room> | join <room> <hostNick> | start | next | guess <词语> | leave | status", 
        ["sp"] = "sp [技能ID]：使用现有技能控制器",
        ["~stop"] = "停止技能控制状态",
        ["bundle"] = "bundle <help|list|find|assets|...>：资源包工具",
        ["/bundle"] = "/bundle ...：资源包工具别名",
        ["call"] = "call <命名空间.类型.静态方法> [参数...]：现有反射调用器"
    };
    private static readonly Dictionary<string, string> HostCommands = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["update"] = "检查所有模块的热更新（异步）",
        ["tp"] = "tp <x> <y> <z> 或 tp <玩家名|索引>",
        ["f"] = "f [速度]：现有高度控制",
        ["r"] = "r [x y z]：现有旋转控制",
        ["dcmx"] = "dcmx [玩家名]：模型导出",
        ["ai"] = "ai ...：沿用现有 AI 配置指令",
        ["put"] = "put ...：沿用现有摆放指令",
        ["m"] = "启用现有移动功能",
        ["gp"] = "gp <cl_list|cl_all|reput|stop>：现有家具管理",
        ["look"] = "look <1|2|3> 或 look fov <数值>",
        ["rt"] = "rt <0|1>：切换原地站立",
        ["chat"] = "chat ...：现有聊天控制（该指令可能对外发送消息）",
        ["by"] = "by ...：沿用现有指令",
        ["wave"] = "wave ...：沿用现有指令",
        ["boat"] = "boat ...：现有船只工具",
        ["net"] = "net ...：现有网络工具",
        ["#"] = "#：沿用现有指令"
    };

    public static void Start(int port = 8080)
    {
        lock (LifecycleGate)
        {
            if (_server != null && _server.IsRunning) return;
            Interlocked.Exchange(ref _server, null)?.Dispose();
            var server = new AIControlHttpServer(ExecuteCommand, ReadState, ReadHistory, SendChat,
                ReadCatalog, ModuleVersion, Log);
            try
            {
                server.Start(port);
                Volatile.Write(ref _server, server);
                Log($"服务已启动: http://127.0.0.1:{port}/api/ （session={server.SessionId}, v{ModuleVersion}）");
            }
            catch (Exception ex)
            {
                server.Dispose();
                MelonLogger.Error($"[AI_API] 启动失败: {ex.Message}。请检查端口占用及 Windows HTTP 监听权限。");
            }
        }
    }

    public static void Stop()
    {
        lock (LifecycleGate)
        {
            AIControlHttpServer server = Interlocked.Exchange(ref _server, null);
            if (server == null) return;
            server.Dispose();
            Log("服务已关闭，待执行请求已取消");
        }
    }

    public static void Tick() => Volatile.Read(ref _server)?.Tick();
    public static void Log(string message) => MelonLogger.Msg("[AI_API] " + message);

    private static object ReadCatalog() => new
    {
        defaultTarget = "auto",
        targets = new[] { "auto", "logic", "host" },
        commands = LogicCommands.Select(c => new { name = c.Key, target = "logic", usage = c.Value })
            .Concat(HostCommands.Select(c => new { name = c.Key, target = "host", usage = c.Value })).ToArray(),
        note = "completed 表示现有指令处理函数已返回；异步动作和游戏效果需要结合 state 或游戏日志确认。"
    };

    private static object ExecuteCommand(string target, string command)
    {
        // This method and every method below are called exclusively by the main-thread queue.
        string root = command.Split(' ')[0];
        if (target == "auto") target = LogicCommands.ContainsKey(root) ? "logic" : "host";
        Dictionary<string, string> catalog = target == "logic" ? LogicCommands : HostCommands;
        if (!catalog.ContainsKey(root)) throw new ArgumentException("未知指令或目标不匹配；使用 GET /api/commands 查看支持列表");
        RequirePlayer();
        ValidateArguments(root, command);
        if (target == "logic" && (root == "paint" || root == "dg"))
            TipMsgCore.i.updatecommand(TipMsgCore.i.mainplayername, command);
        else if (target == "logic") MainCore.i.mainplayermessage(command);
        else TipMsgCore.i.command(TipMsgCore.i.mainplayername, command, UI_Chat_Define.eMessageType.Text);
        // Invoke one existing handler chain, once. Never submit the input through game chat.
        return new { dispatched = true, target, command,
            message = "指令处理函数已返回；异步操作请继续检查状态或日志", state = ReadState() };
    }

    private static void RequirePlayer()
    {
        if (PlayerSystem.Instance == null || TipMsgCore.i.mainplayer == null || string.IsNullOrWhiteSpace(TipMsgCore.i.mainplayername))
            throw new InvalidOperationException("玩家尚未初始化，请进入可操作角色的场景后重试");
    }

    private static void ValidateArguments(string root, string command)
    {
        string[] args = command.Split(' ');
        if (new[] { "box", "light", "fish", "sm", "api", "paint", "up", "update", "~stop" }.Contains(root) && args.Length != 1)
            throw new ArgumentException(root + " 不接受参数");
        if (root == "mj") CameraOrbitSystem.ValidateCommand(command);
        if (root == "tp")
        {
            if (args.Length == 4) { for (int i = 1; i < 4; i++) RequireNumber(args[i]); }
            else if (args.Length == 2)
            {
                if (int.TryParse(args[1], out int index))
                {
                    if (index < 0 || index >= TipMsgCore.i.players.Count) throw new ArgumentException("玩家索引超出范围");
                }
                else if (!TipMsgCore.i.players.ContainsKey(args[1])) throw new ArgumentException("找不到目标玩家");
            }
            else throw new ArgumentException("用法: tp <x> <y> <z> 或 tp <玩家名|索引>");
        }
        if (root == "r")
        {
            if (args.Length != 1 && args.Length != 4) throw new ArgumentException("用法: r 或 r <x> <y> <z>");
            for (int i = 1; i < args.Length; i++) RequireNumber(args[i]);
        }
        if (root == "f")
        {
            if (args.Length > 2) throw new ArgumentException("用法: f [速度]");
            if (args.Length == 2) RequireNumber(args[1]);
        }
        if (root == "get" && (args.Length != 2 || !new[] { "cjw", "cjwx", "bx", "moon" }.Contains(args[1])))
            throw new ArgumentException("用法: get <cjw|cjwx|bx|moon>");
        if (new[] { "tb", "sp", "tbs" }.Contains(root))
        {
            if (root == "sp" && args.Length == 1) return;
            if (args.Length != (root == "tbs" ? 3 : 2) || !int.TryParse(args[1], out _))
                throw new ArgumentException("动作或技能指令参数不正确；使用 GET /api/commands 查看用法");
            if (root == "tbs" && args[2] != "t" && args[2] != "f") throw new ArgumentException("tbs 的最后一个参数只能是 t 或 f");
        }
        if (root == "rt" && (args.Length != 2 || (args[1] != "0" && args[1] != "1")))
            throw new ArgumentException("用法: rt <0|1>");
    }
    private static void RequireNumber(string text)
    {
        if (!float.TryParse(text, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.CurrentCulture, out float value) || float.IsNaN(value) || float.IsInfinity(value))
            throw new ArgumentException("坐标、角度和速度必须是有限数字");
    }

    private static object ReadState()
    {
        var player = TipMsgCore.i.mainplayer;
        object position = null;
        object rotation = null;
        if (player != null)
        {
            var p = player.transform.position;
            var r = player.transform.eulerAngles;
            position = new { x = p.x, y = p.y, z = p.z };
            rotation = new { x = r.x, y = r.y, z = r.z };
        }
        return new { moduleVersion = ModuleVersion, scene = SceneManager.GetActiveScene().name,
            playerReady = PlayerSystem.Instance != null && player != null && !string.IsNullOrWhiteSpace(TipMsgCore.i.mainplayername),
            playerName = TipMsgCore.i.mainplayername, position, rotation,
            players = TipMsgCore.i.players.Keys.ToArray(), hostCommandState = TipMsgCore.Stute.ToString(),
            orbit = CameraOrbitSystem.ReadState(),
            collisionDebug = ColliderDebugger.IsActive,
            raycastDebug = ColliderDebugger.UseRaycastMode, commandPanel = CommandPanelSystem.IsActive,
            modules = LogicLoader.Instance.GetAllModules().Select(m => new { id = m.Id, name = m.Logic?.Name, version = m.Logic?.Version, priority = m.Priority }).ToArray() };
    }

    private static object SendChat(string text)
    {
        if (UIChatSystem.Instance == null) throw new InvalidOperationException("UIChatSystem 尚未初始化");
        UIChatSystem.Instance.SendMessageData(text, 0, eChatChannelType.Current);
        return new { sent = true, context = text };
    }

    private static object ReadHistory()
    {
        if (UIChatSystem.Instance == null || UIChatSystem.Instance.ChatModel == null)
            throw new InvalidOperationException("UIChatSystem 尚未初始化");
        var history = new List<object>();
        var chatList = UIChatSystem.Instance.ChatModel.ChatMessages[eChatChannelType.Current];
        if (chatList != null)
            for (int i = Math.Max(0, chatList.Count - 20); i < chatList.Count; i++)
            {
                var message = chatList[i];
                history.Add(new { player = message.PlayerNickName, message = message.Message, time = message.Time.ToString("HH:mm:ss") });
            }
        return history;
    }
}
