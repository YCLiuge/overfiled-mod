using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace DrawGuessModule.Core
{
    /// <summary>Single-threaded host authority. Supply authenticated connection IDs and monotonic seconds.</summary>
    public sealed class AuthoritativeGame
    {
        public const int MaximumPlayers = 16;
        public const int ChoosingSeconds = 30;
        public const int MaximumWordLength = 16;
        public const int MaximumReplayOperations = 1000;
        public static IReadOnlyList<string> Palette { get; } = Array.AsReadOnly(new[] { "#000000", "#FFFFFF", "#FF0000", "#00FF00", "#0000FF", "#FFFF00", "#FF00FF", "#00FFFF", "#808080", "#FFA500", "#800080", "#A52A2A", "#EA4335", "#34A853", "#4285F4", "#FBBC05", "#FF8C00", "#983CD9", "#202020", "#E53935", "#FB8C00", "#FDD835", "#43A047", "#00ACC1", "#1E88E5", "#8E24AA", "#6D4C41", "#EC407A", "#757575" });
        private sealed class Player
        {
            public string Id = "";
            public string Name = "";
            public int Score;
            public bool Guessed;
            public readonly Queue<double> Guesses = new Queue<double>();
            public readonly Queue<double> Commands = new Queue<double>();
            public double LastGuess = double.NegativeInfinity;
        }
        private readonly Dictionary<string, Player> _players = new Dictionary<string, Player>(StringComparer.Ordinal);
        private readonly List<string> _memberOrder = new List<string>();
        private readonly List<string> _turns = new List<string>();
        private readonly List<DrawingOperation> _operations = new List<DrawingOperation>();
        private readonly Func<int, string>? _selectWord;
        private string _phase = "lobby", _drawer = "", _word = "", _lastEvent = "room_created";
        private double _now, _deadline;
        private int _turnPosition = -1, _turnsPerPlayer = 1, _roundSeconds = 60, _revealSeconds = 5;
        private long _roundId, _version = 1, _drawingVersion, _lastOpId, _strokeId;
        private bool _answerRevealed;

        public string HostId { get; }
        public string SessionId { get; }
        public long Version => _version;
        public long DrawingVersion => _drawingVersion;
        public IReadOnlyList<DrawingOperation> Operations => _operations.Select(o => o.Copy()).ToArray();

        public AuthoritativeGame(string hostId, string sessionId, double now, Func<int, string>? wordSelector = null)
        {
            if (!ValidIdentity(hostId) || !ValidIdentity(sessionId)) throw new ArgumentException("Host and session IDs must be nonempty bounded identifiers.");
            if (!Finite(now) || now < 0) throw new ArgumentOutOfRangeException(nameof(now));
            HostId = hostId;
            SessionId = sessionId;
            _now = now;
            // Explicit selectors are an opt-in legacy/test seam. Runtime defaults to player-authored words.
            _selectWord = wordSelector;
            _players.Add(hostId, new Player { Id = hostId, Name = hostId });
            _memberOrder.Add(hostId);
        }

        public CommandResult AddPlayer(string id, string name, double now)
        {
            if (!ValidTime(now)) return Fail("invalid_time");
            Tick(now);
            if (!ValidIdentity(id)) return Fail("invalid_sender");
            if (_phase == "closed") return Fail("room_closed");
            if (!ValidName(name)) return Fail("invalid_name");
            if (_players.TryGetValue(id, out var existing))
            {
                if (existing.Name == name.Trim()) return Ok("already_joined", false);
                existing.Name = name.Trim();
                Changed("player_renamed");
                return Ok("renamed", true);
            }
            if (_players.Count >= MaximumPlayers) return Fail("room_full");
            _players.Add(id, new Player { Id = id, Name = name.Trim() });
            _memberOrder.Add(id);
            if (_phase == "playing" || _phase == "reveal")
                for (int i = 0; i < _turnsPerPlayer; i++) _turns.Add(id);
            Changed("player_joined");
            return Ok("joined", true);
        }

        public CommandResult RemovePlayer(string id, double now)
        {
            if (!ValidTime(now)) return Fail("invalid_time");
            Tick(now);
            if (!ValidIdentity(id)) return Fail("invalid_sender");
            if (!_players.Remove(id)) return Ok("already_left", false);
            _memberOrder.Remove(id);
            if (id == HostId)
            {
                _phase = "closed";
                _word = "";
                _drawer = "";
                _operations.Clear();
                _drawingVersion++;
                Changed("host_disconnected");
            }
            else
            {
                Changed("player_left");
                if (_phase == "playing" || _phase == "choosing")
                {
                    if (id == _drawer) Reveal("drawer_disconnected");
                    else if (_players.Count < 2) Reveal("not_enough_players");
                    else if (_phase == "playing" && AllGuessed()) Reveal("all_guessed");
                }
            }
            return Ok("left", true);
        }

        public CommandResult Handle(string sender, GameCommand command, double now)
        {
            if (!ValidTime(now)) return Fail("invalid_time");
            Tick(now);
            if (!ValidIdentity(sender)) return Fail("invalid_sender");
            if (command == null || command.Type == null || command.Type.Length > 16) return Fail("invalid_command");
            if (!string.Equals(command.SessionId, SessionId, StringComparison.Ordinal)) return Fail("wrong_session");
            if (command.ExpectedVersion < -1 || (command.ExpectedVersion >= 0 && command.ExpectedVersion != _version)) return Fail("stale_version");
            if (_phase == "closed") return Fail("room_closed");
            string type = command.Type.ToLowerInvariant();
            if (type == "join") return AddPlayer(sender, command.Name, now);
            if (!_players.TryGetValue(sender, out var player)) return Fail("not_member");
            while (player.Commands.Count > 0 && now - player.Commands.Peek() >= 1) player.Commands.Dequeue();
            if (player.Commands.Count >= 60) return Fail("command_rate_limited");
            player.Commands.Enqueue(now);
            if (type == "leave") return RemovePlayer(sender, now);
            if (type == "start" || type == "next" || type == "close")
            {
                if (sender != HostId) return Fail("host_only");
                if (type == "close") { _phase = "closed"; _word = ""; _drawer = ""; _operations.Clear(); _drawingVersion++; Changed("room_closed"); return Ok("closed", true); }
                if (type == "start") return Start(command);
                if (command.RoundId != _roundId) return Fail("stale_round");
                if (_phase == "playing" || _phase == "choosing") { Reveal("host_skipped"); return Ok("revealed", true); }
                if (_phase == "reveal") { AdvanceTurn(); return Ok("advanced", true); }
                return Fail("invalid_phase");
            }
            if (type == "word") return SubmitWord(player, command);
            if (type == "guess") return Guess(player, command);
            if (type == "draw") return Draw(player, command);
            return Fail("unknown_command");
        }

        public bool Tick(double now)
        {
            if (!ValidTime(now)) return false;
            _now = now;
            long before = _version;
            if (_phase == "choosing" && now >= _deadline) Reveal("word_timeout");
            else if (_phase == "playing" && now >= _deadline) Reveal("timeout");
            else if (_phase == "reveal" && now >= _deadline) AdvanceTurn();
            return _version != before;
        }

        public GameSnapshot SnapshotFor(string viewer, double now)
        {
            if (!ValidTime(now)) throw new ArgumentOutOfRangeException(nameof(now), "Use finite nondecreasing monotonic seconds.");
            bool visible = _word.Length > 0 && (_phase == "reveal" || (_phase == "finished" && _answerRevealed) || (_phase == "playing" && viewer == _drawer && _players.ContainsKey(viewer)));
            return new GameSnapshot
            {
                SessionId = SessionId, HostId = HostId, Phase = _phase, Version = _version, DrawingVersion = _drawingVersion,
                RoundId = _roundId, RoundNumber = _turnPosition < 0 ? 0 : _turnPosition + 1, TotalRounds = _turns.Count,
                DrawerId = _drawer, RemainingSeconds = _phase == "choosing" || _phase == "playing" || _phase == "reveal" ? Math.Max(0, _deadline - now) : 0,
                Word = visible ? _word : null, WordLength = _phase == "playing" || _phase == "reveal" ? _word.Length : 0,
                LastEvent = _lastEvent, ReplayCount = _operations.Count,
                Players = _memberOrder.Where(id => _players.ContainsKey(id)).Select(id => new PlayerSnapshot { Id = id, Name = _players[id].Name, Score = _players[id].Score, HasGuessed = _players[id].Guessed }).ToList()
            };
        }

        public IReadOnlyList<DrawingChunk> GetDrawingChunks(int maxOperations = 64)
        {
            if (maxOperations < 1 || maxOperations > 64) throw new ArgumentOutOfRangeException(nameof(maxOperations));
            int count = Math.Max(1, (_operations.Count + maxOperations - 1) / maxOperations);
            var chunks = new List<DrawingChunk>(count);
            for (int i = 0; i < count; i++) chunks.Add(new DrawingChunk { SessionId = SessionId, RoundId = _roundId, DrawingVersion = _drawingVersion, Index = i, Count = count, Operations = _operations.Skip(i * maxOperations).Take(maxOperations).Select(o => o.Copy()).ToList() });
            return chunks;
        }

        private CommandResult Start(GameCommand command)
        {
            if (_phase != "lobby" && _phase != "finished") return Fail("invalid_phase");
            if (_players.Count < 2) return Fail("not_enough_players");
            if (command.TurnsPerPlayer < 1 || command.TurnsPerPlayer > 3 || command.RoundSeconds < 30 || command.RoundSeconds > 180 || command.RevealSeconds < 3 || command.RevealSeconds > 15) return Fail("invalid_settings");
            // Select before touching match state: a failed injected selector cannot partially start a game.
            string? firstWord = _selectWord == null ? null : SelectWord(_roundId + 1);
            _turnsPerPlayer = command.TurnsPerPlayer;
            _roundSeconds = command.RoundSeconds;
            _revealSeconds = command.RevealSeconds;
            _turns.Clear();
            for (int i = 0; i < _turnsPerPlayer; i++) _turns.AddRange(_memberOrder);
            foreach (var player in _players.Values) player.Score = 0;
            _turnPosition = 0;
            BeginRound(firstWord);
            return Ok("started", true);
        }

        private void BeginRound(string? word)
        {
            _roundId++;
            _drawer = _turns[_turnPosition];
            _word = word ?? "";
            _answerRevealed = false;
            _phase = word == null ? "choosing" : "playing";
            _deadline = _now + (word == null ? ChoosingSeconds : _roundSeconds);
            _lastOpId = 0;
            _strokeId = 0;
            _operations.Clear();
            _drawingVersion++;
            foreach (var player in _players.Values) { player.Guessed = false; player.Guesses.Clear(); player.LastGuess = double.NegativeInfinity; }
            Changed(word == null ? "awaiting_word" : "round_started");
        }

        private void Reveal(string reason)
        {
            _phase = "reveal";
            _answerRevealed = true;
            _deadline = _now + _revealSeconds;
            Changed(reason);
        }

        private void AdvanceTurn()
        {
            if (_players.Count < 2) { _phase = "finished"; Changed("not_enough_players"); return; }
            int next = _turnPosition + 1;
            while (next < _turns.Count && !_players.ContainsKey(_turns[next])) next++;
            if (next >= _turns.Count) { _phase = "finished"; Changed("match_finished"); return; }
            string? word = _selectWord == null ? null : SelectWord(_roundId + 1);
            _turnPosition = next;
            BeginRound(word);
        }

        private CommandResult SubmitWord(Player player, GameCommand command)
        {
            if (command.RoundId != _roundId) return Fail("stale_round");
            if (player.Id != _drawer) return Fail("drawer_only");
            if (_phase != "choosing") return Fail("invalid_phase");
            string? word = command.Text;
            if (word == null || word.Any(char.IsControl)) return Fail("invalid_word");
            word = word.Trim();
            if (word.Length < 1 || word.Length > MaximumWordLength) return Fail("invalid_word");
            _word = word;
            _phase = "playing";
            _deadline = _now + _roundSeconds;
            Changed("word_submitted");
            return Ok("word_submitted", true);
        }

        private CommandResult Guess(Player player, GameCommand command)
        {
            if (_phase != "playing") return Fail("invalid_phase");
            if (command.RoundId != _roundId) return Fail("stale_round");
            if (player.Id == _drawer) return Fail("drawer_cannot_guess");
            if (player.Guessed) return Fail("already_guessed");
            if (string.IsNullOrWhiteSpace(command.Text) || command.Text.Length > 64 || command.Text.Any(char.IsControl)) return Fail("invalid_guess");
            while (player.Guesses.Count > 0 && _now - player.Guesses.Peek() >= 5) player.Guesses.Dequeue();
            if (_now - player.LastGuess < 0.3 || player.Guesses.Count >= 3) return Fail("guess_rate_limited");
            player.Guesses.Enqueue(_now);
            player.LastGuess = _now;
            if (!string.Equals(NormalizeGuess(command.Text), NormalizeGuess(_word), StringComparison.OrdinalIgnoreCase)) return Ok("wrong_guess", false, "未猜中");
            player.Guessed = true;
            int points = 100 + (int)Math.Floor(100 * Math.Max(0, Math.Min(1, (_deadline - _now) / _roundSeconds)));
            player.Score += points;
            if (_players.TryGetValue(_drawer, out var drawer)) drawer.Score += 50;
            Changed("correct_guess");
            if (AllGuessed()) Reveal("all_guessed");
            return Ok("correct_guess", true, "猜中了");
        }

        private CommandResult Draw(Player player, GameCommand command)
        {
            if (_phase != "playing") return Fail("invalid_phase");
            if (command.RoundId != _roundId || command.Operation == null || command.Operation.RoundId != _roundId) return Fail("stale_round");
            if (player.Id != _drawer) return Fail("drawer_only");
            var input = command.Operation;
            if (input.OpId < 1) return Fail("invalid_operation_id");
            if (input.OpId <= _lastOpId) return Ok("duplicate_operation", false);
            if (input.OpId - _lastOpId > 4096) return Fail("operation_gap");
            string tool = input.Tool == null ? "" : input.Tool.ToLowerInvariant();
            if (tool != "line" && tool != "fill" && tool != "clear" && tool != "undo") return Fail("invalid_tool");
            if (input.X1 < 0 || input.X1 > 511 || input.Y1 < 0 || input.Y1 > 511 || input.X2 < 0 || input.X2 > 511 || input.Y2 < 0 || input.Y2 > 511 || input.Size < 1 || input.Size > 32 || input.Color == null || !Palette.Contains(input.Color.ToUpperInvariant())) return Fail("invalid_drawing");
            if ((tool == "line" || tool == "fill") && _operations.Count >= MaximumReplayOperations) return Fail("drawing_full");
            if (tool == "undo" && !_operations.Any(o => o.Tool == "line" || o.Tool == "fill")) return Fail("nothing_to_undo");
            var accepted = input.Copy();
            accepted.Tool = tool;
            accepted.Color = input.Color.ToUpperInvariant();
            accepted.Sequence = _drawingVersion + 1;
            if (tool == "clear")
            {
                _operations.Clear();
                _strokeId++;
                accepted.NewStroke = true;
                accepted.StrokeId = _strokeId;
                _operations.Add(accepted.Copy());
            }
            else if (tool == "undo")
            {
                long target = _operations.Last(o => o.Tool == "line" || o.Tool == "fill").StrokeId;
                _operations.RemoveAll(o => o.StrokeId == target);
                _strokeId++;
                accepted.StrokeId = target;
                accepted.NewStroke = true;
            }
            else
            {
                if (tool == "fill" || input.NewStroke || _operations.Count == 0 || _operations[_operations.Count - 1].Tool != "line") { _strokeId++; accepted.NewStroke = true; }
                accepted.StrokeId = _strokeId;
                _operations.Add(accepted.Copy());
            }
            _lastOpId = input.OpId;
            _drawingVersion++;
            Changed("drawing_changed");
            return new CommandResult { Success = true, Code = "drawing_accepted", Message = "", Changed = true, Version = _version, Operation = accepted };
        }

        private bool AllGuessed() => _players.Values.Any(p => p.Id != _drawer) && _players.Values.Where(p => p.Id != _drawer).All(p => p.Guessed);
        private string SelectWord(long round)
        {
            string word;
            try { word = _selectWord!(checked((int)round)); }
            catch { throw new InvalidOperationException("Word selector failed."); }
            if (string.IsNullOrWhiteSpace(word) || word.Length > 16 || word.Any(char.IsControl)) throw new InvalidOperationException("Word selector must return a nonempty bounded word.");
            return word.Trim();
        }
        private static string NormalizeGuess(string text) => string.Concat(text.Normalize(NormalizationForm.FormKC).Where(c => !char.IsWhiteSpace(c)));
        private bool ValidTime(double now) => Finite(now) && now >= _now && now >= 0;
        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        private static bool ValidIdentity(string id) => !string.IsNullOrWhiteSpace(id) && id.Length <= 128 && !id.Any(char.IsControl);
        private static bool ValidName(string name) => !string.IsNullOrWhiteSpace(name) && name.Length <= 32 && !name.Any(char.IsControl);
        private void Changed(string reason) { _lastEvent = reason; _version++; }
        private CommandResult Fail(string code) => new CommandResult { Success = false, Code = code, Message = code, Changed = false, Version = _version };
        private CommandResult Ok(string code, bool changed, string message = "") => new CommandResult { Success = true, Code = code, Message = message, Changed = changed, Version = _version };
    }
}
