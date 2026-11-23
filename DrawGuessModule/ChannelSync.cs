using System;
using System.Threading;
using Chat.Client;
using mod.st;

namespace DrawGuessModule;

internal sealed partial class MainLogic
{
    // The host owns the socket and its channel. Modules only borrow it.
    private readonly NetChater _network = NetChater.Instance;
    private SemaphoreSlim _channelGate => _network.ChannelGate;
    private string _desiredChannel = "", _joinedChannel = "";
    private int _channelRevision, _hostChannelRevision = -1;
    private Action<GameMessage>? _messageHandler;
    private Action<GameRoomEvent>? _roomHandler;
    private Action<string>? _disconnectHandler;
    private string? _requestedRoom;

    private bool ChannelReady => ReferenceEquals(_client, _network.Client) &&
        _network.ChannelReady && _client?.IsConnected == true &&
        _desiredChannel == _network.DesiredChannel;
    private string ChannelStatus => _desiredChannel.Length == 0 ? "等待游戏频道" :
        ChannelReady ? "频道 " + _desiredChannel : "频道同步中";

    private void DetachClient()
    {
        if (_client == null) return;
        _client.BoardMessageReceived -= _boardHandler;
        _client.GameMessageReceived -= _messageHandler;
        _client.GameRoomChanged -= _roomHandler;
        _client.Disconnected -= _disconnectHandler;
        _client = null;
    }

    private void FollowGameChannel()
    {
        var shared = _network.Client;
        bool connectionChanged = !ReferenceEquals(shared, _client);
        bool channelChanged = _hostChannelRevision != _network.ChannelRevision;
        if (!connectionChanged && !channelChanged) return;
        if (connectionChanged)
        {
            DetachClient();
            int generation = ++_generation;
            _client = shared;
            if (shared != null)
            {
                _messageHandler = message =>
                {
                    int epoch = Volatile.Read(ref _roomEpoch);
                    Enqueue(generation, () => { if (epoch == _roomEpoch) OnGameMessage(message); });
                };
                _roomHandler = message =>
                {
                    int epoch = Volatile.Read(ref _roomEpoch);
                    Enqueue(generation, () => { if (epoch == _roomEpoch) OnRoomChanged(message); });
                };
                _disconnectHandler = reason => Enqueue(generation, () =>
                { _busy = false; ResetBoards(); ClearRoom(); Notice("宿主网络已断开：" + reason); });
                AttachBoards(shared, generation);
                shared.GameMessageReceived += _messageHandler;
                shared.GameRoomChanged += _roomHandler;
                shared.Disconnected += _disconnectHandler;
            }
        }
        _hostChannelRevision = _network.ChannelRevision;
        _desiredChannel = _network.DesiredChannel;
        _joinedChannel = _network.ChannelReady ? _desiredChannel : "";
        Interlocked.Increment(ref _channelRevision);
        ResetBoards();
        bool hadRoom = _room != null || _requestedRoom != null;
        ClearRoom();
        _busy = false;
        if (hadRoom) Notice("宿主网络或游戏频道已变化，已退出旧绘画房间；请重新创建或加入");
    }
}
