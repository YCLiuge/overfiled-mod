using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Chat.Protocol;

namespace Chat.Client
{
    public sealed partial class ChatClient
    {
        private readonly ConcurrentDictionary<string, GameRoomInfo> _gameRooms =
            new ConcurrentDictionary<string, GameRoomInfo>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Server-issued connection identity, empty on legacy servers. Not account authentication.</summary>
        public string SessionId { get; private set; } = string.Empty;
        /// <summary>Raised on the background receive thread; enqueue before touching Unity.</summary>
        public event Action<GameMessage>? GameMessageReceived;
        /// <summary>Raised on the background receive thread. Host departure closes the room, without migration.</summary>
        public event Action<GameRoomEvent>? GameRoomChanged;

        public Task<GameRoomInfo> CreateGameAsync(string room, CancellationToken cancellationToken = default(CancellationToken))
        { return EnterGameAsync(Wire.CmdGameCreate, room, cancellationToken); }

        public Task<GameRoomInfo> JoinGameAsync(string room, CancellationToken cancellationToken = default(CancellationToken))
        { return EnterGameAsync(Wire.CmdGameJoin, room, cancellationToken); }

        private async Task<GameRoomInfo> EnterGameAsync(string command, string room, CancellationToken token)
        {
            JsonValue reply = await RequestAsync(command, new List<(string, JsonValue)> { ("room", room) }, token).ConfigureAwait(false);
            JsonValue value;
            if (!reply.TryGetProperty("room", out value) || value.Kind != JsonKind.Object)
                throw new InvalidOperationException("Server did not return game room metadata");
            return ParseGameRoom(value);
        }

        public async Task LeaveGameAsync(string room, CancellationToken cancellationToken = default(CancellationToken))
        {
            GameRoomInfo info = RequireGame(room);
            await RequestAsync(Wire.CmdGameLeave, new List<(string, JsonValue)> {
                ("room", info.Room), ("instanceId", info.InstanceId) }, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>Participant sends privately to host; host broadcasts unless toId selects a current member.
        /// Delivered means queued at relay, not applied at destination. Max game text: 32768 UTF-8 bytes.</summary>
        public async Task<int> SendGameAsync(string room, string text, string? toId = null,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            GameRoomInfo info = RequireGame(room);
            JsonValue reply = await RequestAsync(Wire.CmdGameSend, new List<(string, JsonValue)> {
                ("room", info.Room), ("instanceId", info.InstanceId), ("text", text), ("toId", JsonValue.Of(toId!)) },
                cancellationToken).ConfigureAwait(false);
            return ToInt32(reply.GetInt64("delivered"));
        }

        public Task<int> SendPrivateAsync(string room, string toId, string text,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            if (string.IsNullOrEmpty(toId)) throw new ArgumentException("Recipient identity required", nameof(toId));
            return SendGameAsync(room, text, toId, cancellationToken);
        }

        private GameRoomInfo RequireGame(string room)
        {
            GameRoomInfo info;
            string name = (room ?? string.Empty).Trim().TrimStart('#');
            if (!_gameRooms.TryGetValue(name, out info!)) throw new InvalidOperationException("Join or create the game first");
            return info;
        }

        private void TrackGameReply(JsonValue reply)
        {
            JsonValue value;
            if (reply.TryGetProperty("room", out value) && value.Kind == JsonKind.Object)
            {
                GameRoomInfo room = ParseGameRoom(value);
                _gameRooms[room.Room] = room;
            }
            string? left = reply.GetString("left");
            if (left != null) { GameRoomInfo removed; _gameRooms.TryRemove(left, out removed!); }
        }

        private void HandleGameLine(string command, JsonValue payload)
        {
            if (command == Wire.CmdGameMessage)
            {
                string room = payload.GetString("room") ?? string.Empty;
                string incarnation = payload.GetString("instanceId") ?? string.Empty;
                GameRoomInfo info;
                if (!_gameRooms.TryGetValue(room, out info!) || info.InstanceId != incarnation) return;
                Raise(GameMessageReceived, new GameMessage(room, incarnation,
                    payload.GetString("fromId") ?? string.Empty, payload.GetString("from") ?? string.Empty,
                    payload.GetString("toId"), payload.GetString("text") ?? string.Empty, ParseTimestamp(payload)));
                return;
            }
            JsonValue roomValue;
            if (!payload.TryGetProperty("room", out roomValue) || roomValue.Kind != JsonKind.Object) return;
            GameRoomInfo snapshot = ParseGameRoom(roomValue);
            GameRoomInfo current;
            if (!_gameRooms.TryGetValue(snapshot.Room, out current!) || current.InstanceId != snapshot.InstanceId) return;
            string type = payload.GetString("type") ?? string.Empty;
            if (type == "closed") { GameRoomInfo removed; _gameRooms.TryRemove(snapshot.Room, out removed!); }
            else _gameRooms[snapshot.Room] = snapshot;
            Raise(GameRoomChanged, new GameRoomEvent(type, snapshot,
                payload.GetString("memberId") ?? string.Empty, payload.GetString("reason") ?? string.Empty));
        }

        private static GameRoomInfo ParseGameRoom(JsonValue value)
        {
            List<GameMember> members = new List<GameMember>();
            JsonValue list;
            if (value.TryGetProperty("members", out list))
                foreach (JsonValue member in list.Items)
                    members.Add(new GameMember(member.GetString("id") ?? string.Empty, member.GetString("nick") ?? string.Empty));
            return new GameRoomInfo(value.GetString("room") ?? string.Empty, value.GetString("instanceId") ?? string.Empty,
                value.GetString("hostId") ?? string.Empty, value.GetString("hostNick") ?? string.Empty, members.AsReadOnly(),
                value.GetString("channel") ?? string.Empty);
        }
    }
}
