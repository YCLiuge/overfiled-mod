namespace GameOptimizerModule;

// Managed names only: no asset or bundle ownership. Caller mutations cannot poison cache.
public sealed class BundleMetadataCache
{
    private readonly Dictionary<(long Pointer, int Instance), (double Expires, string[] Names)> entries = new();
    private int nameCount;
    public long Hits { get; private set; }
    public long Misses { get; private set; }
    public int Count => entries.Count;
    public int NameCount => nameCount;
    public bool TryGet(long pointer, int instance, double now, out string[] names)
    {
        var key = (pointer, instance);
        if (entries.TryGetValue(key, out var entry))
        {
            if (now < entry.Expires) { Hits++; names = (string[])entry.Names.Clone(); return true; }
            nameCount -= entry.Names.Length; entries.Remove(key);
        }
        Misses++; names = Array.Empty<string>(); return false;
    }
    public void Store(long pointer, int instance, double now, string[] names)
    {
        if (names.Length > 32768) return;
        var key = (pointer, instance);
        if (entries.Remove(key, out var old)) nameCount -= old.Names.Length;
        // Clear at budget rather than retaining a growing map or scanning on each lookup.
        if (entries.Count >= 64 || nameCount + names.Length > 32768) Clear();
        entries[key] = (now + 5, (string[])names.Clone()); nameCount += names.Length;
    }
    public void Clear() { entries.Clear(); nameCount = 0; }
}
