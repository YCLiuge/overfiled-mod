using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Chat.Client;
using DrawGuessModule.Core;
using MelonLoader;
using mod;
using Newtonsoft.Json;

namespace DrawGuessModule;

// Unity and game state are touched only from Update / local game command events.
internal sealed partial class MainLogic : IDisposable
{
    private const int MaxPending = 2048;
    private readonly ConcurrentQueue<Action> _incoming = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly CancellationTokenSource _lifetime = new();
    private readonly object _sendLock = new();
    private Task _sendTail = Task.CompletedTask;
    private ChatClient? _client;
    private GameRoomInfo? _room;
    private AuthoritativeGame? _game;
    private GameSnapshot? _view;
    private bool _disposed, _busy, _syncing;
    private int _generation, _roomEpoch, _pending, _sendPending, _networkOverflow;
    private readonly Dictionary<string, double> _lastSyncRequest = new();
    private long _localOp, _lastStroke, _lastRound = -1, _snapshotVersion = -1;
    private double _lastStateSent, _lastStateReceived, _viewReceivedAt;
    private string _syncId = "", _notice = "连接服务器后创建或加入房间";
    private long _syncRound, _syncDrawingVersion;
    private readonly List<DrawingOperation> _replay = new();
    private static readonly JsonSerializerSettings JsonSettings = new()
    {
        TypeNameHandling = TypeNameHandling.None, MaxDepth = 24,
        MissingMemberHandling = MissingMemberHandling.Ignore
    };
    private double Now => _clock.Elapsed.TotalSeconds;
    private bool IsHost => _room != null && _client?.SessionId == _room.HostId;

    public void Start()
    {
        PaintingSystem.Initialize(Command);
        PaintingSystem.SetSceneBoardAutomaticPlacement(false);
        TipMsgCore.Tip_actions += OnLocalMessage;
        RefreshCharacterName();
        _notice = "F8 打开画板；选择你画我猜或自由绘画同步模式";
    }

    private void OnLocalMessage(string name, string text)
    {
        if (_disposed || TipMsgCore.i == null || name != TipMsgCore.i.mainplayername) return;
        string value = (text ?? "").Trim().TrimStart('.');
        if (value == "paint" || value == "dg" || value.StartsWith("dg ", StringComparison.Ordinal)) Command(value);
    }

    public void Update()
    {
        if (_disposed) return;
        FollowGameChannel();
        if (Interlocked.Exchange(ref _networkOverflow, 0) != 0 && _room != null)
        {
            _busy = false; LeaveRoom(); Notice("绘画消息积压，已退出房间；宿主网络仍保持连接");
        }
        for (int i = 0; i < 128 && _incoming.TryDequeue(out Action? action); i++)
        {
            Interlocked.Decrement(ref _pending);
            try { action(); } catch (Exception e) { Notice("网络状态处理失败：" + e.Message); }
        }
        RefreshCharacterName();
        if (_free != null && IsHost && ChannelReady)
        {
            FlushFreeTransport();
            if (Now - _lastStateSent >= 1) PublishState(false);
        }
        else if (_game != null && IsHost && ChannelReady)
        {
            if (_game.Tick(Now)) PublishState(true);
            else if (Now - _lastStateSent >= 1) PublishState(false);
        }
        else if (_room != null && Now - _lastStateReceived > 15)
        {
            LeaveRoom(); Notice("房主状态超时，已退出绘画房间；请重新加入");
        }
        if (IsFree && _room != null && !IsHost && _syncing && Now - _lastFreeSyncProgress >= 3)
        {
            _lastFreeSyncProgress = Now;
            Submit(new GameCommand { Type = "sync" });
        }
        TickBoards();
        UpdatePanel();
        PaintingSystem.Tick();
    }

    private void Enqueue(int generation, Action action)
    {
        if (_disposed || generation != Volatile.Read(ref _generation)) return;
        if (Interlocked.Increment(ref _pending) > MaxPending)
        {
            Interlocked.Decrement(ref _pending);
            // Release only this module's room on the main thread; the socket belongs to the host.
            Interlocked.Exchange(ref _networkOverflow, 1);
            return;
        }
        _incoming.Enqueue(() => { if (!_disposed && generation == _generation) action(); });
    }

    private void Notice(string text) { _notice = text; MelonLogger.Msg("[DrawGuess] " + text); }
    private static string Explain(string code) => code switch
    {
        "drawer_only" => "只有当前画手可以修改画布",
        "host_only" => "只有房主可以执行此操作",
        "drawer_cannot_guess" => "画手不能猜自己的题目",
        "already_guessed" => "你已经猜中了，请等待下一轮",
        "guess_rate_limited" => "猜词过于频繁，请稍后再试",
        "command_rate_limited" => "操作过于频繁，请稍后再试",
        "not_enough_players" => "至少需要两名玩家才能开始",
        "invalid_settings" => "每人轮数为 1–3，每轮时间为 30–180 秒",
        "drawing_full" => "当前画布笔迹已达上限，请清空画布或撤销后继续",
        "nothing_to_undo" => "当前没有可撤销的笔画",
        "invalid_phase" => "当前阶段不支持此操作",
        "stale_round" => "回合已切换，请等待新状态",
        "invalid_drawing" => "画笔参数无效",
        "invalid_word" => "题目须为 1–16 字且不能包含控制字符",
        "invalid_guess" => "答案需为 1–64 字符",
        _ => code
    };

    internal void Command(string command)
    {
        if (_disposed) return;
        try
        {
            FollowGameChannel();
            string[] p = command.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (p.Length == 0) return;
            if (p[0] == "paint" || (p[0] == "dg" && p.Length == 1)) { PaintingSystem.Show(); return; }
            if (p[0] != "dg") return;
            string type = p[1].ToLowerInvariant();
            if (type == "hide") { PaintingSystem.Hide(); return; }
            if (type == "mode")
            {
                if (p.Length != 3) throw new ArgumentException("dg mode guess / dg mode free");
                SwitchMode(p[2]); return;
            }
            if (type == "save")
            {
                if (!IsFree) throw new InvalidOperationException("请切换到自由绘画模式后保存画作");
                if (_syncing) throw new InvalidOperationException("画布正在同步，请同步完成后保存");
                Notice("画作已保存：" + PaintingSystem.SaveLocalPainting(p.Length > 2 ? string.Join(" ", p.Skip(2)) : null)); return;
            }
            if (type == "boardselect" || type == "boardjoin") { if (p.Length < 3 || p.Length > 4) throw new ArgumentException("请按住 Alt 点击场景画架"); SelectBoard(p[2], p.Length == 4 ? p[3] : null); return; }
            if (type == "status") { Notice(Describe() + "\n" + PaintingSystem.DescribeSceneBoard()); return; }
            if (type == "board")
            {
                CreateOwnBoard(); return;
            }
            if (type == "connect")
            {
                if (p.Length == 2) { Connect(_network.Host, _network.Port, CharacterName()); return; }
                if (p.Length == 3 && (p[2] == "local" || p[2] == "public"))
                {
                    bool local = p[2] == "local";
                    Connect(local ? PaintingDefaults.LocalHost : PaintingDefaults.PublicHost, local ? PaintingDefaults.LocalPort : PaintingDefaults.PublicPort, CharacterName()); return;
                }
                if ((p.Length != 4 && p.Length != 5) || !int.TryParse(p[3], out int port) || port < 1 || port > 65535)
                    throw new ArgumentException("dg connect [public / local] 或 dg connect <服务器地址> <端口>（昵称自动使用角色名）");
                Connect(p[2], port, CharacterName()); return;
            }
            if (type == "host" || type == "join")
            {
                if (p.Length < 3 || p.Length > (type == "join" ? 4 : 3)) throw new ArgumentException("dg host <房间> / dg join <房间> [房主昵称]");
                EnterRoom(type == "host", p[2], p.Length == 4 ? p[3] : null); return;
            }
            if (type == "leave") { LeaveRoom(); return; }
            if (type == "close")
            {
                if (!IsHost) throw new InvalidOperationException("只有房主可以关闭房间");
                LeaveRoom(); return;
            }
            if ((_room == null || _client == null) && !IsFree) throw new InvalidOperationException("请先连接并加入房间");
            if (type == "sync") { Submit(new GameCommand { Type = "sync" }); return; }
            if (IsFree && type != "draw") throw new InvalidOperationException("自由绘画没有回合、猜词或积分；请使用绘画工具或保存 PNG");
            var input = new GameCommand { Type = type, SessionId = _room?.InstanceId ?? LocalCanvas.SessionId, RoundId = IsFree ? 0 : _view?.RoundId ?? 0 };
            switch (type)
            {
                case "start":
                    if (!IsHost) throw new InvalidOperationException("只有房主可以开始游戏");
                    if (p.Length > 2) input.TurnsPerPlayer = int.Parse(p[2], CultureInfo.InvariantCulture);
                    if (p.Length > 3) input.RoundSeconds = int.Parse(p[3], CultureInfo.InvariantCulture);
                    if (p.Length > 4) throw new ArgumentException("dg start [每人轮数] [每轮秒数]");
                    break;
                case "next": case "close":
                    if (p.Length != 2) throw new ArgumentException("此指令不接受参数");
                    break;
                case "word": case "guess":
                    if (p.Length < 3) throw new ArgumentException("dg guess <答案>");
                    input.Text = string.Join(" ", p.Skip(2)); break;
                case "draw":
                    if (p.Length != 10) throw new ArgumentException("绘画参数无效");
                    long stroke = long.Parse(p[9], CultureInfo.InvariantCulture);
                    input.Operation = new DrawingOperation
                    {
                        Tool = p[2], X1 = int.Parse(p[3], CultureInfo.InvariantCulture), Y1 = int.Parse(p[4], CultureInfo.InvariantCulture),
                        X2 = int.Parse(p[5], CultureInfo.InvariantCulture), Y2 = int.Parse(p[6], CultureInfo.InvariantCulture),
                        Color = p[7].StartsWith("#") ? p[7] : "#" + p[7], Size = int.Parse(p[8], CultureInfo.InvariantCulture),
                        NewStroke = stroke != _lastStroke, OpId = ++_localOp, RoundId = input.RoundId
                    };
                    _lastStroke = stroke; break;
                default: throw new ArgumentException("支持 connect / host / join / start / next / guess / leave / status / sync");
            }
            Submit(input);
        }
        catch (Exception e) { Notice(e.Message); }
    }

    private void Connect(string host, int port, string nick)
    {
        if (_busy) throw new InvalidOperationException("正在连接或加入，请稍候");
        if (_room != null) throw new InvalidOperationException("请先离开当前房间再连接服务器");
        if (string.IsNullOrWhiteSpace(nick) || nick.Length > 32) throw new ArgumentException("昵称长度需为 1–32 字符");
        _busy = true;
        Notice("正在连接宿主网络");
        _ = Task.Run(async () =>
        {
            try
            {
                await _network.ConnectAsync(host, port, nick, _lifetime.Token).ConfigureAwait(false);
                if (!_disposed) Enqueue(Volatile.Read(ref _generation), () =>
                {
                    _busy = false; FollowGameChannel();
                    Notice("已复用宿主网络，昵称：" + _client?.Nick + "；等待游戏频道同步");
                });
            }
            catch (Exception e)
            {
                if (!_disposed) Enqueue(Volatile.Read(ref _generation), () => { _busy = false; Notice("宿主连接失败：" + e.Message); });
            }
        });
    }

    private void EnterRoom(bool host, string room, string? expectedHost, string? expectedOwner = null, string? expectedInstance = null)
    {
        if (_busy) throw new InvalidOperationException("正在处理连接请求");
        var client = _client;
        if (client == null || !client.IsConnected) throw new InvalidOperationException("请先 dg connect");
        if (!ChannelReady) throw new InvalidOperationException("请等待游戏频道同步完成后创建或加入绘画房间");
        if (_room != null) throw new InvalidOperationException("请先离开当前房间");
        if (IsFree)
        {
            if (room.Length > 59) throw new ArgumentException("自由画室名称最长 59 个字符");
            room = "free." + room;
        }
        else if (room.StartsWith("free.", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("free. 是自由画室的保留前缀，请使用其他游戏房间名");
        if (IsFree) throw new InvalidOperationException("普通画架自动同步到频道，无需自由画室；请切换你画我猜再创建游戏房间");
        var candidates = _boards.Values.Where(b => b.Room == room && b.RoomInstance.Length > 0 &&
            (expectedHost == null || b.Name == expectedHost) && (expectedOwner == null || b.Owner == expectedOwner) &&
            (expectedInstance == null || b.RoomInstance == expectedInstance)).ToArray();
        if (!host && candidates.Length > 1) throw new InvalidOperationException("发现多个同名房间画架，请按住 Alt 点击要加入的画架");
        ChannelBoard? binding = host ? _ownBoard : candidates.SingleOrDefault();
        if (binding == null) throw new InvalidOperationException(host ? "请先放置自己创建的画架，再创建游戏房间" : "请按住 Alt 点击房主已绑定游戏房间的画架加入");
        string boundOwner = binding.Owner, boundInstance = binding.RoomInstance;
        _busy = true; int generation = _generation;
        _requestedRoom = room;
        int epoch = ++_roomEpoch, channelRevision = _channelRevision; string requestedMode = _mode, channel = _desiredChannel;
        _ = Task.Run(async () =>
        {
            bool locked = false;
            try
            {
                await _channelGate.WaitAsync(_lifetime.Token).ConfigureAwait(false); locked = true;
                if (channelRevision != Volatile.Read(ref _channelRevision) || generation != Volatile.Read(ref _generation) || !ReferenceEquals(_network.Client, client) || !_network.ChannelReady || _network.DesiredChannel != channel) return;
                var joined = host ? await client.CreateGameAsync(room, _lifetime.Token).ConfigureAwait(false)
                                  : await client.JoinGameAsync(room, _lifetime.Token).ConfigureAwait(false);
                if (channelRevision != Volatile.Read(ref _channelRevision) || joined.Channel != channel)
                {
                    await client.LeaveGameAsync(room, _lifetime.Token).ConfigureAwait(false);
                    throw new InvalidOperationException("绘画频道与当前游戏频道不一致；请确认服务器已更新并重新加入");
                }
                if (!host && (joined.HostId != boundOwner || joined.InstanceId != boundInstance || (expectedHost != null && joined.HostNick != expectedHost)))
                {
                    await client.LeaveGameAsync(room, _lifetime.Token).ConfigureAwait(false);
                    throw new InvalidOperationException("服务器返回的房主昵称与指定房主不一致");
                }
                Enqueue(generation, () =>
                {
                    if (epoch != _roomEpoch || channelRevision != _channelRevision || requestedMode != _mode || !ReferenceEquals(_client, client) || !client.IsConnected) return;
                    _busy = false; _room = joined; _lastStateReceived = Now; _view = null;
                    _game = host && !IsFree ? new AuthoritativeGame(joined.HostId, joined.InstanceId, Now) : null;
                    _free = host && IsFree ? new SharedCanvas(joined.HostId, joined.InstanceId, Now) : null;
                    _freeView = null;
                    ResetFreeTransport();
                    PaintingSystem.SetSceneBoardAutomaticPlacement(false);
                    if (host && _ownBoard != null) PaintingSystem.SetSceneBoardPlacement(_ownBoard.Placement);
                    PaintingSystem.ResetCanvas(); _lastRound = -1; _localOp = 0; _lastStroke = 0; _appliedSequence = 0; _syncing = !host;
                    if (_game != null || _free != null)
                    {
                        foreach (var member in joined.Members)
                        {
                            _game?.AddPlayer(member.Id, member.Nick, Now);
                            _free?.AddPlayer(member.Id, member.Nick, Now);
                        }
                        PublishState(true);
                    }
                    else Submit(new GameCommand { Type = "sync", SessionId = joined.InstanceId });
                    _boardDirty = true; _lastBoardInfo = -10; TickBoards();
                    PaintingSystem.Show(); Notice(host ? "房间已绑定自己的游戏画架，其他玩家按住 Alt 点击此画架加入" : "已加入，等待房主同步");
                });
            }
            catch (Exception e)
            {
                Enqueue(generation, () =>
                {
                    if (epoch != _roomEpoch || !ReferenceEquals(_client, client)) return;
                    _busy = false; _requestedRoom = null; Notice("房间操作失败：" + e.Message);
                });
            }
            finally { if (locked) _channelGate.Release(); }
        });
    }

    private void OnRoomChanged(GameRoomEvent change)
    {
        if (_room == null || change.Room.Room != _room.Room || change.Room.InstanceId != _room.InstanceId || change.Room.Channel != _desiredChannel) return;
        _room = change.Room;
        if (change.Type == "closed")
        {
            foreach (var board in _boards.Values.Where(b => b.RoomInstance == change.Room.InstanceId))
            { board.Room = ""; board.RoomInstance = ""; RenderBoard(board, new List<DrawingOperation>(), false); }
            ClearRoom(); Notice("房主离开，游戏房间已关闭"); return;
        }
        if (IsFree) { OnFreeRoomChanged(change); return; }
        if (_game == null) return;
        if (change.Type == "join")
        {
            var member = change.Room.Members.FirstOrDefault(m => m.Id == change.MemberId);
            if (member != null) _game.AddPlayer(member.Id, member.Nick, Now);
            PublishState(true);
            SendReplay(change.MemberId);
        }
        else if (change.Type == "leave") { _game.RemovePlayer(change.MemberId, Now); PublishState(true); }
    }

    private void Submit(GameCommand input)
    {
        if (IsFree && _room == null) { if (_selectedBoard.Length > 0 && _selectedBoard != _client?.SessionId) { Notice("普通画架仅创建者可以绘画"); return; } SubmitOffline(input); return; }
        if (_room == null || _client == null || !ChannelReady) return;
        input.SessionId = _room.InstanceId;
        if (IsHost) HandleInput(_client.SessionId, input);
        else Send(new WireEnvelope { Kind = "input", SessionId = _room.InstanceId, Command = input }, _room.HostId);
    }

    private void HandleInput(string sender, GameCommand input)
    {
        if (IsFree) { HandleFreeInput(sender, input); return; }
        if (_game == null || _room == null || input.SessionId != _room.InstanceId) return;
        if (!_room.Members.Any(m => m.Id == sender)) return;
        if (input.Type == "sync")
        {
            if (_lastSyncRequest.TryGetValue(sender, out double last) && Now - last < 2) return;
            _lastSyncRequest[sender] = Now; SendSnapshot(sender); SendReplay(sender); return;
        }
        // Membership follows authenticated relay events; payloads cannot rejoin to reset score or guesses.
        if (input.Type != "start" && input.Type != "next" && input.Type != "word" && input.Type != "guess" && input.Type != "draw") return;
        var result = _game.Handle(sender, input, Now);
        if (!result.Success)
        {
            if (sender == _client?.SessionId) Notice(Explain(result.Code));
            else Send(new WireEnvelope { Kind = "error", SessionId = _room.InstanceId, Message = Explain(result.Code) }, sender);
            return;
        }
        if (result.Operation != null)
        {
            if (result.Operation.Tool == "undo")
            {
                ReplayLocal(); SendReplay(null);
            }
            else
            {
                Apply(result.Operation);
                Send(new WireEnvelope { Kind = "operation", SessionId = _room.InstanceId, RoundId = _game.SnapshotFor(sender, Now).RoundId, Operation = result.Operation });
            }
        }
        if (input.Type == "guess" && !string.IsNullOrEmpty(result.Message))
        {
            if (sender == _client?.SessionId) Notice(result.Message);
            else Send(new WireEnvelope { Kind = "error", SessionId = _room.InstanceId, Message = result.Message }, sender);
        }
        if (input.Type != "draw") PublishState(true);
    }

    private void OnGameMessage(GameMessage message)
    {
        if (_room == null || message.Room != _room.Room || message.InstanceId != _room.InstanceId || message.Text.Length > 60000) return;
        WireEnvelope? envelope;
        try { envelope = JsonConvert.DeserializeObject<WireEnvelope>(message.Text, JsonSettings); }
        catch { return; }
        if (envelope == null || envelope.Protocol != 1 || envelope.SessionId != _room.InstanceId) return;
        if (envelope.Mode != _mode)
        {
            if (IsHost && _room.Members.Any(m => m.Id == message.FromId))
            {
                if (!_lastSyncRequest.TryGetValue(message.FromId, out double lastMismatch) || Now - lastMismatch >= 2)
                {
                    _lastSyncRequest[message.FromId] = Now;
                    SendSnapshot(message.FromId);
                }
            }
            else if (!IsHost && message.FromId == _room.HostId)
            {
                LeaveRoom(); Notice("房间模式不一致，已离开；请选择相同的绘画模式");
            }
            return;
        }
        if (IsHost)
        {
            if (envelope.Kind == "input" && envelope.Command != null) HandleInput(message.FromId, envelope.Command);
            return;
        }
        if (message.FromId != _room.HostId) return;
        _lastStateReceived = Now;
        switch (envelope.Kind)
        {
            case "snapshot":
                if (IsFree) { AdoptFreeSnapshot(envelope); break; }
                var view = envelope.Snapshot;
                if (view == null || view.SessionId != _room.InstanceId || view.HostId != _room.HostId || view.Version < _snapshotVersion) return;
                AdoptView(view);
                if (envelope.Board?.IsValid == true) PaintingSystem.SetSceneBoardPlacement(envelope.Board);
                break;
            case "operation":
                if (IsFree) { ApplyFreeMessage(envelope); break; }
                if (!_syncing && _view != null && envelope.RoundId == _view.RoundId && envelope.Operation != null) Apply(envelope.Operation);
                break;
            case "operations":
                if (!IsFree || envelope.Operations == null || envelope.Operations.Length > 64) return;
                foreach (var operation in envelope.Operations)
                {
                    envelope.Operation = operation;
                    ApplyFreeMessage(envelope);
                }
                break;
            case "syncbegin":
                if (IsFree ? _freeView == null || envelope.RoundId != 0 : _view == null || envelope.RoundId != _view.RoundId) return;
                _syncing = true; _syncId = envelope.SyncId; _syncRound = envelope.RoundId; _syncDrawingVersion = envelope.DrawingVersion;
                if (IsFree) _lastFreeSyncProgress = Now;
                _replay.Clear(); break;
            case "chunk":
                if (!_syncing || envelope.SyncId != _syncId || envelope.RoundId != _syncRound || envelope.Operations == null) return;
                if (envelope.Operations.Length > 64 || _replay.Count + envelope.Operations.Length > 1200) { _syncing = false; _replay.Clear(); return; }
                _replay.AddRange(envelope.Operations);
                if (IsFree) _lastFreeSyncProgress = Now;
                break;
            case "syncend":
                if (!_syncing || envelope.SyncId != _syncId || envelope.RoundId != _syncRound || envelope.DrawingVersion != _syncDrawingVersion) return;
                PaintingSystem.ResetCanvas(); foreach (var operation in _replay) Apply(operation);
                _replay.Clear(); _syncing = false;
                if (IsFree) _appliedSequence = _syncDrawingVersion;
                break;
            case "error": Notice(envelope.Message); break;
        }
    }

    private void AdoptView(GameSnapshot view)
    {
        if (_lastRound != view.RoundId)
        {
            _lastRound = view.RoundId; _localOp = 0; _lastStroke = 0;
            PaintingSystem.ResetCanvas(); _syncing = false; _replay.Clear();
        }
        _view = view; _snapshotVersion = view.Version; _viewReceivedAt = Now;
    }

    private void PublishState(bool force)
    {
        if (IsFree) { PublishFreeState(); return; }
        if (_game == null || _room == null || _client == null) return;
        _lastStateSent = Now;
        AdoptView(_game.SnapshotFor(_client.SessionId, Now));
        foreach (var member in _room.Members)
            if (member.Id != _client.SessionId) SendSnapshot(member.Id);
    }

    private void SendSnapshot(string viewer)
    {
        if (IsFree) { SendFreeSnapshot(viewer); return; }
        if (_game == null || _room == null) return;
        Send(new WireEnvelope { Kind = "snapshot", SessionId = _room.InstanceId, Snapshot = _game.SnapshotFor(viewer, Now), Board = PaintingSystem.GetSceneBoardPlacement() }, viewer);
    }

    private void SendReplay(string? viewer)
    {
        if (_room == null || (_game == null && _free == null)) return;
        if (IsFree)
        {
            // The canonical replay already includes every accepted delta, including those
            // not yet sent. Never send those obsolete deltas after its syncend.
            _freePendingOperations.Clear(); _freeReplayPending = false;
            _lastFreeReplay = _lastFreeFlush = Now;
        }
        long round = IsFree ? 0 : _game!.SnapshotFor(_room.HostId, Now).RoundId;
        string sync = Guid.NewGuid().ToString("N"); long version = IsFree ? _free!.DrawingVersion : _game!.DrawingVersion;
        Send(new WireEnvelope { Kind = "syncbegin", SessionId = _room.InstanceId, RoundId = round, SyncId = sync, DrawingVersion = version }, viewer);
        var operations = (IsFree ? _free!.Operations : _game!.Operations).ToArray();
        for (int i = 0; i < operations.Length; i += 64)
            Send(new WireEnvelope { Kind = "chunk", SessionId = _room.InstanceId, RoundId = round, SyncId = sync, Operations = operations.Skip(i).Take(64).ToArray() }, viewer);
        Send(new WireEnvelope { Kind = "syncend", SessionId = _room.InstanceId, RoundId = round, SyncId = sync, DrawingVersion = version }, viewer);
    }

    private void ReplayLocal()
    {
        PaintingSystem.ResetCanvas();
        if (_game != null) foreach (var operation in _game.Operations) Apply(operation);
    }
    private static void Apply(DrawingOperation op) => PaintingSystem.ApplyOperation(op.Tool, op.X1, op.Y1, op.X2, op.Y2, op.Color, op.Size, op.NewStroke);

    private void Send(WireEnvelope value, string? recipient = null)
    {
        var client = _client; var room = _room; int generation = _generation, epoch = _roomEpoch;
        if (client == null || room == null || !client.IsConnected || _disposed || !ChannelReady) return;
        value.Mode = _mode;
        string json = JsonConvert.SerializeObject(value, Formatting.None, JsonSettings);
        if (System.Text.Encoding.UTF8.GetByteCount(json) > 32768) { Notice("同步数据超过传输上限"); return; }
        if (Interlocked.Increment(ref _sendPending) > 512)
        {
            Interlocked.Decrement(ref _sendPending);
            Interlocked.Exchange(ref _networkOverflow, 1);
            Notice("网络发送积压，正在退出绘画房间"); return;
        }
        lock (_sendLock)
        {
            if (_sendTail.IsFaulted) _ = _sendTail.Exception;
            _sendTail = _sendTail.ContinueWith(async _ =>
            {
                try
                {
                    if (_disposed || generation != _generation || epoch != _roomEpoch || !ReferenceEquals(_client, client) || _room?.InstanceId != room.InstanceId) return;
                    await client.SendGameAsync(room.Room, json, recipient, _lifetime.Token).ConfigureAwait(false);
                    // Leave headroom under the relay's rolling 128-message budget, including replay bursts.
                    await Task.Delay(12, _lifetime.Token).ConfigureAwait(false);
                }
                catch (Chat.Protocol.ChatException e) when (e.Code == "recipient_not_in_game" && recipient != null)
                {
                    // Membership can change while private snapshots are queued; the relay has the final roster.
                }
                catch (OperationCanceledException) when (_disposed) { }
                catch (Exception e)
                {
                    Enqueue(generation, () =>
                    {
                        if (epoch != _roomEpoch || !ReferenceEquals(_client, client)) return;
                        Notice("发送失败，已退出绘画房间：" + e.Message); LeaveRoom();
                    });
                }
                finally { Interlocked.Decrement(ref _sendPending); }
            }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default).Unwrap();
        }
    }

    private void LeaveRoom()
    {
        if (_busy) throw new InvalidOperationException("房间请求尚未完成");
        var room = _room; var client = _client;
        if (room == null || client == null) { ClearRoom(); return; }
        if (_game != null) _game.Handle(client.SessionId, new GameCommand { Type = "close", SessionId = room.InstanceId }, Now);
        ClearRoom(); _busy = true; int generation = _generation, epoch = _roomEpoch;
        _ = Task.Run(async () =>
        {
            bool locked = false;
            try
            {
                await _channelGate.WaitAsync(_lifetime.Token).ConfigureAwait(false); locked = true;
                if (generation != Volatile.Read(ref _generation)) return;
                await client.LeaveGameAsync(room.Room, _lifetime.Token).ConfigureAwait(false);
            }
            catch (Exception e) { Enqueue(generation, () => { if (epoch == _roomEpoch) Notice("离开房间：" + e.Message); }); }
            finally
            {
                if (locked) _channelGate.Release();
                Enqueue(generation, () => { if (epoch == _roomEpoch) { _busy = false; Notice("已离开房间"); } });
            }
        });
    }

    private void ClearRoom()
    {
        ++_roomEpoch;
        _requestedRoom = null;
        ResetFreeTransport();
        _room = null; _game = null; _view = null; _free = null; _freeView = null; _snapshotVersion = -1; _lastRound = -1; _appliedSequence = 0;
        _localOp = 0; _lastStroke = 0; _syncing = false; _replay.Clear(); _lastSyncRequest.Clear(); PaintingSystem.ResetCanvas(); PaintingSystem.RemoveSceneBoard(); PaintingSystem.SetSceneBoardAutomaticPlacement(false);
        if (IsFree) { _selectedBoard = _ownBoard?.Owner ?? ""; ReplayCanvas(LocalCanvas); }
    }

    private string Describe() => IsFree ? DescribeFree() : _room == null ? (_client?.IsConnected == true ? "已连接；尚未加入房间 | " + ChannelStatus : "未连接服务器") :
        $"频道 {_room.Channel} | 房间 {_room.Room} | 房主 {_room.HostNick} | {_view?.Phase ?? "同步中"} | {_room.Members.Count} 人";

    private void UpdatePanel()
    {
        if (IsFree) { PaintingSystem.SetTopicState("", false); UpdateFreePanel(); return; }
        PaintingSystem.SetTopicState(_view?.Phase ?? "", _view?.DrawerId == _client?.SessionId && _view != null);
        var view = _view;
        bool draw = view != null && ChannelReady && !_syncing && view.Phase == "playing" && view.DrawerId == _client?.SessionId;
        string detail = _notice;
        PaintingSystem.SetState(Describe(), detail, draw);
        if (view != null)
        {
            double remaining = Math.Max(0, view.RemainingSeconds - (Now - _viewReceivedAt));
            string phase = view.Phase switch { "lobby" => "大厅", "choosing" => "画手出题（30秒内）", "playing" => "作画猜词", "reveal" => "答案揭晓", "finished" => "本局结束", _ => "房间关闭" };
            detail += $"\n{phase} | 第 {view.RoundNumber}/{view.TotalRounds} 轮 | 剩余 {Math.Ceiling(remaining)} 秒";
            if ((view.Phase == "reveal" || view.Phase == "finished") && !string.IsNullOrEmpty(view.Word)) detail += "\n答案：" + view.Word;
            else if (view.LastEvent == "word_timeout") detail += "\n画手未出题，本轮跳过";
            else if (view.Phase == "playing" && !draw) detail += $"\n提示：{view.WordLength} 个字";
            string players = string.Join("\n", view.Players.Select(p => $"{p.Name}  {p.Score} 分{(p.Id == view.DrawerId ? " · 画手" : "")}{(p.HasGuessed ? " · 已猜中" : "")}"));
            string drawer = view.Players.FirstOrDefault(p => p.Id == view.DrawerId)?.Name ?? "等待开始";
            PaintingSystem.SetRoomState(players, drawer, view.Word ?? "");
        }
        else PaintingSystem.SetRoomState("", "等待开始", "");
        PaintingSystem.SetState(Describe(), detail, draw);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; Interlocked.Increment(ref _generation);
        TipMsgCore.Tip_actions -= OnLocalMessage;
        var client = _client;
        string? room = _room?.Room ?? _requestedRoom;
        DetachClient();
        _lifetime.Cancel();
        if (client != null && room != null)
        {
            // Reserve cleanup before a replacement module can queue a new room request.
            // Unloading a module releases its room, never the host's connection.
            Task cleanupTurn = _channelGate.WaitAsync();
            _ = Task.Run(async () =>
            {
                await cleanupTurn.ConfigureAwait(false);
                try
                {
                    if (client.IsConnected)
                        await client.LeaveGameAsync(room).ConfigureAwait(false);
                }
                catch { /* A channel change or disconnect may already have removed membership. */ }
                finally { _channelGate.Release(); }
            });
        }
        while (_incoming.TryDequeue(out _)) { } _pending = 0;
        _room = null; _game = null; _view = null; _replay.Clear();
        ResetBoards(); PaintingSystem.Dispose(); _clock.Stop();
    }

    private sealed class WireEnvelope
    {
        public int Protocol { get; set; } = 1;
        public string Mode { get; set; } = "guess";
        public SharedCanvasSnapshot? Canvas { get; set; }
        public string Kind { get; set; } = "";
        public string SessionId { get; set; } = "";
        public string SyncId { get; set; } = "";
        public long RoundId { get; set; }
        public long DrawingVersion { get; set; }
        public string Message { get; set; } = "";
        public GameCommand? Command { get; set; }
        public GameSnapshot? Snapshot { get; set; }
        public SceneBoardPlacement? Board { get; set; }
        public DrawingOperation? Operation { get; set; }
        public DrawingOperation[]? Operations { get; set; }
    }
}
