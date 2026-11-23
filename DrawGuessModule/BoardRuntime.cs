using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Chat.Client;
using DrawGuessModule.Core;
using Newtonsoft.Json;

namespace DrawGuessModule;

internal sealed partial class MainLogic
{
    private sealed class ChannelBoard
    {
        public string Owner = "", Name = "", Room = "", RoomInstance = "", Transfer = "";
        public SceneBoardPlacement Placement = new();
        public long Revision, ReceivingRevision, LatestRevision;
        public double Seen;
        public int NextChunk, ChunkCount;
        public List<DrawingOperation> Operations = new(), Receiving = new();
    }
    private sealed class BoardPacket
    {
        public string Kind { get; set; } = "info";
        public string Name { get; set; } = "";
        public string Room { get; set; } = "";
        public string RoomInstance { get; set; } = "";
        public SceneBoardPlacement? Placement { get; set; }
        public long Revision { get; set; }
        public string Transfer { get; set; } = "";
        public int Index { get; set; }
        public int Count { get; set; }
        public List<DrawingOperation> Operations { get; set; } = new();
    }
    private readonly Dictionary<string, ChannelBoard> _boards = new(StringComparer.Ordinal);
    private readonly Queue<BoardPacket> _boardOutgoing = new();
    private Action<BoardChannelMessage>? _boardHandler;
    private ChannelBoard? _ownBoard;
    private string _selectedBoard = "", _boardSource = "";
    private long _boardDrawingVersion = -1, _boardRevision;
    private double _lastBoardInfo = -10, _lastBoardSnapshot = -10, _lastBoardSend, _lastBoardRequest = -10;
    private bool _boardDirty;
    private int _boardSends;

    private void AttachBoards(ChatClient client, int generation)
    {
        _boardHandler = message =>
        {
            int channelRevision = Volatile.Read(ref _channelRevision);
            Enqueue(generation, () => { if (channelRevision == _channelRevision) ReceiveBoard(message); });
        };
        client.BoardMessageReceived += _boardHandler;
    }
    private void ResetBoards()
    {
        _boards.Clear(); _ownBoard = null; _selectedBoard = ""; _boardSource = "";
        _boardDrawingVersion = -1; _boardOutgoing.Clear(); _boardDirty = false;
        _lastBoardInfo = _lastBoardRequest = _lastBoardSnapshot = -10;
        PaintingSystem.ClearChannelBoards();
    }
    private void CreateOwnBoard()
    {
        if (!ChannelReady || _client == null) throw new InvalidOperationException("请连接宿主网络并等待游戏频道同步后创建画架");
        PaintingSystem.PlaceSceneBoard();
        var placement = PaintingSystem.GetSceneBoardPlacement();
        if (placement == null) throw new InvalidOperationException("当前玩家位置尚未就绪");
        _ownBoard ??= new ChannelBoard { Owner = _client.SessionId, Name = CharacterName() };
        _ownBoard.Placement = placement.Copy(); _ownBoard.Seen = Now;
        _boards[_ownBoard.Owner] = _ownBoard;
        RenderBoard(_ownBoard, new List<DrawingOperation>(), false);
        _boardDirty = true; _lastBoardInfo = -10;
        if (_room == null) { _selectedBoard = _ownBoard.Owner; SwitchMode(PaintingDefaults.FreeMode); ReplayCanvas(LocalCanvas); }
        TickBoards();
        Notice("已创建 / 移动自己的画架，同频道玩家可见；仅创建者可画。切换你画我猜后创建房间可将它设为游戏画架。");
    }
    private void SelectBoard(string owner, string? expectedInstance = null)
    {
        if (!_boards.TryGetValue(owner, out var board) || !ChannelReady) throw new InvalidOperationException("画架已离开当前频道，请等待同步");
        if (expectedInstance != null && expectedInstance != board.RoomInstance) throw new InvalidOperationException("画架绑定的房间已变化，请重新按住 Alt 点击");
        if (board.RoomInstance.Length > 0)
        {
            if (_room?.InstanceId == board.RoomInstance) { PaintingSystem.Show(); return; }
            if (_room != null) throw new InvalidOperationException("请先退出当前游戏房间");
            if (IsFree) SwitchMode(PaintingDefaults.GuessMode);
            EnterRoom(false, board.Room, null, board.Owner, board.RoomInstance); return;
        }
        if (_room != null) throw new InvalidOperationException("请先退出当前游戏房间再查看普通画架");
        if (!IsFree) SwitchMode(PaintingDefaults.FreeMode);
        _selectedBoard = owner;
        PaintingSystem.ResetCanvas();
        if (owner == _client?.SessionId) ReplayCanvas(LocalCanvas);
        else foreach (var operation in board.Operations) Apply(operation);
        UpdatePanel(); PaintingSystem.Show();
        Notice(owner == _client?.SessionId ? "我的画架：仅自己可画" : board.Name + "的画架：只读观看");
    }
    private void TickBoards()
    {
        if (!ChannelReady || _client == null) return;
        foreach (var stale in _boards.Values.Where(b => b != _ownBoard && Now - b.Seen > 20).ToArray())
        { RemoveRemoteBoard(stale.Owner); }
        if (Now - _lastBoardRequest >= 8)
        { _lastBoardRequest = Now; QueueBoard(new BoardPacket { Kind = "request" }); }
        if (_ownBoard != null)
        {
            var b = _ownBoard;
            string room = IsHost && _game != null ? _room!.Room : "";
            string instance = IsHost && _game != null ? _room!.InstanceId : "";
            string source = _game != null && IsHost ? instance + "/" + _view?.RoundId : LocalCanvas.SessionId;
            long version = _game != null && IsHost ? _game.DrawingVersion : LocalCanvas.DrawingVersion;
            if (b.Room != room || b.RoomInstance != instance) { b.Room = room; b.RoomInstance = instance; _lastBoardInfo = -10; _boardDirty = true; }
            if (source != _boardSource || version != _boardDrawingVersion)
            {
                _boardSource = source; _boardDrawingVersion = version;
                b.Operations = (_game != null && IsHost ? _game.Operations : LocalCanvas.Operations).Select(o => o.Copy()).ToList();
                b.Revision = ++_boardRevision; _boardDirty = true;
                RenderBoard(b, b.Operations, true);
            }
            if (Now - _lastBoardInfo >= 2)
            { _lastBoardInfo = Now; QueueBoard(PacketFor(b, "info")); }
            if (_boardDirty && _boardOutgoing.Count == 0 && Now - _lastBoardSnapshot >= 0.3)
            {
                _boardDirty = false; _lastBoardSnapshot = Now;
                string transfer = Guid.NewGuid().ToString("N"); int count = Math.Max(1, (b.Operations.Count + 47) / 48);
                for (int i = 0; i < count; i++)
                {
                    var p = PacketFor(b, "snapshot"); p.Transfer = transfer; p.Index = i; p.Count = count;
                    p.Operations = b.Operations.Skip(i * 48).Take(48).Select(o => o.Copy()).ToList();
                    QueueBoard(p);
                }
            }
        }
        if (_boardOutgoing.Count > 0 && Volatile.Read(ref _boardSends) < 4 && Now - _lastBoardSend >= 0.1)
        {
            _lastBoardSend = Now; var packet = _boardOutgoing.Dequeue();
            var client = _client; var channel = _desiredChannel; int revision = _channelRevision, generation = _generation;
            string json = JsonConvert.SerializeObject(packet, JsonSettings);
            Interlocked.Increment(ref _boardSends);
            lock (_sendLock) _sendTail = _sendTail.ContinueWith(async _ =>
            {
                try
                {
                    if (_disposed || generation != _generation || revision != _channelRevision || !ReferenceEquals(client, _network.Client) || !ChannelReady) return;
                    await client.BroadcastBoardAsync(channel, json, _lifetime.Token).ConfigureAwait(false);
                }
                catch (Exception e) { Enqueue(generation, () => { _boardDirty = true; Notice("频道画架同步失败，请确认中继已更新：" + e.Message); }); }
                finally { Interlocked.Decrement(ref _boardSends); }
            }, CancellationToken.None, System.Threading.Tasks.TaskContinuationOptions.None, System.Threading.Tasks.TaskScheduler.Default).Unwrap();
        }
    }
    private void QueueBoard(BoardPacket packet) { if (_boardOutgoing.Count < 64) _boardOutgoing.Enqueue(packet); else _boardDirty = true; }
    private static BoardPacket PacketFor(ChannelBoard b, string kind) => new() { Kind = kind, Name = b.Name, Room = b.Room, RoomInstance = b.RoomInstance, Placement = b.Placement.Copy(), Revision = b.Revision };
    private static bool ValidBoardOperation(DrawingOperation o) => o != null &&
        (o.Tool == "line" || o.Tool == "brush" || o.Tool == "fill" || o.Tool == "clear") && o.X1 >= 0 && o.X1 <= 511 && o.Y1 >= 0 && o.Y1 <= 511 &&
        o.X2 >= 0 && o.X2 <= 511 && o.Y2 >= 0 && o.Y2 <= 511 && o.Size >= 1 && o.Size <= 32 && AuthoritativeGame.Palette.Contains(o.Color);
    private void RenderBoard(ChannelBoard b, List<DrawingOperation> operations, bool reset) =>
        PaintingSystem.SetChannelBoard(b.Owner, b.Placement, b.Name + (b.RoomInstance.Length > 0 ? " · 游戏画架：" + b.Room + "\nAlt＋点击加入" : " · 仅创建者可画"), operations, reset);
    private void RemoveRemoteBoard(string owner)
    {
        _boards.Remove(owner); PaintingSystem.RemoveChannelBoard(owner);
        if (_selectedBoard != owner) return;
        _selectedBoard = _ownBoard?.Owner ?? "";
        if (IsFree && _room == null) ReplayCanvas(LocalCanvas);
    }
    private void ReceiveBoard(BoardChannelMessage message)
    {
        if (!ChannelReady || message.Channel != _desiredChannel || message.AuthenticatedId == _client?.SessionId || string.IsNullOrEmpty(message.AuthenticatedId) || message.Text.Length > 24000) return;
        BoardPacket? p;
        try { p = JsonConvert.DeserializeObject<BoardPacket>(message.Text, JsonSettings); } catch { return; }
        if (p == null) return;
        if (p.Kind == "request") { _boardDirty = true; _lastBoardInfo = -10; return; }
        if (p.Kind == "remove") { RemoveRemoteBoard(message.AuthenticatedId); return; }
        if (p.Kind != "info" && p.Kind != "snapshot") return;
        if (p.Placement == null || !p.Placement.IsValid || p.Revision < 0 || p.Name == null || p.Name.Length > 32 || p.Room == null || p.Room.Length > 64 || p.RoomInstance == null || p.RoomInstance.Length > 128) return;
        if (!_boards.TryGetValue(message.AuthenticatedId, out var b))
        {
            if (_boards.Count >= 64) return;
            b = new ChannelBoard { Owner = message.AuthenticatedId, Name = message.Name }; _boards.Add(b.Owner, b);
        }
        // Info and older transfer fragments may arrive after a newer drawing/binding.
        if (p.Revision < b.LatestRevision) return;
        b.LatestRevision = p.Revision;
        b.Seen = Now; b.Name = message.Name; b.Placement = p.Placement.Copy(); b.Room = p.Room; b.RoomInstance = p.RoomInstance;
        RenderBoard(b, new List<DrawingOperation>(), false);
        if (p.Kind != "snapshot" || p.Revision < b.Revision || p.Count < 1 || p.Count > 21 || p.Index < 0 || p.Index >= p.Count || p.Transfer == null || p.Transfer.Length > 64 || p.Operations == null || p.Operations.Count > 48 || p.Operations.Any(o => !ValidBoardOperation(o))) return;
        if (p.Index == 0)
        {
            if (p.Revision < b.ReceivingRevision) return;
            b.Transfer = p.Transfer; b.NextChunk = 0; b.ChunkCount = p.Count; b.ReceivingRevision = p.Revision; b.Receiving.Clear();
        }
        if (p.Transfer != b.Transfer || p.Revision != b.ReceivingRevision || p.Index != b.NextChunk || p.Count != b.ChunkCount) return;
        if (b.Receiving.Count + p.Operations.Count > 1000) return;
        b.Receiving.AddRange(p.Operations); b.NextChunk++;
        if (b.NextChunk != b.ChunkCount) return;
        b.Revision = p.Revision; b.Operations = b.Receiving.Select(o => o.Copy()).ToList(); b.Receiving.Clear();
        RenderBoard(b, b.Operations, true);
        if (IsFree && _room == null && _selectedBoard == b.Owner)
        { PaintingSystem.ResetCanvas(); foreach (var op in b.Operations) Apply(op); }
    }
}
