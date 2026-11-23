using System.Text;
using Chat.Protocol;

namespace Chat.Server;

/// <summary>Connection-bound room routing only. Game rules and secrets belong to the game host.</summary>
internal sealed class GameRelay(ServerOptions options)
{
    private sealed class Room(string channel, string name, ClientSession host)
    {
        public string Channel { get; } = channel;
        public string Name { get; } = name;
        public string InstanceId { get; } = Guid.NewGuid().ToString("N");
        public ClientSession Host { get; } = host;
        public Dictionary<string, ClientSession> Members { get; } = new(StringComparer.Ordinal) { [host.SessionId] = host };
    }

    private sealed class RoomKeyComparer : IEqualityComparer<(string Channel, string Name)>
    {
        public bool Equals((string Channel, string Name) x, (string Channel, string Name) y) =>
            StringComparer.OrdinalIgnoreCase.Equals(x.Channel, y.Channel) &&
            StringComparer.OrdinalIgnoreCase.Equals(x.Name, y.Name);
        public int GetHashCode((string Channel, string Name) key) => HashCode.Combine(
            StringComparer.OrdinalIgnoreCase.GetHashCode(key.Channel), StringComparer.OrdinalIgnoreCase.GetHashCode(key.Name));
    }

    private readonly object _gate = new();
    private readonly Dictionary<(string Channel, string Name), Room> _rooms = new(new RoomKeyComparer());
    private readonly Dictionary<string, Room> _membership = new(StringComparer.Ordinal);
    internal const int MaxMembers = 16;
    internal const int MaxTextBytes = 32768;

    public bool Handle(ClientSession sender, string command, JsonValue payload)
    {
        lock (_gate)
        {
            if (sender.Nick is null) return Error(sender, payload, "not_handshaken", "HELLO required");
            if (!Validation.TryChannel(payload.GetString("room"), out string name, out string error))
                return Error(sender, payload, "bad_room", error);
            // Cleanup uses connection-bound membership, even after the channel set changes.
            // Never look up an arbitrary room for LEAVE, which could expose another channel's room.
            if (command == Wire.CmdGameLeave)
            {
                if (!_membership.TryGetValue(sender.SessionId, out Room? joined) ||
                    !string.Equals(joined.Name, name, StringComparison.OrdinalIgnoreCase))
                    return Error(sender, payload, "not_in_game", "Join the game first");
                if (!string.Equals(payload.GetString("instanceId"), joined.InstanceId, StringComparison.Ordinal))
                    return Error(sender, payload, "stale_game", "Game incarnation changed; join again");
                sender.ReplyOk(payload, ("left", joined.Name));
                Remove(joined, sender, "left");
                return true;
            }
            string[] channels = sender.Channels.Keys.ToArray();
            if (channels.Length != 1)
                return Error(sender, payload, "single_channel_required", "Game commands require exactly one joined chat channel");
            string channel = channels[0];
            var key = (channel, name);
            if (command == Wire.CmdGameCreate)
            {
                if (_membership.ContainsKey(sender.SessionId)) return Error(sender, payload, "already_in_game", "Leave the current game first");
                if (_rooms.ContainsKey(key)) return Error(sender, payload, "room_exists", "Room already has a host");
                Room created = new(channel, name, sender);
                if (!FitsSnapshot(created)) return Error(sender, payload, "room_too_large", "Room metadata exceeds line limit");
                _rooms.Add(key, created);
                _membership.Add(sender.SessionId, created);
                sender.ReplyOk(payload, ("room", Snapshot(created)));
                return true;
            }
            if (!_rooms.TryGetValue(key, out Room? room)) return Error(sender, payload, "game_not_found", "Game room does not exist");
            if (command == Wire.CmdGameJoin)
            {
                if (_membership.TryGetValue(sender.SessionId, out Room? current))
                {
                    if (!ReferenceEquals(room, current)) return Error(sender, payload, "already_in_game", "Leave the current game first");
                    sender.ReplyOk(payload, ("room", Snapshot(room)));
                    return true;
                }
                if (room.Members.Count >= MaxMembers) return Error(sender, payload, "game_full", "Room member limit reached");
                room.Members.Add(sender.SessionId, sender);
                if (!FitsSnapshot(room))
                {
                    room.Members.Remove(sender.SessionId);
                    return Error(sender, payload, "room_too_large", "Room metadata exceeds line limit");
                }
                _membership.Add(sender.SessionId, room);
                sender.ReplyOk(payload, ("room", Snapshot(room)));
                Notify(room, "join", sender.SessionId, "", sender.SessionId);
                return true;
            }
            if (!room.Members.ContainsKey(sender.SessionId)) return Error(sender, payload, "not_in_game", "Join the game first");
            if (!string.Equals(payload.GetString("instanceId"), room.InstanceId, StringComparison.Ordinal))
                return Error(sender, payload, "stale_game", "Game incarnation changed; join again");
            if (!Validation.TryText(payload.GetString("text"), options.MaxTextChars, out string text, out error))
                return Error(sender, payload, "bad_text", error);
            int textBytes = Encoding.UTF8.GetByteCount(text);
            if (textBytes > MaxTextBytes)
                return Error(sender, payload, "game_msg_too_large", $"Game text exceeds {MaxTextBytes} UTF-8 bytes; batch strokes/snapshots");
            if (!sender.TryGameBudget(textBytes))
                return Error(sender, payload, "game_rate_limited", "Game transport budget: 128 messages and 1 MiB per second per connection");
            string? to = payload.GetString("toId");
            bool isHost = ReferenceEquals(sender, room.Host);
            if (!isHost)
            {
                if (!string.IsNullOrEmpty(to) && to != room.Host.SessionId)
                    return Error(sender, payload, "host_only_route", "Participants may send only to the host");
                to = room.Host.SessionId;
            }
            ClientSession[] recipients;
            if (string.IsNullOrEmpty(to)) recipients = room.Members.Values.Where(m => !ReferenceEquals(m, sender)).ToArray();
            else
            {
                if (!room.Members.TryGetValue(to, out ClientSession? target)) return Error(sender, payload, "recipient_not_in_game", "Recipient is not a member");
                if (ReferenceEquals(target, sender)) return Error(sender, payload, "self_route", "Self delivery is not supported");
                recipients = [target];
            }
            string line = Wire.Serialize(Wire.CmdGameMessage, JsonValue.Object(
                ("room", room.Name), ("instanceId", room.InstanceId),
                ("fromId", sender.SessionId), ("from", sender.Nick),
                ("toId", to), ("text", text), ("ts", Wire.TimestampNow())));
            if (TransferBudget.LineBytes(line) > options.MaxLineBytes) return Error(sender, payload, "msg_too_large", "Forwarded message exceeds line limit");
            int delivered = recipients.Count(target => target.Channels.ContainsKey(room.Channel) && target.Send(line));
            sender.ReplyOk(payload, ("delivered", delivered));
            return true;
        }
    }

    // Called before the chat channel is removed, without holding any chat/hub lock.
    // Send only queues with TryWrite; neither this gate nor Handle waits for network I/O.
    public void LeaveChannel(ClientSession session, string channel)
    {
        lock (_gate)
        {
            if (_membership.TryGetValue(session.SessionId, out Room? room) &&
                string.Equals(room.Channel, channel, StringComparison.OrdinalIgnoreCase))
                Remove(room, session, "channel_left");
        }
    }

    public void Disconnect(ClientSession session, string reason)
    {
        lock (_gate)
        {
            if (_membership.TryGetValue(session.SessionId, out Room? room)) Remove(room, session, reason);
        }
    }

    private void Remove(Room room, ClientSession member, string reason)
    {
        if (ReferenceEquals(member, room.Host))
        {
            // Keep the closing roster in the event; no successor may silently inherit authority.
            Notify(room, "closed", member.SessionId, reason, member.SessionId);
            foreach (string id in room.Members.Keys) _membership.Remove(id);
            _rooms.Remove((room.Channel, room.Name));
        }
        else
        {
            room.Members.Remove(member.SessionId);
            _membership.Remove(member.SessionId);
            Notify(room, "leave", member.SessionId, reason, member.SessionId);
        }
    }

    private static JsonValue Snapshot(Room room) => JsonValue.Object(
        ("room", room.Name), ("channel", room.Channel), ("instanceId", room.InstanceId), ("hostId", room.Host.SessionId), ("hostNick", room.Host.Nick),
        ("members", JsonValue.Array(room.Members.Values.Select(m => JsonValue.Object(("id", m.SessionId), ("nick", m.Nick))).ToArray())));

    private bool FitsSnapshot(Room room)
    {
        string line = Wire.Serialize(Wire.CmdGameEvent, JsonValue.Object(("type", "closed"), ("room", Snapshot(room)),
            ("memberId", room.Host.SessionId), ("reason", new string('x', 960))));
        return TransferBudget.LineBytes(line) + 64 <= options.MaxLineBytes;
    }

    private static void Notify(Room room, string type, string memberId, string reason, string exceptId)
    {
        // Bound disconnect descriptions so events cannot exceed the metadata budget.
        if (reason.Length > 160) reason = reason[..160];
        string line = Wire.Serialize(Wire.CmdGameEvent, JsonValue.Object(
            ("type", type), ("room", Snapshot(room)), ("memberId", memberId), ("reason", reason)));
        foreach (ClientSession target in room.Members.Values)
            if (target.SessionId != exceptId && target.Channels.ContainsKey(room.Channel)) target.Send(line);
    }

    private static bool Error(ClientSession sender, JsonValue payload, string code, string message)
    {
        sender.ReplyError(payload, code, message);
        return true;
    }
}
