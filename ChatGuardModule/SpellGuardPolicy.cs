namespace ChatGuardModule;

public sealed class SpellGuardOptions
{
    public bool Enabled { get; set; } = true;
    public bool BlockAllRemote { get; set; }
    public int UnitBurst { get; set; } = 12;
    public double UnitPerSecond { get; set; } = 6;
    public int GlobalBurst { get; set; } = 80;
    public double GlobalPerSecond { get; set; } = 40;
    public void Validate()
    {
        if (UnitBurst is < 1 or > 500 || GlobalBurst is < 1 or > 2000 ||
            !double.IsFinite(UnitPerSecond) || UnitPerSecond is <= 0 or > 500 ||
            !double.IsFinite(GlobalPerSecond) || GlobalPerSecond is <= 0 or > 2000)
            throw new ArgumentException("技能保护配置超出允许范围");
    }
}

// Independent budget: spell traffic must never consume chat tokens.
public sealed class SpellGuardPolicy
{
    private sealed class Bucket { public double Tokens, At, Seen; }
    private readonly object sync = new();
    private readonly Dictionary<long, Bucket> units = new();
    private SpellGuardOptions options = new();
    private double tokens = 80, at, prune;
    public int Tracked { get { lock (sync) return units.Count; } }
    public void Reset(SpellGuardOptions value)
    {
        value.Validate();
        lock (sync) { options = value; units.Clear(); tokens = value.GlobalBurst; at = prune = 0; }
    }
    public bool Allow(long unit, bool remote, double now)
    {
        lock (sync)
        {
            if (!options.Enabled || !remote) return true;
            if (options.BlockAllRemote) return false;
            if (now >= prune)
            {
                foreach (var id in units.Where(p => now - p.Value.Seen > 120).Select(p => p.Key).ToArray()) units.Remove(id);
                prune = now + 30;
            }
            tokens = Math.Min(options.GlobalBurst, tokens + Math.Max(0, now - at) * options.GlobalPerSecond);
            at = now;
            if (tokens < 1) return false;
            if (!units.TryGetValue(unit, out var bucket))
            {
                if (units.Count >= 4096) return false;
                bucket = new Bucket { Tokens = options.UnitBurst, At = now };
                units.Add(unit, bucket);
            }
            bucket.Seen = now;
            bucket.Tokens = Math.Min(options.UnitBurst, bucket.Tokens + Math.Max(0, now - bucket.At) * options.UnitPerSecond);
            bucket.At = now;
            if (bucket.Tokens < 1) return false;
            bucket.Tokens--; tokens--;
            return true;
        }
    }
}
