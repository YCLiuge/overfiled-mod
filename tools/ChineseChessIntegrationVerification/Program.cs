using System.Reflection;
using Chat.Client;
using ChineseChessModule;
using ChineseChessModule.Core;
using mod;
using mod.st;
using Newtonsoft.Json.Linq;

internal static class Program
{
    private sealed class Peer : IDisposable
    {
        public readonly ChatClient Client;
        public readonly NetChater Host;
        public readonly ChessRuntime Runtime;
        public Peer(string id)
        {
            Client = new ChatClient(id);
            Host = new NetChater { Client = Client };
            NetChater.Instance = Host;
            Runtime = new ChessRuntime(); Runtime.Start();
        }
        public void Dispose() => Runtime.Dispose();
    }
    private static int _passed;
    private static void Check(bool value, string message)
    {
        if (!value) throw new Exception("FAIL: " + message);
        Console.WriteLine("PASS: " + message); _passed++;
    }
    private static FieldInfo Field(string name) => typeof(ChessRuntime).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static void Tick(params Peer[] peers)
    {
        foreach (var p in peers) { Field("_lastSend").SetValue(p.Runtime, -100d); p.Runtime.Update(); }
    }
    private static void Pump(params Peer[] peers) { for (int i = 0; i < 8; i++) Tick(peers); }
    private static JObject Request(TableState table, string kind, long? revision = null, string? instance = null, int from = -1, int to = -1) => new()
    {
        ["Magic"] = "xiangqi-v1", ["Kind"] = kind, ["Owner"] = table.Owner,
        ["Instance"] = instance ?? table.Instance, ["Revision"] = revision ?? table.Revision, ["From"] = from, ["To"] = to
    };
    private static void Inject(Peer peer, string sender, JObject packet) => peer.Client.Emit(sender, packet.ToString(Newtonsoft.Json.Formatting.None));
    private static JObject Snapshot(Peer peer) => JObject.Parse(peer.Client.Sent.Last(text => (string?)JObject.Parse(text)["Kind"] == "snapshot"));
    public static async Task<int> Main()
    {
        try
        {
            using var red = new Peer("red"); using var black = new Peer("black"); using var observer = new Peer("observer");
            var rr = red.Runtime; var br = black.Runtime; var or = observer.Runtime;
            rr.Start(); Check(TipMsgCore.Subscriptions == 3, "Start is idempotent");
            rr.Place(); var own = rr.Current!; Pump(red, black, observer);
            Check(own.X == 10 && own.Y == 20 && own.Z == 32 && own.Yaw == 45, "placement is 2m ahead of player transform");
            Check(br.Tables.ContainsKey("red") && or.Tables.ContainsKey("red"), "full snapshot discovers table at all peers");
            Check(br.Tables["red"].Game.Board.Count(p => p != null) == 32 && br.Tables["red"].Game.Status == own.Game.Status, "snapshot restores board and computed status");
            Check(red.Client.Sent.All(t => (string?)JObject.Parse(t)["Magic"] == "xiangqi-v1"), "every outgoing Board packet carries xiangqi magic");
            br.OpenTable("red");
            Check(br.PanelRequested && br.Selected == "red", "clicking placed table opens UI and selects room");
            Pump(black, red, observer);
            Check(own.Red == "red" && own.Black == "black" && br.Current!.Black == "black", "authenticated first joiner gets black seat");
            or.OpenTable("red"); Pump(observer, red, black);
            Check(own.Black == "black" && or.PanelRequested && or.Notice.Contains("旁观"), "clicking occupied table opens spectator UI without taking seat");
            long revision = own.Revision;
            or.Click(54); or.Click(45); Pump(observer, red, black);
            Check(own.Revision == revision && or.SelectedIndex == -1, "spectator Click cannot select or move");
            Inject(red, "observer", Request(own, "move", from: 27, to: 36)); Pump(red);
            Check(own.Revision == revision, "spoofed black move rejected using authenticated sender");
            Inject(red, "black", Request(own, "move", from: 27, to: 36)); Pump(red);
            Check(own.Revision == revision, "black cannot move on red turn");
            rr.Click(54); Check(rr.SelectedIndex == 54, "own piece selection exposed for highlight");
            rr.Click(45); Pump(red, black, observer);
            Check(own.Game.Board[45]?.Side == Side.Red && own.Game.Turn == Side.Black, "host red move is authoritative");
            Check(br.Current!.Game.Board[45]?.Side == Side.Red && br.Current.Game.Turn == Side.Black, "red move replicated by full snapshot");
            revision = own.Revision;
            Inject(red, "observer", Request(own, "move", from: 27, to: 36)); Pump(red);
            Check(own.Revision == revision && own.Game.Board[27] != null, "non-seat authenticated sender rejected even during black turn");
            Inject(black, "observer", Request(own, "chess_remove")); Pump(black);
            Check(br.Tables.ContainsKey("red"), "non-owner authenticated sender cannot remove another table");
            Inject(red, "black", Request(own, "move", revision - 1, from: 27, to: 36));
            Inject(red, "black", Request(own, "move", instance: Guid.NewGuid().ToString("N"), from: 27, to: 36));
            Inject(red, "black", Request(own, "move", from: 45, to: 36)); Pump(red);
            Check(own.Revision == revision && own.Game.Board[27] != null, "old revision, wrong instance and wrong-side source rejected");
            br.Click(27); br.Click(36); Pump(black, red, observer);
            Check(own.Game.Board[36]?.Side == Side.Black && own.Game.Turn == Side.Red && own.Revision == revision + 1, "black authenticated move accepted exactly once");
            Inject(red, "black", Request(own, "move", revision, from: 27, to: 36)); Pump(red);
            Check(own.Revision == revision + 1, "replayed prior move rejected");
            var valid = Snapshot(red); long seenRevision = or.Tables["red"].Revision;
            var spoof = (JObject)valid.DeepClone(); spoof["Revision"] = seenRevision + 100; spoof["Black"] = "observer";
            Inject(observer, "attacker", spoof); Pump(observer);
            Check(or.Tables["red"].Revision == seenRevision && or.Tables["red"].Black == "black", "snapshot cannot spoof another authenticated owner");
            var art = (JObject)valid.DeepClone(); art["Magic"] = "drawing-v1"; art["Revision"] = seenRevision + 10;
            Inject(observer, "red", art); Pump(observer);
            Check(or.Tables["red"].Revision == seenRevision, "drawing Board magic isolated");
            var old = (JObject)valid.DeepClone(); old["Revision"] = seenRevision - 1;
            Inject(observer, "red", old); Pump(observer);
            Check(or.Tables["red"].Revision == seenRevision, "old snapshot cannot overwrite current table");
            var malformed = (JObject)valid.DeepClone(); malformed["Revision"] = seenRevision + 10; malformed["Board"] = new string('K', 90);
            Inject(observer, "red", malformed); Pump(observer);
            malformed["Board"] = valid["Board"]!.DeepClone(); malformed["X"] = 1000001;
            Inject(observer, "red", malformed); Pump(observer);
            Check(or.Tables["red"].Revision == seenRevision, "invalid piece counts and out-of-range coordinates rejected");
            observer.Client.Emit("red", valid.ToString(), "wrong-channel"); observer.Client.Emit("red", new string('x', 8193)); Pump(observer);
            Check(or.Tables["red"].Revision == seenRevision, "wrong-channel and oversized messages ignored");
            int sends = red.Client.Sent.Count(t => (string?)JObject.Parse(t)["Kind"] == "snapshot");
            Field("_lastSnapshot").SetValue(rr, -10d); Pump(red);
            Check(red.Client.Sent.Count(t => (string?)JObject.Parse(t)["Kind"] == "snapshot") > sends, "2-second heartbeat branch broadcasts full state");
            await Task.Run(() => { for (int i = 0; i < 1000; i++) observer.Client.Emit("noise", "{}"); });
            Check((int)Field("_pending").GetValue(or)! <= 256 && or.Tables.Count == 1, "async callback only enqueues and queue remains bounded");
            Pump(observer);
            or.Tables["red"].Seen = -100; Tick(observer);
            Check(!or.Tables.ContainsKey("red"), "remote tables expire after 20 seconds");
            Inject(observer, "red", valid); Pump(observer);
            Check(or.Tables.ContainsKey("red"), "fresh snapshot rediscovers timed-out owner");
            br.Resign(); Pump(black, red, observer);
            Check(own.Game.Finished && own.Game.Winner == Side.Red && or.Tables["red"].Game.Finished, "black resignation authenticated and replicated");
            br.Reset(); Check(own.Game.Finished, "guest reset cannot change host state");
            revision = own.Revision; rr.Reset(); Pump(red, black, observer);
            Check(!own.Game.Finished && own.Game.Turn == Side.Red && own.Revision > revision, "host reset preserves monotonic transport revision");
            br.Leave(); Pump(black, red, observer);
            Check(own.Black == "" && br.Selected == "", "black leave releases seat");
            or.Select("red"); or.Join(); Pump(observer, red, black);
            Check(own.Black == "observer", "new first joiner can occupy vacated black seat");
            var before = Snapshot(red);
            int beforeLeaveSends = red.Client.Sent.Count;
            rr.Leave(); Pump(red, black, observer);
            var leaveKinds = red.Client.Sent.Skip(beforeLeaveSends).Select(text => (string?)JObject.Parse(text)["Kind"]).ToArray();
            Check(leaveKinds.Contains("chess_remove"), "host leave broadcasts chess_remove with isolated packet kind");
            Check(new[] { red, black, observer }.SelectMany(peer => peer.Client.Sent)
                .All(text => !new[] { "remove", "request", "info" }.Contains((string?)JObject.Parse(text)["Kind"])),
                "all chess broadcasts avoid drawing remove/request/info control kinds");
            Check(!br.Tables.ContainsKey("red") && !or.Tables.ContainsKey("red"), "host leave broadcasts authenticated removal");
            Inject(observer, "red", before); Pump(observer);
            Check(!or.Tables.ContainsKey("red"), "retired instance snapshot cannot resurrect removed table");
            or.Command("xq local");
            Check(!or.Tables.ContainsKey("local") && or.Notice.Contains("已移除"), "legacy local command cannot create a second board");
            rr.Place(); Pump(red, black, observer); string previousInstance = rr.Current!.Instance;
            Inject(red, "black", Request(rr.Current!, "join")); // Queue old-channel callback before switching.
            red.Host.DesiredChannel = "new"; red.Host.ChannelRevision++; red.Client.Channel = "new";
            Tick(red);
            Check(!rr.Tables.Any(p => !p.Value.Local), "channel revision clears network tables and old callbacks");
            observer.Host.DesiredChannel = "new"; observer.Host.ChannelRevision++; observer.Client.Channel = "new"; Tick(observer);
            Check(or.Tables.Count == 0, "channel switch clears boards with no local board left over");
            rr.Place(); Pump(red, observer);
            Check(rr.Current!.Instance != previousInstance && or.Tables.ContainsKey("red"), "new channel uses fresh table instance");
            red.Client.Drop(); Tick(red);
            Check(!rr.Tables.Any(p => !p.Value.Local), "disconnect clears own network table");
            TipMsgCore.Emit("foreign", "xq local"); Tick(black);
            Check(!br.Tables.ContainsKey("local"), "Tip commands from other players ignored");
            TipMsgCore.Emit("local", "xq local"); Tick(black);
            Check(!br.Tables.ContainsKey("local") && br.Notice.Contains("已移除"), "own Tip command dispatches but does not create local board");
            br.Dispose(); br.Dispose();
            Check(black.Client.IsConnected && black.Client.Subscriptions == 0 && TipMsgCore.Subscriptions == 2, "Dispose unsubscribes idempotently without closing host");
            using var cap = new Peer("capacity");
            for (int i = 0; i < 35; i++)
            {
                var p = (JObject)valid.DeepClone(); p["Owner"] = p["Red"] = "owner-" + i; p["Black"] = ""; p["Instance"] = Guid.NewGuid().ToString("N");
                Inject(cap, "owner-" + i, p);
            }
            Pump(cap);
            Check(cap.Runtime.Tables.Count == 32, "maximum 32 tables enforced");
            using var failure = new Peer("failure");
            MelonLoader.MelonLogger.Errors.Clear();
            failure.Client.SendFailure = new Chat.Protocol.ChatException("unknown_command", "server rejected BOARD");
            failure.Runtime.Place(); Pump(failure);
            Check(MelonLoader.MelonLogger.Errors.Any(x => x.Contains("code=unknown_command") &&
                x.Contains("kind=snapshot") && x.Contains("endpoint=probe-host:9000") &&
                x.Contains("channel=test") && x.Contains("session=failure") && x.Contains("bytes=") &&
                x.Contains("exceptionType=Chat.Protocol.ChatException") && x.Contains("ChessRuntime.SendAsync")),
                "send failure logs actual server code, context and explicit stack despite exception ToString override");
            Check(failure.Runtime.Notice.Contains("服务器不支持 BOARD 协议"), "unknown command explains missing relay protocol");
            failure.Client.SendFailure = new TimeoutException("server timeout");
            failure.Runtime.Reset(); Pump(failure);
            Check(failure.Runtime.Notice.Contains("等待服务端确认超时") &&
                MelonLoader.MelonLogger.Errors.Any(x => x.Contains("exceptionType=System.TimeoutException") && x.Contains("server timeout")),
                "timeout reports concrete notice and logs original exception");

            // Exercise cancellation of an in-flight send, not merely the disposed early return.
            failure.Client.SendFailure = null;
            failure.Client.SendOverride = token => Task.Delay(Timeout.Infinite, token);
            var send = typeof(ChessRuntime).GetMethod("SendAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
            int logCount = MelonLoader.MelonLogger.Errors.Count;
            var pendingSend = (Task)send.Invoke(failure.Runtime, new object[] {
                failure.Client, "test", failure.Host.ChannelRevision, (int)Field("_epoch").GetValue(failure.Runtime)!, "{}", "snapshot" })!;
            bool wasPending = !pendingSend.IsCompleted;
            failure.Runtime.Dispose();
            await pendingSend;
            Check(wasPending && MelonLoader.MelonLogger.Errors.Count == logCount &&
                (int)Field("_sending").GetValue(failure.Runtime)! == 0 && failure.Client.IsConnected,
                "Dispose cancels in-flight send without false error or closing host connection");
            Console.WriteLine($"ALL {_passed} CHECKS PASSED"); return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }
}
