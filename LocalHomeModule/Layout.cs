using System.Text.Json;

namespace LocalHomeModule;

// Preserve the positional arguments used by old saves and callers.
public sealed record Placement(int Id, uint Item, float X, float Y, float Z, float Yaw, float Pitch = 0, float Roll = 0, float Scale = 1)
{
    public bool Valid => Id > 0 && Item > 0 && Item <= int.MaxValue && new[]{X,Y,Z,Yaw,Pitch,Roll}.All(v => float.IsFinite(v) && Math.Abs(v) <= 100000) && float.IsFinite(Scale) && Scale >= .01f && Scale <= 100f;

    public Placement Rounded()
    {
        static float Round(float v)
        {
            var rounded = (float)Math.Round((double)v, 2, MidpointRounding.AwayFromZero);
            return rounded == 0 ? 0f : rounded;
        }
        return this with { X = Round(X), Y = Round(Y), Z = Round(Z), Yaw = Round(Yaw), Pitch = Round(Pitch), Roll = Round(Roll), Scale = Round(Scale) };
    }
}

public sealed record Frame(string Kind, string Batch, int Count = 0, Placement? Item = null, string? Base = null, int Id = 0);
public sealed class SnapshotReceiver
{
    private string batch = "";
    private string? patchBase;
    private int count;
    private readonly Dictionary<int, Placement?> pending = new();
    private Dictionary<int, Placement> committed = new();
    // Retain terminal transaction IDs for the lifetime of this peer receiver. Replayed
    // snapshots must never replace a newer layout, including an empty one.
    private readonly HashSet<string> retired = new(StringComparer.OrdinalIgnoreCase);
    private DateTime lastActivity;
    public string? BaselineBatch { get; private set; }
    public bool NeedsSnapshot { get; private set; }

    private void Reset(bool needsSnapshot = false)
    {
        if (needsSnapshot && batch.Length != 0) NeedsSnapshot = true;
        if (batch.Length != 0) retired.Add(batch);
        batch = ""; patchBase = null; count = 0; pending.Clear();
    }
    private static bool Same(string? a, string? b) => string.Equals(a,b,StringComparison.OrdinalIgnoreCase);

    public Placement[]? Accept(Frame? f, DateTime now)
    {
        // Expiration is terminal even if a later caller supplies an earlier time.
        if (batch.Length != 0 && (now < lastActivity || now - lastActivity > TimeSpan.FromSeconds(30))) Reset(needsSnapshot:true);
        if (f == null) return null;
        if (!LayoutCodec.ValidFrame(f))
        {
            if (batch.Length != 0 && Same(f.Batch,batch)) Reset(needsSnapshot:true);
            return null;
        }
        if (f.Kind == "request" || retired.Contains(f.Batch)) return null;
        if (f.Kind is "begin" or "patch")
        {
            if (Same(f.Batch,batch))
            {
                // Duplicate headers neither discard operations nor refresh the deadline.
                if (f.Count != count || !Same(f.Base,patchBase)) Reset(needsSnapshot:true);
                return null;
            }
            if (f.Kind == "patch" && (BaselineBatch == null || !Same(f.Base,BaselineBatch)))
            {
                NeedsSnapshot = true;
                retired.Add(f.Batch);
                return null;
            }
            Reset();
            batch = f.Batch; patchBase = f.Base; count = f.Count; lastActivity = now;
            return null;
        }
        if (batch.Length == 0 || !Same(f.Batch,batch))
        {
            // A valid orphan operation/end proves that a transaction header was lost.
            // Preserve any other active transaction and request a full recovery now.
            NeedsSnapshot = true;
            retired.Add(f.Batch);
            return null;
        }
        if (f.Kind is "item" or "remove")
        {
            var p = f.Item?.Rounded();
            int id = p?.Id ?? f.Id;
            if (f.Kind == "remove" && (patchBase == null || !committed.ContainsKey(id))) { Reset(needsSnapshot:true); return null; }
            if (pending.TryGetValue(id, out var previous))
            {
                if (previous != p) Reset(needsSnapshot:true);
            }
            else if (pending.Count >= count) Reset(needsSnapshot:true);
            else { pending.Add(id,p); lastActivity = now; }
            return null;
        }
        if (pending.Count != count) { Reset(needsSnapshot:true); return null; }
        var next = patchBase == null ? new Dictionary<int,Placement>() : new Dictionary<int,Placement>(committed);
        foreach (var operation in pending)
            if (operation.Value == null) next.Remove(operation.Key); else next[operation.Key] = operation.Value;
        committed = next;
        BaselineBatch = batch;
        if (patchBase == null) NeedsSnapshot = false;
        var result = committed.Values.OrderBy(p=>p.Id).ToArray();
        Reset();
        return result;
    }
}
public static class LayoutCodec
{
    public const string Prefix = "LH2:";
    public const string LegacyPrefix = "LH1:";
    public static Placement[] Validate(Placement[]? values)
    {
        if (values == null || values.Any(p=>p == null || !p.Valid) || values.Select(p=>p.Id).Distinct().Count()!=values.Length)
            throw new InvalidDataException("布局数据无效（ID 唯一，坐标及角度有限，缩放须为 0.01 到 100）");
        return values.Select(p => p.Rounded()).ToArray();
    }
    private static string[] Serialize(IEnumerable<Frame> frames) => frames.Select(f=>Prefix+JsonSerializer.Serialize(f)).ToArray();
    public static string[] Encode(IEnumerable<Placement> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        var values=Validate(items.ToArray()); string batch=Guid.NewGuid().ToString("N");
        return Serialize(new[]{new Frame("begin",batch,values.Length)}.Concat(values.Select(p=>new Frame("item",batch,Item:p))).Append(new Frame("end",batch)));
    }
    public static string[] EncodeDelta(IEnumerable<Placement> before,IEnumerable<Placement> after,string baseBatch)
    {
        ArgumentNullException.ThrowIfNull(before); ArgumentNullException.ThrowIfNull(after);
        if (!ValidBatch(baseBatch)) throw new InvalidDataException("增量基线 GUID 无效");
        var old = Validate(before.ToArray()).ToDictionary(p=>p.Id);
        var next = Validate(after.ToArray()).ToDictionary(p=>p.Id);
        string batch=Guid.NewGuid().ToString("N");
        var operations=next.Values.Where(p=>!old.TryGetValue(p.Id,out var previous) || previous!=p).OrderBy(p=>p.Id).Select(p=>new Frame("item",batch,Item:p))
            .Concat(old.Keys.Where(id=>!next.ContainsKey(id)).OrderBy(id=>id).Select(id=>new Frame("remove",batch,Id:id))).ToArray();
        if (operations.Length==0) return Array.Empty<string>();
        return Serialize(new[]{new Frame("patch",batch,operations.Length,Base:baseBatch)}.Concat(operations).Append(new Frame("end",batch)));
    }
    private static bool ValidBatch(string? batch) => batch != null && batch.Length == 32 && Guid.TryParseExact(batch,"N",out _);
    internal static bool ValidFrame(Frame f) => ValidBatch(f.Batch) && (f.Kind switch
    {
        "begin" => f.Count >= 0 && f.Item == null && f.Base == null && f.Id == 0,
        "patch" => f.Count >= 0 && f.Item == null && ValidBatch(f.Base) && !string.Equals(f.Batch,f.Base,StringComparison.OrdinalIgnoreCase) && f.Id == 0,
        "item" => f.Count == 0 && f.Item is { Valid: true } && f.Base == null && f.Id == 0,
        "remove" => f.Count == 0 && f.Item == null && f.Base == null && f.Id > 0,
        "end" or "request" => f.Count == 0 && f.Item == null && f.Base == null && f.Id == 0,
        _ => false
    });
    private static bool UniqueObject(JsonElement value) => value.ValueKind == JsonValueKind.Object &&
        value.EnumerateObject().Select(p => p.Name).Distinct(StringComparer.Ordinal).Count() == value.EnumerateObject().Count();

    public static Frame? Decode(string? text)
    {
        if (text == null || text.Length > 1024 || !(text.StartsWith(Prefix,StringComparison.Ordinal) || text.StartsWith(LegacyPrefix,StringComparison.Ordinal))) return null;
        try
        {
            using var document = JsonDocument.Parse(text[Prefix.Length..]);
            var root = document.RootElement;
            if (!UniqueObject(root) || !root.TryGetProperty("Kind", out var kind) || kind.ValueKind != JsonValueKind.String ||
                !root.TryGetProperty("Batch", out var batch) || batch.ValueKind != JsonValueKind.String) return null;
            var count = 0;
            if (root.TryGetProperty("Count", out var countValue) &&
                (countValue.ValueKind != JsonValueKind.Number || !countValue.TryGetInt32(out count))) return null;
            if (kind.GetString() is "begin" or "patch" && !root.TryGetProperty("Count", out _)) return null;
            var id=0;
            if (root.TryGetProperty("Id",out var idValue) && (idValue.ValueKind!=JsonValueKind.Number || !idValue.TryGetInt32(out id))) return null;
            string? baseBatch=null;
            if (root.TryGetProperty("Base",out var baseValue) && baseValue.ValueKind!=JsonValueKind.Null)
            {
                if (baseValue.ValueKind!=JsonValueKind.String) return null;
                baseBatch=baseValue.GetString();
            }
            Placement? item = null;
            if (root.TryGetProperty("Item", out var itemValue) && itemValue.ValueKind != JsonValueKind.Null)
            {
                if (!UniqueObject(itemValue)) return null;
                foreach (var name in new[] { "Id", "Item", "X", "Y", "Z", "Yaw" })
                    if (!itemValue.TryGetProperty(name, out var field) || field.ValueKind != JsonValueKind.Number) return null;
                foreach (var name in new[] { "Pitch", "Roll", "Scale" })
                    if (itemValue.TryGetProperty(name, out var field) && field.ValueKind != JsonValueKind.Number) return null;
                item = itemValue.Deserialize<Placement>();
            }
            var frame = new Frame(kind.GetString()!, batch.GetString()!, count, item, baseBatch, id);
            return ValidFrame(frame) ? frame with { Item = item?.Rounded() } : null;
        }
        catch (JsonException) { return null; }
    }
}
