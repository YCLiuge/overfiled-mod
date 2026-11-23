using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Chat.Client;
using ChineseChessModule.Core;
using mod;
using mod.st;
using Newtonsoft.Json;

namespace ChineseChessModule;

public sealed class TableState
{
    public string Owner = "", Name = "", Instance = "", Red = "", Black = "";
    public ChessGame Game = new();
    public float X, Y, Z, Yaw;
    public double Seen;
    public bool Local;
    // Transport revision also covers seats, placement and resets.
    public long Revision;
}

/// <summary>Borrows the host Board connection. All table/game mutations happen on Update or UI calls.</summary>
public sealed class ChessRuntime : IDisposable
{
    private const string Magic = "xiangqi-v1";
    private const int MaxTables = 32, MaxMessage = 8192, MaxPending = 256;
    private sealed class Packet
    {
        public string Magic = "", Kind = "", Owner = "", Instance = "", Name = "", Red = "", Black = "";
        public long Revision;
        public int From = -1, To = -1;
        public float X, Y, Z, Yaw;
        public string Board = "";
        public int Turn, Winner = -1;
        public long GameRevision;
        public bool Finished;
    }
    private readonly struct Pending
    {
        public readonly int Epoch;
        public readonly Action Action;
        public Pending(int epoch, Action action) { Epoch = epoch; Action = action; }
    }
    private readonly NetChater _network = NetChater.Instance;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly ConcurrentQueue<Pending> _incoming = new();
    private readonly Queue<Packet> _outgoing = new();
    private readonly Dictionary<string, double> _retired = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource _lifetime = new();
    private static readonly JsonSerializerSettings JsonSettings = new()
    {
        TypeNameHandling = TypeNameHandling.None, MaxDepth = 8,
        MissingMemberHandling = MissingMemberHandling.Ignore
    };
    private ChatClient? _client;
    private Action<BoardChannelMessage>? _boardHandler;
    private Action<string>? _disconnectHandler;
    private string _channel = "";
    private int _hostRevision = -1, _epoch, _pending, _sending;
    private bool _ready, _started, _disposed;
    private double _lastSnapshot = -10, _lastSend = -10, _lastRequest = -10;
    private long _requestRevision = -1;
    private string _requestInstance = "";
    private double _requestAt;
    public readonly Dictionary<string, TableState> Tables = new(StringComparer.Ordinal);
    public string Selected = "";
    public string Notice = "放置棋盘即创建房间；Alt＋左键点击棋盘加入";
    public int SelectedIndex = -1;
    public bool PanelRequested;
    public TableState? Current => Tables.TryGetValue(Selected, out var table) ? table : null;
    private double Now => _clock.Elapsed.TotalSeconds;
    private string Self => _client?.SessionId ?? "";
    private bool Ready => !_disposed && _ready && ReferenceEquals(_client, _network.Client) &&
        _client?.IsConnected == true && _network.ChannelReady && _network.ChannelRevision == _hostRevision &&
        _network.DesiredChannel == _channel;

    public void Start()
    {
        if (_disposed || _started) return;
        _started = true;
        TipMsgCore.Tip_actions += OnLocalMessage;
        FollowChannel();
    }
    private void OnLocalMessage(string name, string text)
    {
        if (_disposed || TipMsgCore.i == null || name != TipMsgCore.i.mainplayername) return;
        string value = (text ?? "").Trim().TrimStart('.');
        if (value.Equals("xq", StringComparison.OrdinalIgnoreCase) || value.StartsWith("xq ", StringComparison.OrdinalIgnoreCase))
            Enqueue(Volatile.Read(ref _epoch), () => Command(value));
    }
    private void Enqueue(int epoch, Action action)
    {
        if (_disposed || epoch != Volatile.Read(ref _epoch)) return;
        if (Interlocked.Increment(ref _pending) > MaxPending)
        { Interlocked.Decrement(ref _pending); return; }
        _incoming.Enqueue(new Pending(epoch, action));
    }
    public void Update()
    {
        if (_disposed) return;
        FollowChannel();
        for (int i = 0; i < 64 && _incoming.TryDequeue(out var item); i++)
        {
            Interlocked.Decrement(ref _pending);
            if (item.Epoch != _epoch) continue;
            try { item.Action(); }
            catch (Exception) { Notice = "象棋消息无效，已忽略"; }
        }
        foreach (var table in Tables.Values.Where(t => !t.Local && t.Owner != Self && Now - t.Seen > 20).ToArray())
            Remove(table.Owner, false);
        foreach (var key in _retired.Where(p => Now - p.Value > 120).Select(p => p.Key).ToArray()) _retired.Remove(key);
        if (!Ready) return;
        if (Now - _lastRequest >= 8)
        { _lastRequest = Now; Queue(new Packet { Kind = "discover" }); }
        if (Now - _lastSnapshot >= 2)
        {
            _lastSnapshot = Now;
            if (Tables.TryGetValue(Self, out var own) && !own.Local) Publish(own);
        }
        SendNext();
    }
    private void FollowChannel()
    {
        var shared = _network.Client;
        bool ready = _network.ChannelReady && shared?.IsConnected == true;
        if (ReferenceEquals(shared, _client) && _hostRevision == _network.ChannelRevision &&
            _channel == _network.DesiredChannel && ready == _ready) return;
        Detach();
        Interlocked.Increment(ref _epoch);
        _outgoing.Clear(); _retired.Clear();
        foreach (var key in Tables.Where(p => !p.Value.Local).Select(p => p.Key).ToArray()) Remove(key, false);
        _client = shared; _ready = ready;
        _hostRevision = _network.ChannelRevision; _channel = _network.DesiredChannel;
        _lastSnapshot = _lastRequest = -10; ClearSelection();
        if (shared == null) return;
        int epoch = _epoch;
        _boardHandler = message =>
        {
            if (message.Text == null || message.Text.Length > MaxMessage) return;
            Enqueue(epoch, () => Receive(message));
        };
        _disconnectHandler = reason => Enqueue(epoch, () =>
        {
            _ready = false; _outgoing.Clear();
            foreach (var key in Tables.Where(p => !p.Value.Local).Select(p => p.Key).ToArray()) Remove(key, false);
            Notice = "宿主连接已断开，网络棋桌已清理";
        });
        shared.BoardMessageReceived += _boardHandler;
        shared.Disconnected += _disconnectHandler;
    }
    private void Detach()
    {
        if (_client == null) return;
        _client.BoardMessageReceived -= _boardHandler;
        _client.Disconnected -= _disconnectHandler;
        _boardHandler = null; _disconnectHandler = null;
    }
    private void ClearSelection()
    { SelectedIndex = -1; _requestRevision = -1; _requestInstance = ""; }
    public void Select(string owner)
    {
        if (_disposed) return;
        if (owner == null || !Tables.ContainsKey(owner)) { Notice = "棋桌已离开"; return; }
        Selected = owner; ClearSelection();
        Notice = Current!.Local ? "本地双人对弈" : "已选择 " + Current.Name + " 的棋桌";
    }
    public void OpenTable(string owner)
    {
        if (_disposed || !Tables.ContainsKey(owner)) return;
        Select(owner);
        var table = Current!;
        if (!table.Local && table.Owner != Self && table.Black.Length == 0) Join();
        else if (!table.Local && table.Owner != Self && table.Black != Self) Notice = "黑方已入座，已进入旁观";
        PanelRequested = true;
    }
    public void Command(string command)
    {
        if (_disposed) return;
        var parts = (command ?? "").Trim().TrimStart('.').Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || !parts[0].Equals("xq", StringComparison.OrdinalIgnoreCase)) return;
        PanelRequested = true;
        switch (parts.Length > 1 ? parts[1].ToLowerInvariant() : "status")
        {
            case "board": Place(); break;
            case "local": Notice = "已移除本地棋盘，请用 xq board 放置棋盘并创建房间"; break;
            case "join": Join(); break;
            case "leave": Leave(); break;
            case "reset": Reset(); break;
            case "resign": Resign(); break;
            case "status":
                Notice = Current == null ? "未选择棋桌；xq board / join / leave / reset / resign / status" :
                    Current.Name + "：" + Current.Game.Status + (Current.Local ? "（本地双人）" : Current.Black.Length == 0 ? "；等待黑方" : "；黑方已入座");
                break;
            default: Notice = "xq board / join / leave / reset / resign / status"; break;
        }
    }
    public void Place()
    {
        if (_disposed) return;
        FollowChannel();
        if (!Ready || Self.Length == 0) { Notice = "请等待宿主网络频道就绪"; return; }
        if (!Tables.TryGetValue(Self, out var table))
        {
            if (Tables.Count >= MaxTables) { Notice = "最多显示32桌，请先移除棋桌"; return; }
            table = new TableState { Owner = Self, Red = Self, Instance = Guid.NewGuid().ToString("N"), Name = PlayerName() };
        }
        if (!Locate(table)) return;
        Tables[Self] = table; Selected = Self; ClearSelection(); Changed(table);
        Notice = "棋桌已放置在身前；房主执红，首位加入者执黑";
    }
    private bool Locate(TableState table)
    {
        var player = TipMsgCore.i?.mainplayer;
        if (player == null) { Notice = "当前玩家位置尚未就绪"; return false; }
        var t = player.transform;
        var position = t.position + t.forward * 2f;
        if (!ValidCoordinate(position.x) || !ValidCoordinate(position.y) || !ValidCoordinate(position.z))
        { Notice = "玩家坐标无效"; return false; }
        table.X = position.x; table.Y = position.y; table.Z = position.z; table.Yaw = t.eulerAngles.y;
        table.Seen = Now; return true;
    }
    private static string PlayerName()
    {
        string name = TipMsgCore.i?.mainplayername ?? "棋友";
        return name.Length <= 64 ? name : name.Substring(0, 64);
    }
    public void Join()
    {
        if (_disposed) return;
        var table = Current;
        if (table == null) { Notice = "请先选择棋桌"; return; }
        if (table.Local) { Notice = "本地双人无需加入"; return; }
        if (!Ready) { Notice = "频道尚未就绪"; return; }
        if (table.Owner == Self || table.Black == Self) { Notice = "你已入座"; return; }
        if (table.Black.Length != 0) { Notice = "黑方已有棋友，你可旁观"; return; }
        Request(table, "join"); Notice = "已请求执黑；等待房主确认";
    }
    public void Leave()
    {
        if (_disposed || Current == null) return;
        var table = Current;
        if (table.Local) Remove(table.Owner, false);
        else if (table.Owner == Self)
        { Queue(For(table, "chess_remove")); Remove(table.Owner, true); }
        else
        {
            if (table.Black == Self && Ready) Request(table, "leave");
            Selected = ""; ClearSelection();
        }
        Notice = "已离开棋桌";
    }
    public void Reset()
    {
        var table = Current;
        if (_disposed || table == null) return;
        if (!table.Local && (table.Owner != Self || !Ready)) { Notice = "仅房主可重置棋局"; return; }
        table.Game.Reset(); ClearSelection(); Changed(table); Notice = "棋局已重置";
    }
    public void Resign()
    {
        var table = Current;
        if (_disposed || table == null || table.Game.Finished) return;
        if (table.Local) { table.Game.Resign(table.Game.Turn); Changed(table); return; }
        if (!Ready) { Notice = "频道尚未就绪"; return; }
        if (table.Owner == Self) { table.Game.Resign(Side.Red); Changed(table); }
        else if (table.Black == Self) Request(table, "resign");
        else Notice = "旁观者不能认输";
        SelectedIndex = -1;
    }
    public void Click(int index)
    {
        var table = Current;
        if (_disposed || table == null || index < 0 || index >= 90) return;
        if (table.Game.Finished) { Notice = table.Game.Status; return; }
        Side side = table.Game.Turn;
        if (!table.Local)
        {
            if (!Ready) { Notice = "频道尚未就绪"; return; }
            if (Self != table.Red && Self != table.Black) { Notice = "旁观者不能走棋"; return; }
            side = Self == table.Red ? Side.Red : Side.Black;
            if (side != table.Game.Turn) { Notice = "请等待对方走棋"; return; }
            if (table.Black.Length == 0) { Notice = "等待黑方加入"; return; }
        }
        var piece = table.Game.Board[index];
        if (piece != null && piece.Side == side)
        { SelectedIndex = SelectedIndex == index ? -1 : index; return; }
        if (SelectedIndex < 0) { Notice = "请先选择己方棋子"; return; }
        int from = SelectedIndex;
        if (!table.Game.LegalTargets(from).Contains(index)) { Notice = "此处不能落子"; return; }
        if (table.Local || table.Owner == Self)
        {
            if (table.Game.TryMove(from, index, out var error)) { SelectedIndex = -1; Changed(table); }
            else Notice = error;
        }
        else { Request(table, "move", from, index); SelectedIndex = -1; }
    }
    private void Request(TableState table, string kind, int from = -1, int to = -1)
    {
        // A snapshot acknowledges the request; after a lost packet allow a retry.
        if (_requestInstance == table.Instance && _requestRevision == table.Revision && Now - _requestAt < 2)
        { Notice = "正在等待房主确认"; return; }
        var p = For(table, kind); p.From = from; p.To = to;
        if (!Queue(p)) return;
        _requestInstance = table.Instance; _requestRevision = table.Revision; _requestAt = Now;
    }
    private void Changed(TableState table)
    {
        table.Revision++; table.Seen = Now;
        if (!table.Local) Publish(table);
        Notice = table.Game.Status;
    }
    private static Packet For(TableState t, string kind) => new()
    { Kind = kind, Owner = t.Owner, Instance = t.Instance, Revision = t.Revision };
    private void Publish(TableState table)
    {
        var p = For(table, "snapshot");
        p.Name = table.Name; p.Red = table.Red; p.Black = table.Black;
        p.X = table.X; p.Y = table.Y; p.Z = table.Z; p.Yaw = table.Yaw;
        p.Turn = (int)table.Game.Turn; p.Winner = table.Game.Winner.HasValue ? (int)table.Game.Winner.Value : -1;
        p.Finished = table.Game.Finished; p.GameRevision = table.Game.Revision;
        var chars = new char[90];
        for (int i = 0; i < chars.Length; i++)
        {
            var piece = table.Game.Board[i];
            chars[i] = piece == null ? '.' : piece.Side == Side.Red ? piece.Kind : char.ToLowerInvariant(piece.Kind);
        }
        p.Board = new string(chars); Queue(p);
    }
    private bool Queue(Packet packet)
    {
        if (!Ready) return false;
        packet.Magic = Magic;
        // Coalesce heartbeat snapshots without delaying player commands.
        if (packet.Kind == "snapshot")
        {
            var retained = _outgoing.Where(p => p.Kind != "snapshot" || p.Owner != packet.Owner).ToArray();
            _outgoing.Clear(); foreach (var p in retained) _outgoing.Enqueue(p);
        }
        if (_outgoing.Count >= 64) { Notice = "象棋发送队列已满，请稍后重试"; return false; }
        _outgoing.Enqueue(packet); return true;
    }
    private void SendNext()
    {
        if (_outgoing.Count == 0 || Volatile.Read(ref _sending) != 0 || Now - _lastSend < 0.1 || !Ready) return;
        var packet = _outgoing.Dequeue();
        var client = _client!; string channel = _channel;
        int epoch = _epoch, revision = _hostRevision;
        string text = JsonConvert.SerializeObject(packet, JsonSettings);
        if (text.Length > MaxMessage) return;
        _lastSend = Now; Interlocked.Exchange(ref _sending, 1);
        _ = SendAsync(client, channel, revision, epoch, text, packet.Kind);
    }
    private async Task SendAsync(ChatClient client, string channel, int revision, int epoch, string text, string kind)
    {
        try
        {
            if (_disposed || epoch != Volatile.Read(ref _epoch) || !ReferenceEquals(client, _network.Client) ||
                !_network.ChannelReady || _network.ChannelRevision != revision || _network.DesiredChannel != channel) return;
            await client.BroadcastBoardAsync(channel, text, _lifetime.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception error)
        {
            string code = (error as Chat.Protocol.ChatException)?.Code ?? "";
            // Log outside the bounded main-thread queue: a channel change or a full queue must
            // not discard the only evidence of a failed request. Never include the board payload.
            MelonLoader.MelonLogger.Error("[ChineseChess] BOARD send failed" +
                " kind=" + kind + " channel=" + channel + " session=" + client.SessionId +
                " endpoint=" + _network.Host + ":" + _network.Port +
                " bytes=" + System.Text.Encoding.UTF8.GetByteCount(text) + " code=" + code +
                " epoch=" + epoch + "/" + Volatile.Read(ref _epoch) +
                " channelRevision=" + revision + "/" + _network.ChannelRevision +
                " connected=" + client.IsConnected + " ready=" + _network.ChannelReady +
                " desiredChannel=" + _network.DesiredChannel +
                " exceptionType=" + error.GetType().FullName + Environment.NewLine + error +
                Environment.NewLine + error.StackTrace);
            string detail = code switch
            {
                "unknown_command" => "服务器不支持 BOARD 协议，请更新聊天中继",
                "not_in_channel" => "服务端频道与宿主不一致，请等待频道重新就绪",
                "board_rate_limited" => "服务端限制了棋盘消息速率或大小，请稍后重试",
                _ => error is TimeoutException ? "等待服务端确认超时" :
                    error.GetType().Name + ": " + error.Message
            };
            Enqueue(epoch, () => Notice = "象棋同步发送失败：" + detail + "；详见 MelonLoader 日志");
        }
        finally { Interlocked.Exchange(ref _sending, 0); }
    }
    private void Receive(BoardChannelMessage message)
    {
        string sender = message.AuthenticatedId;
        if (!Ready || message.Channel != _channel || !ValidId(sender) || sender == Self || message.Text.Length > MaxMessage) return;
        Packet? p;
        try { p = JsonConvert.DeserializeObject<Packet>(message.Text, JsonSettings); }
        catch (Exception) { return; }
        if (p == null || p.Magic != Magic) return;
        if (p.Kind == "discover")
        { if (Tables.TryGetValue(Self, out var own) && !own.Local) Publish(own); return; }
        if (!ValidId(p.Owner) || !ValidInstance(p.Instance) || p.Revision < 0 || p.Revision == long.MaxValue) return;
        if (p.Kind == "snapshot") { Snapshot(sender, message.Name, p); return; }
        if (p.Kind == "chess_remove")
        {
            if (sender == p.Owner && Tables.TryGetValue(sender, out var removed) && !removed.Local &&
                removed.Instance == p.Instance && p.Revision >= removed.Revision) Remove(sender, true);
            return;
        }
        if (p.Owner != Self || !Tables.TryGetValue(Self, out var table) || table.Local ||
            table.Instance != p.Instance) return;
        if (table.Revision != p.Revision) { Publish(table); return; }
        switch (p.Kind)
        {
            case "join":
                if (sender != table.Red && table.Black.Length == 0) { table.Black = sender; Changed(table); }
                else Publish(table);
                break;
            case "leave":
                if (sender == table.Black) { table.Black = ""; Changed(table); }
                break;
            case "resign":
                if (sender == table.Black && !table.Game.Finished) { table.Game.Resign(Side.Black); Changed(table); }
                break;
            case "move":
                if (sender != table.Black || table.Game.Turn != Side.Black || table.Game.Finished ||
                    p.From < 0 || p.From >= 90 || p.To < 0 || p.To >= 90) return;
                var piece = table.Game.Board[p.From];
                if (piece == null || piece.Side != Side.Black) return;
                if (table.Game.TryMove(p.From, p.To, out _)) { SelectedIndex = -1; Changed(table); }
                else Publish(table);
                break;
        }
    }
    private void Snapshot(string sender, string senderName, Packet p)
    {
        if (p.Owner != sender || p.Red != sender || !ValidId(p.Red) ||
            (p.Black != "" && (!ValidId(p.Black) || p.Black == p.Red)) ||
            p.Name == null || p.Name.Length > 64 || !ValidCoordinate(p.X) || !ValidCoordinate(p.Y) || !ValidCoordinate(p.Z) ||
            float.IsNaN(p.Yaw) || float.IsInfinity(p.Yaw) || Math.Abs(p.Yaw) > 36000 ||
            p.GameRevision < 0 || p.GameRevision > 10000000 || p.Turn < 0 || p.Turn > 1 || p.Winner < -1 || p.Winner > 1 ||
            (p.Finished != (p.Winner >= 0)) || _retired.ContainsKey(sender + "/" + p.Instance)) return;
        if (!TryDecode(p.Board, p.Finished, out var board)) return;
        Tables.TryGetValue(sender, out var table);
        if (table != null && (table.Local || (table.Instance == p.Instance && p.Revision < table.Revision))) return;
        if (table == null && Tables.Count >= MaxTables) return;
        var game = new ChessGame();
        game.LoadSnapshot(board, (Side)p.Turn, p.Winner < 0 ? (Side?)null : (Side)p.Winner, p.Finished, p.GameRevision);
        if (table == null) { table = new TableState { Owner = sender }; Tables.Add(sender, table); }
        else if (table.Instance != p.Instance) Retire(sender, table.Instance);
        bool changed = table.Instance != p.Instance || table.Revision != p.Revision;
        table.Instance = p.Instance; table.Name = string.IsNullOrEmpty(senderName) ? p.Name : senderName.Length <= 64 ? senderName : senderName.Substring(0, 64);
        table.Red = p.Red; table.Black = p.Black; table.Revision = p.Revision;
        table.Game = game; table.X = p.X; table.Y = p.Y; table.Z = p.Z; table.Yaw = p.Yaw; table.Seen = Now;
        if (Selected == sender && changed) { ClearSelection(); Notice = game.Status; }
    }
    private static bool TryDecode(string text, bool finished, out Piece?[] board)
    {
        board = new Piece?[90];
        if (text == null || text.Length != 90) return false;
        var counts = new int[2, 7];
        const string kinds = "KABNRCP";
        int[] limits = { 1, 2, 2, 2, 2, 2, 5 };
        for (int i = 0; i < 90; i++)
        {
            char c = text[i]; if (c == '.') continue;
            char kind = char.ToUpperInvariant(c); int k = kinds.IndexOf(kind);
            if (k < 0 || (c != kind && c != char.ToLowerInvariant(kind))) return false;
            int side = c == kind ? 0 : 1;
            if (++counts[side, k] > limits[k]) return false;
            board[i] = new Piece { Side = side == 0 ? Side.Red : Side.Black, Kind = kind };
        }
        return finished ? counts[0, 0] + counts[1, 0] >= 1 : counts[0, 0] == 1 && counts[1, 0] == 1;
    }
    private static bool ValidId(string value) => !string.IsNullOrEmpty(value) && value.Length <= 128 && !value.Any(char.IsControl);
    private static bool ValidInstance(string value) => value != null && value.Length == 32 && Guid.TryParseExact(value, "N", out _);
    private static bool ValidCoordinate(float value) => !float.IsNaN(value) && !float.IsInfinity(value) && Math.Abs(value) <= 1000000f;
    private void Retire(string owner, string instance)
    {
        if (_retired.Count >= 128) _retired.Remove(_retired.OrderBy(p => p.Value).First().Key);
        _retired[owner + "/" + instance] = Now;
    }
    private void Remove(string owner, bool retire)
    {
        if (!Tables.TryGetValue(owner, out var table)) return;
        if (retire) Retire(owner, table.Instance);
        Tables.Remove(owner);
        if (Selected == owner) { Selected = ""; ClearSelection(); }
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_started) TipMsgCore.Tip_actions -= OnLocalMessage;
        Detach(); Interlocked.Increment(ref _epoch);
        _lifetime.Cancel();
        _outgoing.Clear(); Tables.Clear(); _retired.Clear(); Selected = ""; ClearSelection();
        while (_incoming.TryDequeue(out _)) Interlocked.Decrement(ref _pending);
        // Never dispose, disconnect or leave channels on the host-owned client.
    }
}
