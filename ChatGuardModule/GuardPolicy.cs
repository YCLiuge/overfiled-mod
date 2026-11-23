namespace ChatGuardModule;

public sealed class GuardOptions
{
    public bool Enabled { get; set; } = true;
    public bool LogStats { get; set; } = true;
    public int PlayerBurst { get; set; } = 4;
    public double PlayerPerSecond { get; set; } = 0.8;
    public int GlobalBurst { get; set; } = 24;
    public double GlobalPerSecond { get; set; } = 12;
    public double RepeatSeconds { get; set; } = 3;
    public double FloodMuteSeconds { get; set; } = 5;
    public int MaxTextLength { get; set; } = 512;
    public int MaxNameLength { get; set; } = 64;
    public int MaxLines { get; set; } = 8;
    public bool RejectRichText { get; set; } = true;
    public int MaxHistoryBatch { get; set; } = 100;
    public SpellGuardOptions Spell { get; set; } = new();
    public void Validate()
    {
        if (Spell == null) throw new ArgumentException("Spell 配置不能为空");
        Spell.Validate();
        if (PlayerBurst is < 1 or > 100 || GlobalBurst is < 1 or > 500 ||
            !double.IsFinite(PlayerPerSecond) || PlayerPerSecond is <= 0 or > 100 ||
            !double.IsFinite(GlobalPerSecond) || GlobalPerSecond is <= 0 or > 500 ||
            !double.IsFinite(RepeatSeconds) || RepeatSeconds is < 0 or > 300 ||
            !double.IsFinite(FloodMuteSeconds) || FloodMuteSeconds is < 0 or > 300 ||
            MaxTextLength is < 1 or > 4096 || MaxNameLength is < 1 or > 256 ||
            MaxLines is < 1 or > 32 || MaxHistoryBatch is < 1 or > 500)
            throw new ArgumentException("聊天保护配置超出允许范围，保留上一次有效配置");
    }
}

// Pure managed policy: no Unity objects retained, no timers, bounded state.
public sealed class GuardPolicy
{
    private sealed class Sender
    {
        public double Tokens, Updated, LastSeen, LastMessageAt, MutedUntil;
        public string? LastText;
        public uint LastEmotion;
    }
    private readonly object sync = new();
    private readonly Dictionary<long, Sender> senders = new();
    private GuardOptions options;
    private double globalTokens, globalAt, nextPrune;
    public const int MaxSenders = 4096;
    public int TrackedSenders { get { lock (sync) return senders.Count; } }
    public GuardPolicy(GuardOptions options) { options.Validate(); this.options = options; globalTokens = options.GlobalBurst; }
    public void Reset(GuardOptions value)
    {
        value.Validate();
        lock (sync) { options = value; senders.Clear(); globalTokens = value.GlobalBurst; globalAt = nextPrune = 0; }
    }
    public bool ValidContent(string? text, string? name)
    {
        lock (sync) return !options.Enabled || Content(text, options.MaxTextLength) && Content(name, options.MaxNameLength);
    }
    private bool Content(string? value, int limit)
    {
        if (value == null) return true; // Expression-only messages are valid.
        if (value.Length > limit) return false;
        int lines = 1;
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            if (c == '\n' && ++lines > options.MaxLines) return false;
            if (char.IsControl(c) && c != '\n' && c != '\r' && c != '\t') return false;
            // TMP interprets markup in chat; reject tag openers by default.
            if (options.RejectRichText && c == '<' && i + 1 < value.Length &&
                (char.IsLetter(value[i + 1]) || value[i + 1] == '/' || value[i + 1] == '#')) return false;
        }
        return true;
    }
    public bool Allow(long player, string? text, string? name, uint emotion, double now)
        => AllowLazy(player, (text, name, emotion), static payload => payload, now);

    // Reserve budget BEFORE decoding native strings, even for invalid/repeated content.
    // Static callbacks + value tuples avoid allocating a closure for every rejected message.
    public bool AllowLazy<T>(long player, T state, Func<T, (string? Text, string? Name, uint Emotion)> readPayload, double now)
    {
        lock (sync)
        {
            if (!options.Enabled) return true;
            if (now >= nextPrune)
            {
                foreach (long id in senders.Where(p => now - p.Value.LastSeen > 120).Select(p => p.Key).ToArray()) senders.Remove(id);
                nextPrune = now + 30;
            }
            if (senders.TryGetValue(player, out var muted) && now < muted.MutedUntil)
            {
                muted.LastSeen = now;
                muted.MutedUntil = now + options.FloodMuteSeconds;
                return false;
            }
            globalTokens = Math.Min(options.GlobalBurst, globalTokens + Math.Max(0, now - globalAt) * options.GlobalPerSecond);
            globalAt = now;
            // Global rejection occurs before allocation for new sender IDs.
            if (globalTokens < 1) return false;
            if (!senders.TryGetValue(player, out var sender))
            {
                if (senders.Count >= MaxSenders) return false;
                sender = new Sender { Tokens = options.PlayerBurst, Updated = now };
                senders.Add(player, sender);
            }
            sender.LastSeen = now;
            sender.Tokens = Math.Min(options.PlayerBurst, sender.Tokens + Math.Max(0, now - sender.Updated) * options.PlayerPerSecond);
            sender.Updated = now;
            if (sender.Tokens < 1)
            {
                sender.MutedUntil = now + options.FloodMuteSeconds;
                return false;
            }
            sender.Tokens--;
            globalTokens--;
            var (text, name, emotion) = readPayload(state);
            if (!Content(text, options.MaxTextLength) || !Content(name, options.MaxNameLength)) return false;
            if (sender.LastText != null && sender.LastText == (text ?? "") && sender.LastEmotion == emotion && now - sender.LastMessageAt < options.RepeatSeconds) return false;
            sender.LastText = text ?? "";
            sender.LastEmotion = emotion;
            sender.LastMessageAt = now;
            return true;
        }
    }
}
