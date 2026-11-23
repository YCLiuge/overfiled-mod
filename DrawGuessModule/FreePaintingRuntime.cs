using System;
using System.Collections.Generic;
using System.Linq;
using Chat.Client;
using DrawGuessModule.Core;
using mod;

namespace DrawGuessModule;

internal sealed partial class MainLogic
{
    private const string LocalArtist = "__local__";
    private string _mode = PaintingDefaults.GuessMode;
    private string _characterName = "";
    private SharedCanvas? _free, _localCanvas;
    private SharedCanvasSnapshot? _freeView;
    private long _offlineOp, _appliedSequence;
    private readonly List<DrawingOperation> _freePendingOperations = new();
    private readonly Dictionary<string, double> _lastFreeError = new();
    private bool _freeReplayPending;
    private double _lastFreeFlush, _lastFreeReplay = -0.5, _lastFreeSyncProgress;
    private bool IsFree => _mode == PaintingDefaults.FreeMode;
    private SharedCanvas LocalCanvas => _localCanvas ??= new SharedCanvas(LocalArtist, Guid.NewGuid().ToString("N"), Now);

    private string CharacterName()
    {
        string name = (TipMsgCore.i?.mainplayername ?? "").Trim();
        if (name.Length == 0) throw new InvalidOperationException("请先进入游戏，读取角色名称后再连接");
        if (name.Length > 32 || name.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)))
            throw new InvalidOperationException("角色名称须为 1–32 个非空白字符");
        return name;
    }

    private void RefreshCharacterName()
    {
        string name = (TipMsgCore.i?.mainplayername ?? "").Trim();
        if (name == _characterName) return;
        _characterName = name;
        PaintingSystem.SetPlayerName(name);
    }

    private void SwitchMode(string mode)
    {
        mode = mode.ToLowerInvariant();
        if (mode != PaintingDefaults.GuessMode && mode != PaintingDefaults.FreeMode)
            throw new ArgumentException("模式为 guess（你画我猜）或 free（自由绘画同步）");
        if (_busy) throw new InvalidOperationException("连接或房间请求正在处理中，请稍候切换模式");
        if (mode == _mode) { PaintingSystem.Show(); return; }
        // Leave with the old mode before changing the renderer's source. Queued sends are
        // guarded by the old room instance; personal free artwork never enters a game room.
        LeaveRoom();
        _mode = mode;
        PaintingSystem.SetMode(mode);
        _localOp = 0; _lastStroke = 0; _view = null; _freeView = null;
        PaintingSystem.ResetCanvas();
        if (IsFree) { _selectedBoard = _ownBoard?.Owner ?? ""; ReplayCanvas(LocalCanvas); }
        UpdatePanel();
        PaintingSystem.Show();
        Notice(IsFree ? "自由绘画：可离线绘画、保存 PNG；连接后创建自己的频道画架，别人只能观看" : "你画我猜：创建或加入游戏房间，由房主开始回合");
    }

    private static void ReplayCanvas(SharedCanvas canvas)
    {
        PaintingSystem.ResetCanvas();
        foreach (var operation in canvas.Operations) Apply(operation);
    }

    private void SubmitOffline(GameCommand input)
    {
        if (_busy) { Notice("房间操作尚未完成，请稍候绘画"); return; }
        if (input.Type == "sync") { ReplayCanvas(LocalCanvas); return; }
        if (input.Operation != null) input.Operation.OpId = ++_offlineOp;
        input.SessionId = LocalCanvas.SessionId;
        var result = LocalCanvas.Handle(LocalArtist, input, Now);
        if (!result.Success) { Notice(Explain(result.Code)); return; }
        if (result.Operation == null) return;
        if (result.Operation.Tool == "undo") ReplayCanvas(LocalCanvas);
        else Apply(result.Operation);
    }

    private void OnFreeRoomChanged(GameRoomEvent change)
    {
        if (_free == null) return;
        if (change.Type == "join")
        {
            var member = change.Room.Members.FirstOrDefault(m => m.Id == change.MemberId);
            if (member != null) _free.AddPlayer(member.Id, member.Nick, Now);
            PublishFreeState();
            RequestFreeReplay();
        }
        else if (change.Type == "leave")
        {
            _free.RemovePlayer(change.MemberId, Now);
            _lastFreeError.Remove(change.MemberId); _lastSyncRequest.Remove(change.MemberId);
            PublishFreeState();
        }
    }

    private void HandleFreeInput(string sender, GameCommand input)
    {
        if (_free == null || _room == null || input.SessionId != _room.InstanceId || !_room.Members.Any(m => m.Id == sender)) return;
        if (input.Type == "sync")
        {
            if (_lastSyncRequest.TryGetValue(sender, out double last) && Now - last < 2) return;
            _lastSyncRequest[sender] = Now; SendFreeSnapshot(sender); RequestFreeReplay(); return;
        }
        var result = _free.Handle(sender, input, Now);
        if (!result.Success)
        {
            if (sender == _client?.SessionId) Notice(Explain(result.Code));
            else if (System.Threading.Volatile.Read(ref _sendPending) < 64 && (!_lastFreeError.TryGetValue(sender, out double lastError) || Now - lastError >= 1))
            {
                _lastFreeError[sender] = Now;
                Send(new WireEnvelope { Kind = "error", SessionId = _room.InstanceId, Message = Explain(result.Code) }, sender);
            }
            return;
        }
        if (result.Operation == null) return;
        if (result.Operation.Tool == "undo") { ReplayCanvas(_free); RequestFreeReplay(); }
        else
        {
            Apply(result.Operation);
            if (!_freeReplayPending)
            {
                _freePendingOperations.Add(result.Operation);
                // A single very busy Update cannot grow an unbounded staging list.
                if (_freePendingOperations.Count >= 64) FlushFreeOperations();
            }
        }
        // Heartbeats carry roster/version at most once per second; accepted operations
        // already carry their shared order and must not fan out a second snapshot per segment.
        _freeView = _free.Snapshot();
    }

    private void PublishFreeState()
    {
        if (_free == null || _room == null || _client == null) return;
        _lastStateSent = Now;
        _freeView = _free.Snapshot();
        SendFreeSnapshot(null);
    }

    private void SendFreeSnapshot(string? viewer)
    {
        if (_free == null || _room == null) return;
        FlushFreeOperations();
        if (System.Threading.Volatile.Read(ref _sendPending) >= 64) return;
        Send(new WireEnvelope { Kind = "snapshot", SessionId = _room.InstanceId, Canvas = _free.Snapshot(), Board = PaintingSystem.GetSceneBoardPlacement() }, viewer);
    }

    private void ResetFreeTransport()
    {
        _freePendingOperations.Clear(); _lastFreeError.Clear();
        _freeReplayPending = false; _lastFreeFlush = Now; _lastFreeReplay = Now - 0.5;
        _lastFreeSyncProgress = Now;
    }

    private void RequestFreeReplay()
    {
        // All free-room members share one public canvas. Multiple undos or recovery
        // requests within 500 ms are covered by one current canonical broadcast.
        _freePendingOperations.Clear(); _freeReplayPending = true;
    }

    private void FlushFreeOperations()
    {
        if (_freeReplayPending || _freePendingOperations.Count == 0 || _room == null) return;
        if (System.Threading.Volatile.Read(ref _sendPending) >= 64)
        {
            RequestFreeReplay();
            return;
        }
        var operations = _freePendingOperations.ToArray(); _freePendingOperations.Clear();
        _lastFreeFlush = Now;
        Send(new WireEnvelope { Kind = "operations", SessionId = _room.InstanceId, RoundId = 0, Operations = operations });
    }

    private void FlushFreeTransport()
    {
        if (_freeReplayPending)
        {
            if (Now - _lastFreeReplay >= 0.5 && System.Threading.Volatile.Read(ref _sendPending) < 64) SendReplay(null);
        }
        else if (Now - _lastFreeFlush >= 0.125) FlushFreeOperations();
    }

    private void AdoptFreeSnapshot(WireEnvelope envelope)
    {
        if (_room == null) return;
        var view = envelope.Canvas;
        if (view == null || view.Mode != PaintingDefaults.FreeMode || view.HostId != _room.HostId || view.SessionId != _room.InstanceId || view.Version < _snapshotVersion) return;
        _freeView = view; _snapshotVersion = view.Version;
        if (envelope.Board?.IsValid == true) PaintingSystem.SetSceneBoardPlacement(envelope.Board);
    }

    private void ApplyFreeMessage(WireEnvelope envelope)
    {
        if (_syncing || _freeView == null || envelope.RoundId != 0 || envelope.Operation == null) return;
        var op = envelope.Operation;
        if (op.Sequence <= _appliedSequence) return;
        if (op.Sequence != _appliedSequence + 1)
        {
            _lastFreeSyncProgress = Now;
            Submit(new GameCommand { Type = "sync" });
            _syncing = true;
            return;
        }
        Apply(op);
        _appliedSequence = op.Sequence;
    }

    private string DescribeFree() => _room == null ? "自由绘画 · 本地画布（可保存 PNG）" + (_client?.IsConnected == true ? " | " + ChannelStatus : "") :
        $"频道 {_room.Channel} | 自由画室 {_room.Room.Substring(5)} | 房主 {_room.HostNick} | {_room.Members.Count} 人 | {(_syncing ? "同步中" : "共同作画")}";

    private void UpdateFreePanel()
    {
        bool canDraw = !_busy && !_syncing && _room == null && (_selectedBoard.Length == 0 || _selectedBoard == _client?.SessionId);
        string players = _room == null ? _characterName + " · 本地绘画" :
            string.Join("\n", _room.Members.Select(m => m.Nick + (m.Id == _room.HostId ? " · 房主" : "")));
        PaintingSystem.SetState(DescribeFree(), _notice, canDraw);
        PaintingSystem.SetRoomState(players, "所有成员", "");
    }
}
