using System;
using System.Collections.Generic;

namespace Chat.Client
{
    /// <summary>Relay-issued connection identity and display nickname; reconnect gives a new identity.</summary>
    public sealed class GameMember
    {
        public string Id { get; private set; }
        public string Nick { get; private set; }
        public GameMember(string id, string nick) { Id = id; Nick = nick; }
    }

    /// <summary>Transport room snapshot. The relay does not own game state.</summary>
    public sealed class GameRoomInfo
    {
        public string Room { get; private set; }
        /// <summary>Chat channel that scopes this room; empty for legacy server snapshots.</summary>
        public string Channel { get; private set; }
        public string InstanceId { get; private set; }
        public string HostId { get; private set; }
        public string HostNick { get; private set; }
        public IReadOnlyList<GameMember> Members { get; private set; }
        public GameRoomInfo(string room, string instanceId, string hostId, string hostNick, IReadOnlyList<GameMember> members, string channel = "")
        { Room = room; InstanceId = instanceId; HostId = hostId; HostNick = hostNick; Members = members; Channel = channel; }
    }

    /// <summary>Game payload. FromId/From come from the connection, never from payload fields.</summary>
    public sealed class GameMessage
    {
        public string Room { get; private set; }
        public string InstanceId { get; private set; }
        public string FromId { get; private set; }
        public string From { get; private set; }
        public string? ToId { get; private set; }
        public string Text { get; private set; }
        public DateTimeOffset Timestamp { get; private set; }
        public GameMessage(string room, string instanceId, string fromId, string from, string? toId, string text, DateTimeOffset timestamp)
        { Room = room; InstanceId = instanceId; FromId = fromId; From = from; ToId = toId; Text = text; Timestamp = timestamp; }
    }

    /// <summary>Background-thread join/leave/closed notification, with a complete (maximum 16 members) roster.</summary>
    public sealed class GameRoomEvent
    {
        public string Type { get; private set; }
        public GameRoomInfo Room { get; private set; }
        public string MemberId { get; private set; }
        public string Reason { get; private set; }
        public GameRoomEvent(string type, GameRoomInfo room, string memberId, string reason)
        { Type = type; Room = room; MemberId = memberId; Reason = reason; }
    }
}
