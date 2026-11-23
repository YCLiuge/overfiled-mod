using System.Text.Json;
using LocalHomeModule;

// Standalone assertions: only the .NET runtime and the linked production Layout.cs are used.
var tests = new List<(string Name, Action Run)>();
var time = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
const string batch = "0123456789abcdef0123456789abcdef";
const string other = "fedcba9876543210fedcba9876543210";
var p = new Placement(1, 42, 1, 2, 3, 90);
var q = new Placement(2, 43, -1, -2, -3, -90);
void Test(string name, Action run) => tests.Add((name, run));
void Check(bool condition, string message = "Assertion failed") { if (!condition) throw new Exception(message); }
void Reject(Action run)
{
    try { run(); }
    catch (InvalidDataException) { return; }
    throw new Exception("Expected InvalidDataException");
}
SnapshotReceiver Start(int count = 1)
{
    var r = new SnapshotReceiver();
    Check(r.Accept(new Frame("begin", batch, count), time) == null);
    return r;
}
Placement[]? End(SnapshotReceiver r, double seconds = 1) => r.Accept(new Frame("end", batch), time.AddSeconds(seconds));
string Json(Frame f) => LayoutCodec.Prefix + JsonSerializer.Serialize(f);
string ItemJson(string fields) => $"LH1:{{\"Kind\":\"item\",\"Batch\":\"{batch}\",\"Item\":{{{fields}}}}}";
string fields = "\"Id\":1,\"Item\":42,\"X\":1,\"Y\":2,\"Z\":3,\"Yaw\":90";

Test("complete snapshot commits only at end, sorted by ID", () => {
    var r = Start(2);
    Check(r.Accept(new Frame("item", batch, Item:q), time) == null);
    Check(r.Accept(new Frame("item", batch, Item:p), time) == null);
    Check(End(r)!.SequenceEqual(new[] {p, q}));
    Check(End(r) == null, "end replay must not commit twice");
});
Test("missing item is rejected and end discards pending state", () => {
    var r = Start(2); r.Accept(new Frame("item", batch, Item:p), time);
    Check(End(r) == null); r.Accept(new Frame("item", batch, Item:q), time);
    Check(End(r) == null);
});
Test("identical duplicate item is idempotent before and after capacity", () => {
    var r = Start(2);
    foreach (var item in new[] {p, p, q, p, q}) r.Accept(new Frame("item", batch, Item:item), time);
    Check(End(r)!.SequenceEqual(new[] {p, q}));
});
Test("conflicting duplicate invalidates whole batch", () => {
    var r = Start(); r.Accept(new Frame("item", batch, Item:p), time);
    r.Accept(new Frame("item", batch, Item:p with { X = 99 }), time);
    Check(End(r) == null);
});
Test("extra distinct item invalidates whole batch", () => {
    var r = Start(); r.Accept(new Frame("item", batch, Item:p), time);
    r.Accept(new Frame("item", batch, Item:q), time); Check(End(r) == null);
});
Test("empty snapshot commits empty array", () => Check(End(Start(0)) is { Length:0 }));
Test("empty snapshot with item is rejected", () => {
    var r = Start(0); r.Accept(new Frame("item", batch, Item:p), time); Check(End(r) == null);
});
Test("30 second deadline is inclusive", () => {
    var r = Start(); r.Accept(new Frame("item", batch, Item:p), time.AddSeconds(30));
    Check(End(r, 30)!.Single() == p);
});
Test("expired snapshot is rejected permanently", () => {
    var r = Start(); r.Accept(new Frame("item", batch, Item:p), time);
    Check(End(r, 30.001) == null); Check(End(r, 1) == null);
});
Test("expired item cannot revive snapshot", () => {
    var r = Start(); r.Accept(new Frame("item", batch, Item:p), time.AddSeconds(31));
    Check(End(r, 1) == null);
});
Test("clock rollback invalidates snapshot", () => {
    var r = Start(); r.Accept(new Frame("item", batch, Item:p), time.AddSeconds(-1)); Check(End(r) == null);
});
Test("other batch items and ends do not corrupt active batch", () => {
    var r = Start(); r.Accept(new Frame("item", other, Item:q), time);
    Check(r.Accept(new Frame("end", other), time) == null);
    r.Accept(new Frame("item", batch, Item:p), time); Check(End(r)!.Single() == p);
});
Test("new begin replaces incomplete old batch", () => {
    var r = Start(2); r.Accept(new Frame("item", batch, Item:p), time);
    r.Accept(new Frame("begin", other, 1), time);
    r.Accept(new Frame("item", batch, Item:q), time);
    r.Accept(new Frame("item", other, Item:q), time);
    Check(End(r) == null);
    Check(r.Accept(new Frame("end", other), time)!.Single() == q);
});
Test("duplicate begin preserves pending items", () => {
    var r = Start(); r.Accept(new Frame("item", batch, Item:p), time);
    r.Accept(new Frame("begin", batch, 1), time.AddSeconds(20)); Check(End(r, 21)!.Single() == p);
});
Test("duplicate begin cannot extend deadline", () => {
    var r = Start(); r.Accept(new Frame("item", batch, Item:p), time);
    r.Accept(new Frame("begin", batch, 1), time.AddSeconds(20)); Check(End(r, 31) == null);
});
Test("duplicate item cannot extend inactivity deadline", () => {
    var r = Start(); r.Accept(new Frame("item", batch, Item:p), time.AddSeconds(10));
    r.Accept(new Frame("item", batch, Item:p), time.AddSeconds(35));
    Check(End(r, 40.001) == null);
});
Test("1024 item batch remains active for hours while new items keep arriving", () => {
    var values = Enumerable.Range(1, 1024).Select(i => p with { Id = i }).ToArray();
    var r = Start(values.Length);
    foreach (var value in values)
        Check(r.Accept(new Frame("item", batch, Item:value), time.AddSeconds(value.Id * 20)) == null);
    Check(End(r, values.Length * 20 + 30)!.SequenceEqual(values));
});
Test("new item refreshes deadline but unrelated and null frames do not", () => {
    var r = Start(2);
    r.Accept(new Frame("item", batch, Item:p), time.AddSeconds(25));
    r.Accept(new Frame("item", batch, Item:q), time.AddSeconds(50));
    r.Accept(null, time.AddSeconds(70));
    r.Accept(new Frame("item", other, Item:p), time.AddSeconds(75));
    Check(End(r, 80.001) == null);
});
Test("clock rollback after new item activity invalidates batch", () => {
    var r = Start(); r.Accept(new Frame("item", batch, Item:p), time.AddSeconds(20));
    Check(End(r, 19) == null);
});
Test("conflicting begin invalidates batch", () => {
    var r = Start(); r.Accept(new Frame("item", batch, Item:p), time);
    r.Accept(new Frame("begin", batch, 0), time); Check(End(r) == null);
});
Test("frames before begin and null frame are ignored", () => {
    var r = new SnapshotReceiver(); Check(r.Accept(null, time) == null);
    Check(r.Accept(new Frame("item", batch, Item:p), time) == null); Check(End(r) == null);
});
Test("fresh batch succeeds after rejection", () => {
    var r = Start(2); Check(End(r) == null);
    r.Accept(new Frame("begin", other, 1), time); r.Accept(new Frame("item", other, Item:q), time);
    Check(r.Accept(new Frame("end", other), time)!.Single() == q);
});
Test("encode/decode/receive roundtrip with 1024 items and three rotation axes", () => {
    var values = Enumerable.Range(1, 1024).Select(i => p with { Id = i, X = -i, Yaw = i, Pitch = -i, Roll = i / 2f }).ToArray();
    var frames = LayoutCodec.Encode(values.Reverse()); Check(frames.Length == 1026);
    var r = new SnapshotReceiver(); Placement[]? result = null;
    foreach (var text in frames) { Check(text.Length <= 1024); var f = LayoutCodec.Decode(text); Check(f != null); result = r.Accept(f, time); }
    Check(result!.SequenceEqual(values));
});
Test("empty encode roundtrip", () => {
    var frames = LayoutCodec.Encode(Array.Empty<Placement>()); Check(frames.Length == 2);
    var r = new SnapshotReceiver(); Check(r.Accept(LayoutCodec.Decode(frames[0]), time) == null);
    Check(r.Accept(LayoutCodec.Decode(frames[1]), time) is { Length:0 });
});
Test("encode uses independent batches", () => {
    var a = LayoutCodec.Decode(LayoutCodec.Encode(new[] {p})[0])!;
    var b = LayoutCodec.Decode(LayoutCodec.Encode(new[] {p})[0])!; Check(a.Batch != b.Batch);
});
Test("validation allows inclusive coordinate and ID bounds", () => {
    var value = new Placement(int.MaxValue, int.MaxValue, -100000, 100000, 0, -100000, 100000, -100000);
    Check(LayoutCodec.Validate(new[] {value}).Single() == value);
    Check(LayoutCodec.Decode(Json(new Frame("item", batch, Item:value)))!.Item == value);
});
Test("Rounded rounds every coordinate and angle away from zero at exact decimal midpoints", () => {
    var value = p with { X = 1.125f, Y = 2.376f, Z = 3.124f, Yaw = 90.125f, Pitch = 12.376f, Roll = 35.124f };
    Check(value.Rounded() == new Placement(1, 42, 1.13f, 2.38f, 3.12f, 90.13f, 12.38f, 35.12f));
    // A float is widened to double before rounding: 1.005f is slightly below the decimal midpoint.
    Check((p with { X = 1.005f }).Rounded().X == 1f);
});
Test("Rounded handles negative coordinates and angles symmetrically", () => {
    var value = q with { X = -1.125f, Y = -2.376f, Z = -3.124f, Yaw = -90.125f, Pitch = -12.376f, Roll = -35.124f };
    Check(value.Rounded() == new Placement(2, 43, -1.13f, -2.38f, -3.12f, -90.13f, -12.38f, -35.12f));
});
Test("Rounded normalizes signed and tiny zeros on every axis to positive zero", () => {
    var negativeZero = BitConverter.Int32BitsToSingle(int.MinValue);
    foreach (var zero in new[] { negativeZero, -0.004f, 0.004f, 0f }) {
        var value = new Placement(1, 42, zero, zero, zero, zero, zero, zero).Rounded();
        foreach (var axis in new[] { value.X, value.Y, value.Z, value.Yaw, value.Pitch, value.Roll })
            Check(BitConverter.SingleToInt32Bits(axis) == 0, "zero must have a positive sign bit");
    }
});
Test("rounding is idempotent across positive negative and boundary values", () => {
    foreach (var coordinate in new[] { -100000f, -99999.99f, -90.125f, -1.005f, -0.004f, 0f, 0.004f, 1.005f, 90.125f, 99999.99f, 100000f }) {
        var value = new Placement(1, 42, coordinate, coordinate, coordinate, coordinate, coordinate, coordinate);
        var rounded = value.Rounded();
        Check(rounded.Rounded() == rounded);
        Check(LayoutCodec.Validate(LayoutCodec.Validate(new[] {value})).Single() == rounded);
    }
});
Test("Validate returns a fresh rounded array without mutating original placements", () => {
    var value = p with { X = 1.125f, Y = -2.376f, Pitch = -12.125f, Roll = 35.124f };
    var input = new[] { value };
    var result = LayoutCodec.Validate(input);
    Check(!ReferenceEquals(input, result));
    Check(ReferenceEquals(input[0], value) && input[0].X == 1.125f && input[0].Pitch == -12.125f);
    Check(result.Single() == value.Rounded());
    result[0] = q;
    Check(input.Single() == value);
});
Test("save and protocol roundtrips converge on the same rounded placement", () => {
    var value = p with { X = 1.125f, Y = -2.376f, Z = -0.004f, Yaw = -90.125f, Pitch = 12.376f, Roll = -35.124f };
    var saved = LayoutCodec.Validate(JsonSerializer.Deserialize<Placement[]>(JsonSerializer.Serialize(new[] {value}))!);
    var expected = value.Rounded();
    Check(saved.Single() == expected);
    var archive = JsonSerializer.Serialize(saved);
    Check(!archive.Contains("-0"));
    Check(LayoutCodec.Validate(JsonSerializer.Deserialize<Placement[]>(archive)).Single() == expected);
    var r = new SnapshotReceiver(); Placement[]? result = null;
    foreach (var text in LayoutCodec.Encode(new[] {value})) result = r.Accept(LayoutCodec.Decode(text), time);
    Check(result!.Single() == expected);
    // Decode must normalize incoming raw item JSON even when the sender has not used Encode.
    Check(LayoutCodec.Decode(Json(new Frame("item", batch, Item:value)))!.Item == expected);
    Check(value.X == 1.125f && value.Z == -0.004f);
});
Test("validation and Decode reject raw invalid values before normalization", () => {
    foreach (var invalid in new[] {
        p with {Id=0, X=0.004f}, p with {Item=0, X=0.004f},
        p with {X=MathF.BitIncrement(100000f)}, p with {Y=MathF.BitDecrement(-100000f)},
        p with {Z=MathF.BitIncrement(100000f)}, p with {Yaw=MathF.BitDecrement(-100000f)},
        p with {Pitch=MathF.BitIncrement(100000f)}, p with {Roll=MathF.BitDecrement(-100000f)}
    }) {
        Check(!invalid.Valid);
        Reject(() => LayoutCodec.Validate(new[] {invalid}));
        Reject(() => LayoutCodec.Encode(new[] {invalid}));
        Check(LayoutCodec.Decode(Json(new Frame("item", batch, Item:invalid))) == null);
    }
    Reject(() => LayoutCodec.Validate(new[] {p with {X=float.NaN}}));
    Reject(() => LayoutCodec.Validate(new[] {p with {Roll=float.PositiveInfinity}}));
    Reject(() => LayoutCodec.Validate(new[] {p with {X=0.001f}, p with {X=0.002f}}));
});
Test("request frame serialization and Decode roundtrip", () => {
    var request = new Frame("request", batch);
    Check(LayoutCodec.ValidFrame(request));
    var encoded = Json(request);
    Check(encoded.StartsWith(LayoutCodec.Prefix, StringComparison.Ordinal));
    Check(LayoutCodec.Decode(encoded) == request);
    Check(LayoutCodec.Decode($"LH1:{{\"Kind\":\"request\",\"Batch\":\"{batch}\"}}") == request);
});
Test("request never commits or discards a pending snapshot or refreshes its deadline", () => {
    var r = Start(); r.Accept(new Frame("item", batch, Item:p), time);
    Check(r.Accept(new Frame("request", batch), time.AddSeconds(20)) == null);
    Check(End(r, 21)!.Single() == p);
    var expired = Start(); expired.Accept(new Frame("item", batch, Item:p), time);
    Check(expired.Accept(new Frame("request", batch), time.AddSeconds(20)) == null);
    Check(End(expired, 31) == null);
});
Test("validation rejects null collection, null entry and duplicate IDs", () => {
    Reject(() => LayoutCodec.Validate(null)); Reject(() => LayoutCodec.Validate(new Placement[] {null!}));
    Reject(() => LayoutCodec.Validate(new[] {p, p}));
    Reject(() => LayoutCodec.Encode(new[] {p, p}));
});
Test("begin accepts every nonnegative Int32 boundary without count allocation", () => {
    foreach (var count in new[] { 0, 1, 256, 257, 1024, int.MaxValue }) {
        var frame = new Frame("begin", batch, count);
        Check(LayoutCodec.Decode(Json(frame)) == frame);
        var r = new SnapshotReceiver(); r.Accept(frame, time);
        // Changed counts invalidate a live batch, proving even Int32.MaxValue began successfully.
        r.Accept(new Frame("begin", batch, count == 0 ? 1 : 0), time);
        Check(End(r) == null);
    }
});
Test("old saves and LH1 item frames default optional axes to zero", () => {
    Check(new Placement(1, 42, 1, 2, 3, 90) == p);
    var saved = JsonSerializer.Deserialize<Placement>("{" + fields + "}")!;
    Check(saved == p && saved.Yaw == 90 && saved.Pitch == 0 && saved.Roll == 0);
    Check(LayoutCodec.Decode(ItemJson(fields))!.Item == saved);
    Check(LayoutCodec.Decode(ItemJson(fields + ",\"Pitch\":12.5"))!.Item == p with { Pitch = 12.5f });
    Check(LayoutCodec.Decode(ItemJson(fields + ",\"Roll\":-25"))!.Item == p with { Roll = -25 });
});
Test("three axis save serialization roundtrip", () => {
    var value = p with { Pitch = -12.5f, Roll = 35.75f };
    Check(JsonSerializer.Deserialize<Placement>(JsonSerializer.Serialize(value)) == value);
});
foreach (var invalid in new[] {
    p with {Id=0}, p with {Id=-1}, p with {Item=0}, p with {Item=uint.MaxValue},
    p with {X=float.NaN}, p with {Y=float.PositiveInfinity}, p with {Z=float.NegativeInfinity}, p with {Yaw=float.NaN},
    p with {X=100001}, p with {Y=-100001}, p with {Z=100001}, p with {Yaw=-100001},
    p with {Pitch=float.NaN}, p with {Pitch=float.PositiveInfinity}, p with {Pitch=float.NegativeInfinity},
    p with {Roll=float.NaN}, p with {Roll=float.PositiveInfinity}, p with {Roll=float.NegativeInfinity},
    p with {Pitch=100001}, p with {Pitch=-100001}, p with {Roll=100001}, p with {Roll=-100001}
}) {
    var value = invalid;
    Test($"invalid placement {tests.Count}: rejected by validation and receiver", () => {
        Check(!value.Valid); Reject(() => LayoutCodec.Validate(new[] {value}));
        var r = Start(); r.Accept(new Frame("item", batch, Item:p), time);
        r.Accept(new Frame("item", batch, Item:value), time); Check(End(r) == null);
    });
}
foreach (var invalid in new[] {
    new Frame("begin", batch, -1), new Frame("begin", batch, int.MinValue), new Frame("begin", batch, Item:p),
    new Frame("item", batch), new Frame("item", batch, 1, p), new Frame("end", batch, 1),
    new Frame("end", batch, Item:p), new Frame("request", batch, 1), new Frame("request", batch, -1),
    new Frame("request", batch, Item:p), new Frame("request", "short"), new Frame("unknown", batch), new Frame(null!, batch),
    new Frame("begin", null!), new Frame("begin", ""), new Frame("begin", new string('x',32)), new Frame("begin", "short")
}) {
    var value = invalid;
    Test($"invalid frame {tests.Count}: decode and direct receiver reject safely", () => {
        Check(LayoutCodec.Decode(Json(value)) == null);
        var r = new SnapshotReceiver(); Check(r.Accept(value, time) == null); Check(End(r) == null);
    });
}
var invalidJson = new List<string?> {
    null, "", "{}", "lh1:{}", " LH1:{}", "LH1:", "LH1:{", "LH1:null", "LH1:[]", "LH1:42", "LH1:\"text\"",
    "LH1:{\"Kind\":\"begin\"}", $"LH1:{{\"Batch\":\"{batch}\"}}",
    $"LH1:{{\"Kind\":\"begin\",\"Batch\":\"{batch}\"}}",
    $"LH1:{{\"Kind\":\"begin\",\"Batch\":\"{batch}\",\"Count\":null}}",
    $"LH1:{{\"Kind\":\"begin\",\"Batch\":\"{batch}\",\"Count\":1.5}}",
    $"LH1:{{\"Kind\":\"begin\",\"Batch\":\"{batch}\",\"Count\":2147483648}}",
    $"LH1:{{\"Kind\":\"begin\",\"Batch\":\"{batch}\",\"Count\":\"1\"}}",
    $"LH1:{{\"Kind\":\"end\",\"Kind\":\"begin\",\"Batch\":\"{batch}\",\"Count\":0}}",
    ItemJson(fields + ",\"Id\":2"), ItemJson(fields.Replace("\"Id\":1", "\"Id\":2147483648")),
    ItemJson(fields.Replace("\"Item\":42", "\"Item\":-1")), ItemJson(fields.Replace("\"X\":1", "\"X\":1e999")),
    ItemJson(fields.Replace("\"X\":1", "\"X\":\"NaN\"")), ItemJson(fields.Replace("\"X\":1", "\"X\":null")),
    ItemJson(fields.Replace("\"Id\":1", "\"Id\":1.5")), ItemJson(fields.Replace("\"Item\":42", "\"Item\":0")),
    ItemJson(fields.Replace("\"Yaw\":90", "\"Yaw\":100001")),
    Json(new Frame("begin", batch, 0)) + " trailing", "LH1:" + new string(' ',1021)
};
foreach (var axis in new[] { "Pitch", "Roll" })
    foreach (var value in new[] { "null", "\"0\"", "true", "[]", "{}", "\"NaN\"", "\"Infinity\"", "1e999", "100001", "-100001" })
        invalidJson.Add(ItemJson(fields + $",\"{axis}\":{value}"));
invalidJson.Add(ItemJson(fields + ",\"Pitch\":1,\"Pitch\":2"));
invalidJson.Add(ItemJson(fields + ",\"Roll\":1,\"Roll\":2"));
foreach (var field in fields.Split(',')) invalidJson.Add(ItemJson(string.Join(',', fields.Split(',').Where(x => x != field))));
foreach (var text in invalidJson) {
    var input = text;
    Test($"invalid JSON {tests.Count}: returns null without exception", () => Check(LayoutCodec.Decode(input) == null));
}
Test("minimal valid frames decode", () => {
    Check(LayoutCodec.Decode($"LH1:{{\"Kind\":\"begin\",\"Batch\":\"{batch}\",\"Count\":1}}") != null);
    Check(LayoutCodec.Decode(ItemJson(fields))!.Item == p);
    Check(LayoutCodec.Decode($"LH1:{{\"Kind\":\"end\",\"Batch\":\"{batch}\"}}") != null);
});
Test("frame length 1024 is allowed and 1025 rejected", () => {
    var json = Json(new Frame("end", batch));
    Check(LayoutCodec.Decode(json.PadRight(1024)) != null); Check(LayoutCodec.Decode(json.PadRight(1025)) == null);
});

Placement[]? Deliver(SnapshotReceiver receiver, IEnumerable<string> frames, double seconds=0)
{
    Placement[]? result=null;
    foreach(var text in frames) {
        var frame=LayoutCodec.Decode(text); Check(frame!=null,"Encoded frame must decode");
        var committed=receiver.Accept(frame,time.AddSeconds(seconds));
        if(committed!=null) result=committed;
    }
    return result;
}
Test("old saves and LH1 snapshots default Scale to one",()=>{
    Check(JsonSerializer.Deserialize<Placement>("{"+fields+"}")!.Scale==1);
    Check(LayoutCodec.Decode(ItemJson(fields))!.Item!.Scale==1);
    var legacy=new[]{ $"LH1:{{\"Kind\":\"begin\",\"Batch\":\"{batch}\",\"Count\":1}}",ItemJson(fields),$"LH1:{{\"Kind\":\"end\",\"Batch\":\"{batch}\"}}" };
    Check(Deliver(new SnapshotReceiver(),legacy)!.Single()==p);
});
Test("Scale inclusive bounds and rounding survive persistence and wire",()=>{
    foreach(var scale in new[]{.01f,1f,1.125f,99.999f,100f}) {
        var value=p with {Scale=scale}; Check(value.Valid);
        var rounded=LayoutCodec.Validate(new[]{value}).Single();
        Check(rounded.Scale==(scale==1.125f ? 1.13f : scale==99.999f ? 100f : scale));
        Check(JsonSerializer.Deserialize<Placement>(JsonSerializer.Serialize(rounded))==rounded);
        Check(Deliver(new SnapshotReceiver(),LayoutCodec.Encode(new[]{value}))!.Single()==rounded);
    }
});
foreach(var scale in new[]{0f,-1f,.00999f,MathF.BitDecrement(.01f),100.001f,MathF.BitIncrement(100f),float.NaN,float.NegativeInfinity,float.PositiveInfinity}) {
    var invalidScale=scale;
    Test($"Scale {scale} rejected before rounding",()=>{
        var value=p with {Scale=invalidScale}; Check(!value.Valid);
        Reject(()=>LayoutCodec.Validate(new[]{value})); Reject(()=>LayoutCodec.EncodeDelta(new[]{p},new[]{value},batch));
        var r=Start(); r.Accept(new Frame("item",batch,Item:value),time); Check(End(r)==null);
        if(float.IsFinite(invalidScale)) Check(LayoutCodec.Decode(ItemJson(fields+",\"Scale\":"+invalidScale.ToString(System.Globalization.CultureInfo.InvariantCulture)))==null);
    });
}
Test("malformed Scale JSON is rejected",()=>{
    foreach(var value in new[]{"null","\"1\"","true","{}","[]","1e999"}) Check(LayoutCodec.Decode(ItemJson(fields+",\"Scale\":"+value))==null);
    Check(LayoutCodec.Decode(ItemJson(fields+",\"Scale\":1,\"Scale\":2"))==null);
});
Test("delta sends only one changed item and atomically returns entire layout",()=>{
    var r=new SnapshotReceiver(); var before=new[]{p,q}; var full=LayoutCodec.Encode(before); Deliver(r,full);
    var changed=p with {Scale=2.125f}; var patch=LayoutCodec.EncodeDelta(before,new[]{changed,q},r.BaselineBatch!);
    Check(patch.Length==3); var header=LayoutCodec.Decode(patch[0])!; Check(header.Kind=="patch" && header.Count==1);
    Check(LayoutCodec.Decode(patch[1])!.Item==changed.Rounded());
    Check(r.Accept(header,time)==null); Check(r.Accept(LayoutCodec.Decode(patch[1]),time)==null);
    Check(r.BaselineBatch==LayoutCodec.Decode(full[0])!.Batch);
    Check(r.Accept(LayoutCodec.Decode(patch[2]),time)!.SequenceEqual(new[]{changed.Rounded(),q}));
    Check(r.BaselineBatch==header.Batch && !r.NeedsSnapshot);
});
Test("delta handles deletion and addition as one atomic transaction",()=>{
    var r=new SnapshotReceiver(); Deliver(r,LayoutCodec.Encode(new[]{p,q})); var added=p with {Id=3};
    var patch=LayoutCodec.EncodeDelta(new[]{p,q},new[]{q,added},r.BaselineBatch!);
    Check(patch.Length==4); Check(LayoutCodec.Decode(patch[0])!.Count==2);
    Check(patch.Select(LayoutCodec.Decode).Single(f=>f!.Kind=="remove")!.Id==p.Id);
    Check(Deliver(r,patch)!.SequenceEqual(new[]{q,added}));
});
Test("delta ignores reordering and differences below rounding precision",()=>{
    Check(LayoutCodec.EncodeDelta(new[]{p,q},new[]{q,p},batch).Length==0);
    Check(LayoutCodec.EncodeDelta(new[]{p},new[]{p with {X=1.001f,Scale=1.001f}},batch).Length==0);
    Check(LayoutCodec.EncodeDelta(Array.Empty<Placement>(),Array.Empty<Placement>(),batch).Length==0);
    Reject(()=>LayoutCodec.EncodeDelta(new[]{p},new[]{q},"bad"));
    Reject(()=>LayoutCodec.EncodeDelta(new[]{p,p},new[]{q},batch));
});
Test("empty committed layouts retain baseline for subsequent additions",()=>{
    var r=new SnapshotReceiver(); Deliver(r,LayoutCodec.Encode(new[]{p})); var initial=r.BaselineBatch!;
    var removal=LayoutCodec.EncodeDelta(new[]{p},Array.Empty<Placement>(),initial); Check(Deliver(r,removal) is {Length:0});
    Check(r.BaselineBatch==LayoutCodec.Decode(removal[0])!.Batch);
    Check(Deliver(r,LayoutCodec.EncodeDelta(Array.Empty<Placement>(),new[]{q},r.BaselineBatch!))!.Single()==q);
    var empty=new SnapshotReceiver(); Deliver(empty,LayoutCodec.Encode(Array.Empty<Placement>()));
    Check(Deliver(empty,LayoutCodec.EncodeDelta(Array.Empty<Placement>(),new[]{p},empty.BaselineBatch!))!.Single()==p);
});
Test("missing initial baseline requests full snapshot then recovers",()=>{
    var r=new SnapshotReceiver(); var delta=LayoutCodec.EncodeDelta(new[]{p},new[]{q},batch);
    Check(Deliver(r,delta)==null && r.NeedsSnapshot && r.BaselineBatch==null);
    Check(Deliver(r,LayoutCodec.Encode(new[]{q}))!.Single()==q); Check(!r.NeedsSnapshot);
    Check(Deliver(r,LayoutCodec.EncodeDelta(new[]{q},new[]{p},r.BaselineBatch!))!.Single()==p);
});
Test("lost delta transaction triggers recovery on the next base mismatch",()=>{
    var r=new SnapshotReceiver(); Deliver(r,LayoutCodec.Encode(new[]{p})); var baseline=r.BaselineBatch!;
    var next=p with {X=10}; var missing=LayoutCodec.EncodeDelta(new[]{p},new[]{next},baseline);
    var third=LayoutCodec.EncodeDelta(new[]{next},new[]{q},LayoutCodec.Decode(missing[0])!.Batch);
    Check(Deliver(r,third)==null && r.NeedsSnapshot && r.BaselineBatch==baseline);
    Check(Deliver(r,LayoutCodec.Encode(new[]{q}))!.Single()==q && !r.NeedsSnapshot);
});
foreach(var loss in new[]{"header","operation","end","timeout","conflict"}) {
    var mode=loss;
    Test($"incomplete delta {mode} preserves committed state until full recovery",()=>{
        var r=new SnapshotReceiver(); Deliver(r,LayoutCodec.Encode(new[]{p,q})); var baseline=r.BaselineBatch!;
        var next=p with {Scale=2}; var patch=LayoutCodec.EncodeDelta(new[]{p,q},new[]{next,q},baseline);
        if(mode!="header") r.Accept(LayoutCodec.Decode(patch[0]),time);
        if(mode!="operation") r.Accept(LayoutCodec.Decode(patch[1]),time);
        if(mode=="conflict") r.Accept(new Frame("item",LayoutCodec.Decode(patch[0])!.Batch,Item:p),time);
        if(mode!="end") Check(r.Accept(LayoutCodec.Decode(patch[^1]),time.AddSeconds(mode=="timeout" ? 31 : 0))==null);
        Check(r.BaselineBatch==baseline);
        Check(r.NeedsSnapshot==(mode is "header" or "operation" or "timeout" or "conflict"));
        if(mode=="end") {
            r.Accept(null,time.AddSeconds(31));
            Check(r.NeedsSnapshot,"Tick detects the lost end even without subsequent traffic");
        }
        var subsequent=LayoutCodec.EncodeDelta(new[]{next,q},new[]{q},LayoutCodec.Decode(patch[0])!.Batch);
        Check(Deliver(r,subsequent,32)==null && r.NeedsSnapshot);
        Check(Deliver(r,LayoutCodec.Encode(new[]{next,q}),33)!.SequenceEqual(new[]{next,q}) && !r.NeedsSnapshot);
        Check(Deliver(r,patch,34)==null,"Old failed delta must not roll back recovery");
    });
}
Test("duplicate completed snapshots and patches cannot roll back newer state",()=>{
    var r=new SnapshotReceiver(); var full=LayoutCodec.Encode(new[]{p}); Deliver(r,full);
    var patch=LayoutCodec.EncodeDelta(new[]{p},new[]{q},r.BaselineBatch!); Deliver(r,patch);
    var second=LayoutCodec.EncodeDelta(new[]{q},Array.Empty<Placement>(),r.BaselineBatch!); Deliver(r,second); var baseline=r.BaselineBatch;
    Check(Deliver(r,full)==null); Check(Deliver(r,patch)==null); Check(Deliver(r,second)==null);
    Check(r.BaselineBatch==baseline && !r.NeedsSnapshot);
});
Test("duplicate patch header and operations preserve atomic progress",()=>{
    var r=new SnapshotReceiver(); Deliver(r,LayoutCodec.Encode(new[]{p,q}));
    var patch=LayoutCodec.EncodeDelta(new[]{p,q},new[]{q with {Scale=3}},r.BaselineBatch!);
    r.Accept(LayoutCodec.Decode(patch[0]),time); r.Accept(LayoutCodec.Decode(patch[1]),time);
    r.Accept(LayoutCodec.Decode(patch[0]),time.AddSeconds(20)); r.Accept(LayoutCodec.Decode(patch[1]),time.AddSeconds(20));
    r.Accept(LayoutCodec.Decode(patch[2]),time.AddSeconds(21)); r.Accept(LayoutCodec.Decode(patch[2]),time.AddSeconds(22));
    Check(r.Accept(LayoutCodec.Decode(patch[^1]),time.AddSeconds(23))!.Single()==q with {Scale=3});
});
Test("duplicate patch traffic does not extend deadline",()=>{
    var r=new SnapshotReceiver(); Deliver(r,LayoutCodec.Encode(new[]{p})); var baseline=r.BaselineBatch;
    var patch=LayoutCodec.EncodeDelta(new[]{p},new[]{q},baseline!);
    r.Accept(LayoutCodec.Decode(patch[0]),time); r.Accept(LayoutCodec.Decode(patch[1]),time);
    r.Accept(LayoutCodec.Decode(patch[0]),time.AddSeconds(20)); r.Accept(LayoutCodec.Decode(patch[1]),time.AddSeconds(20));
    r.Accept(LayoutCodec.Decode(patch[2]),time.AddSeconds(31)); Check(r.Accept(LayoutCodec.Decode(patch[^1]),time.AddSeconds(31))==null);
    Check(r.BaselineBatch==baseline);
});
Test("conflicting remove/item and unknown deletion invalidate patch",()=>{
    foreach(var unknown in new[]{false,true}) {
        var r=new SnapshotReceiver(); Deliver(r,LayoutCodec.Encode(new[]{p})); var baseline=r.BaselineBatch;
        r.Accept(new Frame("patch",other,2,Base:baseline),time);
        r.Accept(new Frame("remove",other,Id:unknown ? 999 : p.Id),time);
        r.Accept(new Frame("item",other,Item:p),time);
        Check(r.Accept(new Frame("end",other),time)==null && r.BaselineBatch==baseline);
    }
});
Test("patch and removal frame fields are strictly validated",()=>{
    foreach(var f in new[]{new Frame("patch",batch,1),new Frame("patch",batch,-1,Base:other),new Frame("patch",batch,1,Base:batch),new Frame("patch",batch,1,p,other),new Frame("remove",batch),new Frame("remove",batch,Id:-1),new Frame("remove",batch,1,Id:1),new Frame("remove",batch,Item:p,Id:1),new Frame("end",batch,Base:other),new Frame("item",batch,Item:p,Id:1)}) Check(LayoutCodec.Decode(Json(f))==null);
    foreach(var extra in new[]{"\"Base\":123","\"Base\":true","\"Id\":1.5","\"Id\":null"}) Check(LayoutCodec.Decode($"LH2:{{\"Kind\":\"end\",\"Batch\":\"{batch}\",{extra}}}")==null);
});

Test("damaged operation decoded as null cannot partially commit",()=>{
    var r=new SnapshotReceiver(); Deliver(r,LayoutCodec.Encode(new[]{p,q})); var baseline=r.BaselineBatch!;
    var patch=LayoutCodec.EncodeDelta(new[]{p,q},new[]{p with {Scale=4}},baseline);
    r.Accept(LayoutCodec.Decode(patch[0]),time);
    var broken=patch[1].Replace("\"Scale\":4","\"Scale\":null");
    Check(LayoutCodec.Decode(broken)==null); r.Accept(LayoutCodec.Decode(broken),time);
    r.Accept(LayoutCodec.Decode(patch[2]),time); Check(r.Accept(LayoutCodec.Decode(patch[^1]),time)==null);
    Check(r.BaselineBatch==baseline && r.NeedsSnapshot);
    Check(Deliver(r,LayoutCodec.EncodeDelta(new[]{p,q},new[]{q},baseline))!.Single()==q && r.NeedsSnapshot);
});
Test("only completed full snapshots clear a recovery request",()=>{
    var r=new SnapshotReceiver(); Deliver(r,LayoutCodec.Encode(new[]{p})); var baseline=r.BaselineBatch!;
    Deliver(r,LayoutCodec.EncodeDelta(new[]{p},new[]{q},other)); Check(r.NeedsSnapshot);
    Check(Deliver(r,LayoutCodec.EncodeDelta(new[]{p},new[]{q},baseline))!.Single()==q && r.NeedsSnapshot);
    var recovery=LayoutCodec.Encode(new[]{p,q}); r.Accept(LayoutCodec.Decode(recovery[0]),time);
    r.Accept(LayoutCodec.Decode(recovery[1]),time); Check(r.Accept(LayoutCodec.Decode(recovery[^1]),time)==null && r.NeedsSnapshot);
    Check(Deliver(r,LayoutCodec.Encode(Array.Empty<Placement>())) is {Length:0} && !r.NeedsSnapshot);
});
Test("missing-base patches do not disrupt a valid full snapshot in progress",()=>{
    var r=Start(); r.Accept(new Frame("item",batch,Item:p),time);
    r.Accept(new Frame("patch",other,1,Base:Guid.NewGuid().ToString("N")),time);
    Check(r.NeedsSnapshot); Check(End(r)!.Single()==p && !r.NeedsSnapshot);
});
Test("single deletion and single addition encode exactly one operation",()=>{
    var deletion=LayoutCodec.EncodeDelta(new[]{p,q},new[]{q},batch);
    Check(deletion.Length==3 && LayoutCodec.Decode(deletion[1]) is {Kind:"remove",Id:1});
    var addition=LayoutCodec.EncodeDelta(new[]{q},new[]{p,q},batch);
    Check(addition.Length==3 && LayoutCodec.Decode(addition[1]) is {Kind:"item",Item:not null});
});

Test("idle tick requests recovery for incomplete full and patch transactions",()=>{
    foreach(var patchMode in new[]{false,true}) {
        var r=new SnapshotReceiver(); Deliver(r,LayoutCodec.Encode(new[]{p})); var baseline=r.BaselineBatch;
        var frames=patchMode ? LayoutCodec.EncodeDelta(new[]{p},new[]{q},baseline!) : LayoutCodec.Encode(new[]{q});
        r.Accept(LayoutCodec.Decode(frames[0]),time); r.Accept(LayoutCodec.Decode(frames[1]),time);
        Check(r.Accept(null,time.AddSeconds(30))==null && !r.NeedsSnapshot);
        Check(r.Accept(null,time.AddSeconds(30.001))==null && r.NeedsSnapshot && r.BaselineBatch==baseline);
        Check(r.Accept(LayoutCodec.Decode(frames[^1]),time.AddSeconds(31))==null && r.NeedsSnapshot);
        Check(Deliver(r,LayoutCodec.Encode(new[]{q}),32)!.Single()==q && !r.NeedsSnapshot);
    }
});
Test("broken active frame immediately requests recovery but unrelated invalid frames do not",()=>{
    var r=Start();
    r.Accept(new Frame("item",other),time); Check(!r.NeedsSnapshot);
    r.Accept(new Frame("item",batch,Item:p with {Scale=0}),time);
    Check(r.NeedsSnapshot && End(r)==null);
    var idle=new SnapshotReceiver(); idle.Accept(new Frame("item",batch),time); idle.Accept(null,time.AddSeconds(31));
    Check(!idle.NeedsSnapshot);
});
Test("conflicts and premature end request recovery immediately",()=>{
    foreach(var failure in new[]{"header","duplicate","overflow","end","remove"}) {
        var r=Start(); r.Accept(new Frame("item",batch,Item:p),time);
        var broken=failure switch {
            "header"=>new Frame("begin",batch,2),
            "duplicate"=>new Frame("item",batch,Item:p with {X=5}),
            "overflow"=>new Frame("item",batch,Item:q),
            "remove"=>new Frame("remove",batch,Id:p.Id),
            _=>new Frame("end",batch)
        };
        if(failure=="end") { r=Start(2); r.Accept(new Frame("item",batch,Item:p),time); }
        r.Accept(broken,time); Check(r.NeedsSnapshot,$"{failure} must request recovery");
    }
});
Test("successful commits and replacement headers do not create recovery requests",()=>{
    var r=Start(2); r.Accept(new Frame("item",batch,Item:p),time);
    var full=LayoutCodec.Encode(new[]{q}); r.Accept(LayoutCodec.Decode(full[0]),time); Check(!r.NeedsSnapshot);
    Check(Deliver(r,full.Skip(1))!.Single()==q && !r.NeedsSnapshot);
    Check(Deliver(r,LayoutCodec.EncodeDelta(new[]{q},new[]{p},r.BaselineBatch!))!.Single()==p && !r.NeedsSnapshot);
    Check(Deliver(r,LayoutCodec.Encode(Array.Empty<Placement>())) is {Length:0} && !r.NeedsSnapshot);
});

Test("lost header with no subsequent editing immediately requests recovery",()=>{
    foreach(var orphanKind in new[]{"item","remove","end"}) {
        foreach(var initialSnapshot in new[]{false,true}) {
            var r=new SnapshotReceiver(); if(initialSnapshot) Deliver(r,LayoutCodec.Encode(new[]{p}));
            var baseline=r.BaselineBatch;
            var orphan=orphanKind switch {
                "item"=>new Frame("item",other,Item:q),
                "remove"=>new Frame("remove",other,Id:p.Id),
                _=>new Frame("end",other)
            };
            Check(r.Accept(orphan,time)==null && r.NeedsSnapshot && r.BaselineBatch==baseline);
            r.Accept(null,time.AddSeconds(1)); Check(r.NeedsSnapshot);
            Check(Deliver(r,LayoutCodec.Encode(new[]{q}),2)!.Single()==q && !r.NeedsSnapshot);
            Check(r.Accept(orphan,time.AddSeconds(3))==null && !r.NeedsSnapshot,"Retired orphan repeats must not trigger recovery again");
        }
    }
});
Test("unknown orphan preserves active transaction and its deadline",()=>{
    var r=new SnapshotReceiver(); Deliver(r,LayoutCodec.Encode(new[]{p}));
    var patch=LayoutCodec.EncodeDelta(new[]{p},new[]{q},r.BaselineBatch!);
    foreach(var text in patch.SkipLast(1)) r.Accept(LayoutCodec.Decode(text),time);
    r.Accept(new Frame("remove",other,Id:999),time.AddSeconds(20)); Check(r.NeedsSnapshot);
    Check(r.Accept(LayoutCodec.Decode(patch[^1]),time.AddSeconds(21))!.Single()==q && r.NeedsSnapshot);
    // The orphan is a different transaction. Its traffic cannot refresh this one.
    var full=LayoutCodec.Encode(new[]{p}); r.Accept(LayoutCodec.Decode(full[0]),time.AddSeconds(22));
    r.Accept(new Frame("item",Guid.NewGuid().ToString("N"),Item:q),time.AddSeconds(45));
    r.Accept(null,time.AddSeconds(53));
    Check(r.Accept(LayoutCodec.Decode(full[1]),time.AddSeconds(54))==null);
    Check(r.Accept(LayoutCodec.Decode(full[^1]),time.AddSeconds(54))==null && r.NeedsSnapshot);
});

var failures = 0;
foreach (var (name, run) in tests) {
    try { run(); Console.WriteLine($"PASS {name}"); }
    catch (Exception e) { failures++; Console.Error.WriteLine($"FAIL {name}: {e}"); }
}
Console.WriteLine($"LocalHomeVerification: {tests.Count - failures}/{tests.Count} passed; {failures} failed.");
return failures == 0 ? 0 : 1;
