using DrawGuessModule.Core;

int checks = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new Exception("FAIL: " + name);
    checks++;
    Console.WriteLine("PASS: " + name);
}
void Throws<T>(Action action, string name) where T : Exception
{
    try { action(); }
    catch (T) { Check(true, name); return; }
    throw new Exception("FAIL: " + name);
}
SharedCanvas Make() => new("host", "session", 0);
GameCommand Draw(long id, string tool = "line", bool newStroke = true) => new()
{
    Type = "draw", SessionId = "session", RoundId = 0,
    Operation = new DrawingOperation
    {
        OpId = id, Tool = tool, RoundId = 0, X1 = 0, Y1 = 511, X2 = 511, Y2 = 0,
        Size = 1, Color = "#ff0000", NewStroke = newStroke,
        Sequence = -100, StrokeId = long.MaxValue
    }
};
void Fails(SharedCanvas canvas, string sender, GameCommand command, double now, string code, string name)
{
    long version = canvas.Version, drawingVersion = canvas.DrawingVersion;
    var replay = canvas.Operations.Select(o => (o.Sequence, o.StrokeId, o.Tool, o.OpId)).ToArray();
    var result = canvas.Handle(sender, command, now);
    Check(!result.Success && result.Code == code && !result.Changed && result.Operation == null &&
        result.Version == version && canvas.Version == version && canvas.DrawingVersion == drawingVersion &&
        replay.SequenceEqual(canvas.Operations.Select(o => (o.Sequence, o.StrokeId, o.Tool, o.OpId))), name);
}

Throws<ArgumentException>(() => new SharedCanvas("", "session", 0), "empty host rejected");
Throws<ArgumentException>(() => new SharedCanvas("host", new string('x', 129), 0), "oversized session rejected");
Throws<ArgumentException>(() => new SharedCanvas("host\n", "session", 0), "control characters in identity rejected");
foreach (double constructorTime in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, -1d })
    Throws<ArgumentOutOfRangeException>(() => new SharedCanvas("host", "session", constructorTime), "constructor rejects invalid clock " + constructorTime);

var offline = Make();
var initial = offline.Snapshot();
Check(initial.Mode == "free" && initial.HostId == "host" && initial.SessionId == "session" && initial.Version == 1 &&
    initial.DrawingVersion == 0 && initial.ReplayCount == 0 && initial.Players.Single().Id == "host", "offline host starts as sole member");
Check(offline.SnapshotFor().Players.Single().Score == 0 && !offline.SnapshotFor().Players.Single().HasGuessed,
    "viewer-free snapshot has no gameplay state");
Check(ReferenceEquals(SharedCanvas.Palette, AuthoritativeGame.Palette), "existing palette is reused");
var input = Draw(1, "BrUsH");
var first = offline.Handle("host", input, 0);
Check(first.Success && first.Changed && first.Operation!.Tool == "brush" && first.Operation.RoundId == 0 &&
    first.Operation.Sequence == 1 && first.Operation.StrokeId == 1 && first.Operation.NewStroke && first.Operation.Color == "#FF0000" &&
    offline.Version == 2 && offline.DrawingVersion == 1, "offline drawing normalizes and assigns authoritative IDs");
input.Operation!.X1 = 300;
first.Operation!.Color = "corrupt";
var exposed = offline.Operations;
exposed[0].Tool = "corrupt";
((DrawingOperation[])exposed)[0] = new DrawingOperation();
initial.Players.Clear();
var detached = offline.Snapshot();
detached.Players[0].Name = "corrupt";
Check(offline.Operations[0].X1 == 0 && offline.Operations[0].Color == "#FF0000" && offline.Operations[0].Tool == "brush" &&
    offline.Snapshot().Players.Single().Name == "host", "inputs, results, replay and roster cannot mutate authority");

var members = Make();
Check(members.AddPlayer("a", " Alice ", 0).Success && members.Snapshot().Players[1].Name == "Alice", "authenticated transport joins and trims names");
long memberVersion = members.Version;
Check(members.AddPlayer("a", "Alice", 0).Code == "already_joined" && members.Version == memberVersion, "repeated membership is idempotent");
Check(members.AddPlayer("a", "Alicia", 0).Code == "renamed" && members.Version == memberVersion + 1 && members.DrawingVersion == 0,
    "rename changes only membership version");
Check(!members.AddPlayer("bad", "\n", 0).Success && !members.AddPlayer("bad", new string('n', 33), 0).Success &&
    !members.AddPlayer("bad\n", "Name", 0).Success, "bounded membership identities and names validated");
for (int i = 0; i < 14; i++) Check(members.AddPlayer("p" + i, "Player " + i, 0).Success, "member slot " + (i + 3));
Check(members.Snapshot().Players.Count == 16 && members.AddPlayer("overflow", "Overflow", 0).Code == "room_full", "sixteen-member maximum includes host");
Check(members.RemovePlayer("p0", 0).Changed && !members.RemovePlayer("p0", 0).Changed && members.AddPlayer("new", "New", 0).Success &&
    members.Snapshot().Players.Last().Id == "new", "removal is idempotent and frees a roster slot");

var auth = Make();
Fails(auth, "outsider", Draw(1), 0, "not_member", "outsider cannot draw");
Fails(auth, "", Draw(1), 0, "invalid_sender", "invalid sender rejected");
Fails(auth, "host", null!, 0, "invalid_command", "null command rejected");
var bad = Draw(1); bad.Type = null!;
Fails(auth, "host", bad, 0, "invalid_command", "null command type rejected");
bad = Draw(1); bad.Type = new string('x', 17);
Fails(auth, "host", bad, 0, "invalid_command", "command type bounded");
bad = Draw(1); bad.SessionId = "other";
Fails(auth, "host", bad, 0, "wrong_session", "other session denied");
bad = Draw(1); bad.ExpectedVersion = 0;
Fails(auth, "host", bad, 0, "stale_version", "stale expected version denied");
bad.ExpectedVersion = -2;
Fails(auth, "host", bad, 0, "stale_version", "invalid expected version denied");
foreach (string type in new[] { "guess", "start", "next", "join", "leave", "close" })
{
    bad = Draw(1); bad.Type = type;
    Fails(auth, "host", bad, 0, "unknown_command", "game command not supported: " + type);
}
bad = Draw(1); bad.RoundId = 1;
Fails(auth, "host", bad, 0, "stale_round", "nonzero command round denied");
bad = Draw(1); bad.Operation!.RoundId = 1;
Fails(auth, "host", bad, 0, "stale_round", "nonzero operation round denied");
bad = Draw(1); bad.Operation = null;
Fails(auth, "host", bad, 0, "stale_round", "missing operation denied");

var invalidMutations = new Action<DrawingOperation>[]
{
    o => o.X1 = -1, o => o.X1 = 512, o => o.Y1 = -1, o => o.Y1 = 512,
    o => o.X2 = -1, o => o.X2 = 512, o => o.Y2 = -1, o => o.Y2 = 512,
    o => o.Size = 0, o => o.Size = 33, o => o.Color = "#123456", o => o.Color = null!
};
for (int i = 0; i < invalidMutations.Length; i++)
{
    bad = Draw(1); invalidMutations[i](bad.Operation!);
    Fails(auth, "host", bad, 0, "invalid_drawing", "drawing validation boundary " + i);
}
bad = Draw(1, "unknown");
Fails(auth, "host", bad, 0, "invalid_tool", "unknown tool denied");
bad.Operation!.Tool = null!;
Fails(auth, "host", bad, 0, "invalid_tool", "null tool denied");
Fails(auth, "host", Draw(0), 0, "invalid_operation_id", "zero operation ID denied");
Fails(auth, "host", Draw(-1), 0, "invalid_operation_id", "negative operation ID denied");
Fails(auth, "host", Draw(4097), 0, "operation_gap", "initial operation gap bounded");
bad = Draw(4096); bad.Operation!.Size = 32; bad.ExpectedVersion = auth.Version;
Check(auth.Handle("host", bad, 0).Success, "inclusive coordinate, size, expected version and gap boundaries accepted");
long authVersion = auth.Version;
var duplicate = auth.Handle("host", Draw(1), 0);
Check(duplicate.Success && duplicate.Code == "duplicate_operation" && !duplicate.Changed && duplicate.Operation == null && auth.Version == authVersion,
    "old per-sender operation ID deduplicates without changing state");
Fails(auth, "host", Draw(8193), 0, "operation_gap", "subsequent operation gap bounded");
Check(auth.Handle("host", Draw(8192), 0).Success, "rejected operations do not advance dedup watermark");
Fails(auth, "host", Draw(long.MaxValue), 0, "operation_gap", "huge operation ID cannot overflow gap validation");

var shared = Make(); shared.AddPlayer("a", "Alice", 0); shared.AddPlayer("b", "Bob", 0);
var a1 = shared.Handle("a", Draw(1, "line", false), 0).Operation!;
var b1 = shared.Handle("b", Draw(1, "brush", false), 0).Operation!;
var a2 = shared.Handle("a", Draw(2, "brush", false), 0).Operation!;
Check(a1.Sequence == 1 && b1.Sequence == 2 && a2.Sequence == 3 && a1.StrokeId == a2.StrokeId && a1.StrokeId != b1.StrokeId &&
    a1.NewStroke && b1.NewStroke && !a2.NewStroke, "interleaved senders have independent dedup and continuous strokes with shared ordering");
var undoA = shared.Handle("a", Draw(3, "undo"), 0);
Check(undoA.Success && undoA.Operation!.StrokeId == a1.StrokeId && undoA.Operation.Sequence == 4 && undoA.Operation.Tool == "undo" &&
    shared.Operations.Count == 1 && shared.Operations[0].StrokeId == b1.StrokeId, "undo removes only caller's entire retained stroke");
Fails(shared, "a", Draw(4, "undo"), 0, "nothing_to_undo", "sender cannot undo someone else's stroke");
var b2 = shared.Handle("b", Draw(2, "line", false), 0).Operation!;
Check(b2.StrokeId == b1.StrokeId && !b2.NewStroke, "other sender's undo does not break a continuous stroke");
var fill = shared.Handle("a", Draw(5, "fill", false), 0).Operation!;
var afterFill = shared.Handle("a", Draw(6, "line", false), 0).Operation!;
Check(fill.NewStroke && afterFill.NewStroke && fill.StrokeId != afterFill.StrokeId, "fill is atomic and subsequent drawing starts a new stroke");
Check(shared.Handle("a", Draw(7, "undo"), 0).Operation!.StrokeId == afterFill.StrokeId &&
    shared.Handle("a", Draw(8, "undo"), 0).Operation!.StrokeId == fill.StrokeId, "repeated undo walks only the caller's retained strokes");
Fails(shared, "b", Draw(3, "clear"), 0, "host_only", "guest cannot clear shared artwork");
long priorSequence = shared.DrawingVersion;
var clear = shared.Handle("host", Draw(1, "clear"), 0);
Check(clear.Success && clear.Operation!.Tool == "clear" && clear.Operation.RoundId == 0 && clear.Operation.Sequence == priorSequence + 1 &&
    shared.Operations.Count == 0 && shared.Snapshot().ReplayCount == 0, "host clear empties canonical replay and advances sequence");
Fails(shared, "b", Draw(3, "undo"), 0, "nothing_to_undo", "clear erases retained ownership");
var afterClear = shared.Handle("b", Draw(4, "line", false), 0).Operation!;
Check(afterClear.NewStroke && afterClear.StrokeId > clear.Operation!.StrokeId && afterClear.Sequence > clear.Operation.Sequence &&
    shared.Handle("b", Draw(2), 0).Code == "duplicate_operation", "clear resets grouping while preserving dedup and monotonic IDs");
shared.RemovePlayer("b", 0);
Check(shared.Operations.Count == 1, "leaving retains shared artwork");
Fails(shared, "b", Draw(5), 0, "not_member", "removed member cannot draw");
shared.AddPlayer("b", "Bob again", 0);
Check(shared.Handle("b", Draw(1, "undo"), 0).Success && shared.Operations.Count == 0, "authenticated rejoin starts new command epoch and retains ownership");
shared.RemovePlayer("host", 0);
Check(shared.Handle("a", Draw(9), 0).Success, "membership removal has no implicit game lifecycle");

var time = Make();
foreach (double invalid in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, -1d })
    Fails(time, "host", Draw(1), invalid, "invalid_time", "invalid drawing time rejected: " + invalid);
Check(time.AddPlayer("a", "Alice", 10).Success, "membership advances monotonic clock");
Fails(time, "host", Draw(1), 9, "invalid_time", "regressing handle time rejected");
Check(time.AddPlayer("b", "Bob", 9).Code == "invalid_time" && time.RemovePlayer("a", double.NaN).Code == "invalid_time", "membership shares finite nondecreasing clock");
Fails(time, "host", Draw(1, "invalid"), 11, "invalid_tool", "rejected command still samples valid host time");
Fails(time, "host", Draw(1), 10, "invalid_time", "failed input cannot rewind host clock");
Check(time.Handle("host", Draw(1), 11).Success, "equal monotonic timestamps accepted");

var budget = Make(); budget.AddPlayer("a", "Alice", 0);
bool sixty = true;
for (int i = 1; i <= 60; i++) sixty &= budget.Handle("host", Draw(i), 0).Success;
Check(sixty, "sixty commands per sender accepted in rolling second");
Fails(budget, "host", Draw(61), 0.999, "command_rate_limited", "sixty-first command denied before one second");
Check(budget.Handle("a", Draw(1), 0.999).Success && budget.Handle("host", Draw(61), 1).Success, "budgets are per sender and expire at exact one-second boundary");
var badBudget = Make();
for (int i = 0; i < 60; i++) badBudget.Handle("host", Draw(1, "invalid"), 0);
Fails(badBudget, "host", Draw(1), 0, "command_rate_limited", "invalid tools consume command budget");
var duplicateBudget = Make();
for (int i = 0; i < 60; i++) duplicateBudget.Handle("host", Draw(1), 0);
Fails(duplicateBudget, "host", Draw(2), 0, "command_rate_limited", "duplicates consume command budget");

var full = Make(); full.AddPlayer("a", "Alice", 0);
bool allAccepted = true;
for (int i = 1; i <= SharedCanvas.MaximumReplayOperations; i++) allAccepted &= full.Handle("host", Draw(i), i * 0.03).Success;
Check(allAccepted && full.Operations.Count == 1000 && full.Snapshot().ReplayCount == 1000, "canonical replay bounded at one thousand operations");
Fails(full, "a", Draw(1, "fill"), 30.03, "drawing_full", "fill denied when replay full");
Check(full.Handle("host", Draw(1001, "undo"), 30.06).Success && full.Operations.Count == 999 &&
    full.Handle("a", Draw(1, "brush"), 30.09).Success, "owner undo at capacity restores a replay slot");
Check(full.Handle("host", Draw(1002, "clear"), 30.12).Success && full.Operations.Count == 0 &&
    full.Handle("a", Draw(2), 30.15).Success, "host clear at capacity restores replay budget");

// Valid rejected attempts must not strand a full local/shared canvas beyond the ID-gap window.
var exhausted = Make(); exhausted.AddPlayer("a", "Alice", 0);
bool filledAfterSaturation = true;
for (int i = 1; i <= 1000; i++) filledAfterSaturation &= exhausted.Handle("host", Draw(i), i * 0.03).Success;
long saturatedVersion = exhausted.Version;
bool allFullRejected = true;
for (int i = 1; i <= 4100; i++)
{
    double attemptTime = (1000 + i) * 0.03;
    allFullRejected &= exhausted.Handle("host", Draw(1000 + i), attemptTime).Code == "drawing_full";
    allFullRejected &= exhausted.Handle("a", Draw(i), attemptTime).Code == "drawing_full";
}
Check(filledAfterSaturation && allFullRejected && exhausted.Version == saturatedVersion && exhausted.DrawingVersion == 1000 &&
    exhausted.Operations.Count == 1000, "4100 valid rejected draws per member preserve full canvas and accepted versions");
Check(exhausted.Handle("host", Draw(5101, "undo"), 153.03).Success && exhausted.Operations.Count == 999,
    "owner undo succeeds after more than 4096 full-canvas rejections");
Check(exhausted.Handle("a", Draw(4100), 153.06).Success && exhausted.Operations.Count == 1000,
    "previously rejected operation ID can be retried when undo frees capacity");
Check(exhausted.Handle("host", Draw(5102, "clear"), 153.09).Success && exhausted.Operations.Count == 0 &&
    exhausted.Handle("host", Draw(5103), 153.12).Success,
    "host clear and offline drawing continue after full-canvas ID saturation");
var invalidWatermark = Make();
var invalidSeen = Draw(4096); invalidSeen.Operation!.X1 = -1;
Fails(invalidWatermark, "host", invalidSeen, 0, "invalid_drawing", "invalid drawing near ID-gap edge rejected");
Fails(invalidWatermark, "host", Draw(8192), 0.03, "operation_gap", "invalid payload cannot advance seen operation watermark");
Check(invalidWatermark.Handle("host", Draw(4096), 0.06).Success,
    "corrected invalid operation ID remains acceptable within original gap window");

// Compare deterministic mixed traffic to an independent retained-stroke ownership model.
var modelCanvas = Make(); modelCanvas.AddPlayer("a", "Alice", 0); modelCanvas.AddPlayer("b", "Bob", 0);
var random = new Random(982451653);
var ids = new Dictionary<string, long> { ["host"] = 0, ["a"] = 0, ["b"] = 0 };
var model = new List<(string Owner, long Stroke, long Sequence)>();
long sequence = 0;
bool modelMatches = true;
for (int i = 0; i < 600; i++)
{
    string sender = new[] { "host", "a", "b" }[random.Next(3)];
    string tool = new[] { "line", "brush", "fill", "undo", "clear" }[random.Next(5)];
    var command = Draw(++ids[sender], tool, random.Next(3) == 0);
    var result = modelCanvas.Handle(sender, command, i * 0.03);
    long target = model.LastOrDefault(item => item.Owner == sender).Stroke;
    bool expectedSuccess = (tool != "clear" || sender == "host") && (tool != "undo" || target != 0);
    modelMatches &= result.Success == expectedSuccess;
    if (result.Success)
    {
        sequence++;
        modelMatches &= result.Operation!.Sequence == sequence && result.Operation.RoundId == 0;
        if (tool == "clear") model.Clear();
        else if (tool == "undo")
        {
            modelMatches &= result.Operation!.StrokeId == target;
            model.RemoveAll(item => item.Stroke == target);
        }
        else model.Add((sender, result.Operation!.StrokeId, sequence));
    }
    modelMatches &= model.Select(item => (item.Stroke, item.Sequence)).SequenceEqual(modelCanvas.Operations.Select(o => (o.StrokeId, o.Sequence)));
    modelMatches &= modelCanvas.DrawingVersion == sequence && modelCanvas.Snapshot().Players.All(p => p.Score == 0 && !p.HasGuessed);
}
Check(modelMatches, "six hundred deterministic mixed commands match independent canonical replay model");
Console.WriteLine($"SHARED CANVAS: {checks} checks passed");
