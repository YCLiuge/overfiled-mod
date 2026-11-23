using System;
using System.Collections.Generic;
using System.Linq;

namespace DrawGuessModule.Core
{
    /// <summary>A public, detached roster and replay summary for a cooperative canvas.</summary>
    public sealed class SharedCanvasSnapshot
    {
        public string Mode { get; set; } = "free";
        public string SessionId { get; set; } = "";
        public string HostId { get; set; } = "";
        public long Version { get; set; }
        public long DrawingVersion { get; set; }
        public int ReplayCount { get; set; }
        public List<PlayerSnapshot> Players { get; set; } = new List<PlayerSnapshot>();
    }

    /// <summary>
    /// Single-threaded, host-authoritative cooperative drawing. Supply authenticated member IDs
    /// separately from commands and finite, nondecreasing monotonic seconds. The host is the first
    /// member; the same class works offline with only that member. No game lifecycle is involved.
    /// </summary>
    public sealed class SharedCanvas
    {
        public const int MaximumPlayers = 16;
        public const int MaximumReplayOperations = 1000;
        public static IReadOnlyList<string> Palette => AuthoritativeGame.Palette;

        private sealed class Player
        {
            public string Id = "";
            public string Name = "";
            public long LastOpId, LastSeenOpId;
            public long CurrentStrokeId;
            public readonly Queue<double> Commands = new Queue<double>();
        }

        private readonly Dictionary<string, Player> _players = new Dictionary<string, Player>(StringComparer.Ordinal);
        private readonly List<string> _memberOrder = new List<string>();
        private readonly List<DrawingOperation> _operations = new List<DrawingOperation>();
        private readonly Dictionary<long, string> _strokeOwners = new Dictionary<long, string>();
        private double _now;
        private long _version = 1, _drawingVersion, _strokeId;

        public string HostId { get; }
        public string SessionId { get; }
        public long Version => _version;
        public long DrawingVersion => _drawingVersion;
        /// <summary>
        /// Detached canonical replay. Undo and clear are returned by Handle but are not retained;
        /// after either operation, replace a replica's replay with this list before replaying it.
        /// </summary>
        public IReadOnlyList<DrawingOperation> Operations => _operations.Select(operation => operation.Copy()).ToArray();

        public SharedCanvas(string hostId, string sessionId, double now)
        {
            if (!ValidIdentity(hostId) || !ValidIdentity(sessionId))
                throw new ArgumentException("Host and session IDs must be nonempty bounded identifiers.");
            if (!Finite(now) || now < 0) throw new ArgumentOutOfRangeException(nameof(now));
            HostId = hostId;
            SessionId = sessionId;
            _now = now;
            _players.Add(hostId, new Player { Id = hostId, Name = hostId });
            _memberOrder.Add(hostId);
        }

        /// <summary>Called only by the trusted membership transport. Existing members may be renamed.</summary>
        public CommandResult AddPlayer(string id, string name, double now)
        {
            if (!AdvanceClock(now)) return Fail("invalid_time");
            if (!ValidIdentity(id)) return Fail("invalid_sender");
            if (!ValidName(name)) return Fail("invalid_name");
            string trimmed = name.Trim();
            if (_players.TryGetValue(id, out var player))
            {
                if (player.Name == trimmed) return Ok("already_joined", false);
                player.Name = trimmed;
                _version++;
                return Ok("renamed", true);
            }
            if (_players.Count >= MaximumPlayers) return Fail("room_full");
            _players.Add(id, new Player { Id = id, Name = trimmed });
            _memberOrder.Add(id);
            _version++;
            return Ok("joined", true);
        }

        /// <summary>
        /// Removes membership without erasing retained artwork. Rejoining creates a new command-ID
        /// and rate-budget epoch; the authenticated ID still owns its retained strokes.
        /// </summary>
        public CommandResult RemovePlayer(string id, double now)
        {
            if (!AdvanceClock(now)) return Fail("invalid_time");
            if (!ValidIdentity(id)) return Fail("invalid_sender");
            if (!_players.Remove(id)) return Ok("already_left", false);
            _memberOrder.Remove(id);
            _version++;
            return Ok("left", true);
        }

        public CommandResult Handle(string sender, GameCommand input, double now)
        {
            if (!AdvanceClock(now)) return Fail("invalid_time");
            if (!ValidIdentity(sender)) return Fail("invalid_sender");
            if (input == null || input.Type == null || input.Type.Length > 16) return Fail("invalid_command");
            if (!string.Equals(input.SessionId, SessionId, StringComparison.Ordinal)) return Fail("wrong_session");
            if (input.ExpectedVersion < -1 || (input.ExpectedVersion >= 0 && input.ExpectedVersion != _version))
                return Fail("stale_version");
            if (!_players.TryGetValue(sender, out var player)) return Fail("not_member");
            while (player.Commands.Count > 0 && now - player.Commands.Peek() >= 1) player.Commands.Dequeue();
            if (player.Commands.Count >= 60) return Fail("command_rate_limited");
            player.Commands.Enqueue(now);
            if (!string.Equals(input.Type, "draw", StringComparison.OrdinalIgnoreCase)) return Fail("unknown_command");
            if (input.RoundId != 0 || input.Operation == null || input.Operation.RoundId != 0) return Fail("stale_round");

            var operation = input.Operation;
            if (operation.OpId < 1) return Fail("invalid_operation_id");
            if (operation.OpId <= player.LastOpId) return Ok("duplicate_operation", false);
            if (operation.OpId - player.LastSeenOpId > 4096) return Fail("operation_gap");
            string tool = operation.Tool == null ? "" : operation.Tool.ToLowerInvariant();
            if (tool != "line" && tool != "brush" && tool != "fill" && tool != "undo" && tool != "clear")
                return Fail("invalid_tool");
            if (operation.X1 < 0 || operation.X1 > 511 || operation.Y1 < 0 || operation.Y1 > 511 ||
                operation.X2 < 0 || operation.X2 > 511 || operation.Y2 < 0 || operation.Y2 > 511 ||
                operation.Size < 1 || operation.Size > 32 || operation.Color == null ||
                !Palette.Contains(operation.Color.ToUpperInvariant())) return Fail("invalid_drawing");
            // Valid attempts advance gap tracking even when capacity or permissions reject
            // them. Accepted-operation dedup stays separate, so a failed ID can be retried.
            player.LastSeenOpId = Math.Max(player.LastSeenOpId, operation.OpId);
            if (tool == "clear" && sender != HostId) return Fail("host_only");
            if ((tool == "line" || tool == "brush" || tool == "fill") && _operations.Count >= MaximumReplayOperations)
                return Fail("drawing_full");

            long undoTarget = 0;
            if (tool == "undo")
            {
                for (int i = _operations.Count - 1; i >= 0; i--)
                {
                    long stroke = _operations[i].StrokeId;
                    if (_strokeOwners.TryGetValue(stroke, out var owner) && owner == sender)
                    {
                        undoTarget = stroke;
                        break;
                    }
                }
                if (undoTarget == 0) return Fail("nothing_to_undo");
            }

            var accepted = operation.Copy();
            accepted.Tool = tool;
            accepted.Color = operation.Color.ToUpperInvariant();
            accepted.RoundId = 0;
            accepted.Sequence = _drawingVersion + 1;
            if (tool == "clear")
            {
                _operations.Clear();
                _strokeOwners.Clear();
                foreach (var member in _players.Values) member.CurrentStrokeId = 0;
                accepted.StrokeId = ++_strokeId;
                accepted.NewStroke = true;
            }
            else if (tool == "undo")
            {
                _operations.RemoveAll(retained => retained.StrokeId == undoTarget);
                _strokeOwners.Remove(undoTarget);
                player.CurrentStrokeId = 0;
                accepted.StrokeId = undoTarget;
                accepted.NewStroke = true;
            }
            else
            {
                bool newStroke = tool == "fill" || operation.NewStroke || player.CurrentStrokeId == 0;
                if (newStroke)
                {
                    player.CurrentStrokeId = ++_strokeId;
                    _strokeOwners.Add(player.CurrentStrokeId, sender);
                }
                accepted.StrokeId = player.CurrentStrokeId;
                accepted.NewStroke = newStroke;
                _operations.Add(accepted.Copy());
                // Fill is an atomic stroke and cannot absorb a subsequent brush/line segment.
                if (tool == "fill") player.CurrentStrokeId = 0;
            }
            player.LastOpId = operation.OpId;
            _drawingVersion++;
            _version++;
            return new CommandResult
            {
                Success = true, Code = "drawing_accepted", Changed = true, Version = _version, Operation = accepted
            };
        }

        public SharedCanvasSnapshot Snapshot() => new SharedCanvasSnapshot
        {
            SessionId = SessionId,
            HostId = HostId,
            Version = _version,
            DrawingVersion = _drawingVersion,
            ReplayCount = _operations.Count,
            Players = _memberOrder.Select(id => new PlayerSnapshot { Id = id, Name = _players[id].Name, Score = 0, HasGuessed = false }).ToList()
        };

        public SharedCanvasSnapshot SnapshotFor() => Snapshot();

        private bool AdvanceClock(double now)
        {
            if (!Finite(now) || now < 0 || now < _now) return false;
            _now = now;
            return true;
        }
        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        private static bool ValidIdentity(string id) => !string.IsNullOrWhiteSpace(id) && id.Length <= 128 && !id.Any(char.IsControl);
        private static bool ValidName(string name) => !string.IsNullOrWhiteSpace(name) && name.Length <= 32 && !name.Any(char.IsControl);
        private CommandResult Fail(string code) => new CommandResult { Success = false, Code = code, Message = code, Version = _version };
        private CommandResult Ok(string code, bool changed) => new CommandResult { Success = true, Code = code, Changed = changed, Version = _version };
    }
}
