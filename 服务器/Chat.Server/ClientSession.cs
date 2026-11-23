using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Threading.Channels;
using Chat.Protocol;

namespace Chat.Server;

/// <summary>一个已接入的客户端连接。</summary>
internal sealed class ClientSession : IDisposable
{
    private static long _nextId;

    private readonly Channel<string> _outbound;
    private readonly CancellationTokenSource _cts = new();
    private readonly int _maxLineBytes;
    private long _lastSeenTicks = DateTime.UtcNow.Ticks;
    private int _disposed;
    private int _completing;

    public ClientSession(TcpClient tcp, ServerOptions options)
    {
        Id = Interlocked.Increment(ref _nextId);
        Tcp = tcp;
        Remote = tcp.Client.RemoteEndPoint?.ToString() ?? "unknown";
        _maxLineBytes = options.MaxLineBytes;
        _outbound = Channel.CreateBounded<string>(new BoundedChannelOptions(options.OutboundQueueCapacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait,
        });
    }

    public long Id { get; }

    // A connection identity, not a login/account credential. Never reused on reconnect.
    public string SessionId { get; } = Guid.NewGuid().ToString("N");

    private long _gameBudgetTick = Environment.TickCount64;
    private int _gameBudgetMessages;
    private int _gameBudgetBytes;

    // Called only under GameRelay's gate; keep burst costs bounded without blocking the game thread.
    public bool TryGameBudget(int bytes)
    {
        long now = Environment.TickCount64;
        if (now - _gameBudgetTick >= 1000)
        {
            _gameBudgetTick = now;
            _gameBudgetMessages = 0;
            _gameBudgetBytes = 0;
        }
        if (_gameBudgetMessages >= 128 || bytes > 1024 * 1024 - _gameBudgetBytes) return false;
        _gameBudgetMessages++;
        _gameBudgetBytes += bytes;
        return true;
    }

    public TcpClient Tcp { get; }

    public string Remote { get; }

    /// <summary>握手前为 null。</summary>
    public string? Nick { get; set; }

    public string DisplayName => Nick ?? Remote;

    /// <summary>当前所在频道（键不区分大小写）。</summary>
    public ConcurrentDictionary<string, byte> Channels { get; } = new(StringComparer.OrdinalIgnoreCase);

    public CancellationToken SessionToken => _cts.Token;

    public DateTime LastSeenUtc => new(Volatile.Read(ref _lastSeenTicks), DateTimeKind.Utc);

    /// <summary>收到任意一行数据（含 PONG）就刷新活跃时间。</summary>
    public void Touch() => Volatile.Write(ref _lastSeenTicks, DateTime.UtcNow.Ticks);

    public bool Send(string line)
    {
        if (Volatile.Read(ref _disposed) == 1)
        {
            return false;
        }

        // 最后一道闸：绝不把超过单次传输上限的行发出去。
        // 客户端的读取器有同样的上限，发出去会把对方判成 line_too_long 直接断开，
        // 那等于「谁都能踢掉别人」。正常路径上调用方已经先检查过，这里只做兜底。
        int bytes = TransferBudget.LineBytes(line);
        if (bytes > _maxLineBytes)
        {
            Log.Error($"[#{Id}] 拒绝发送 {bytes} 字节的行（单次传输上限 {_maxLineBytes} 字节），已经丢弃：{Preview(line)}");
            return false;
        }

        if (_outbound.Writer.TryWrite(line))
        {
            return true;
        }

        if (Volatile.Read(ref _completing) == 1)
        {
            return false; // 正在收尾（队列已封闭），后续消息静默丢弃即可
        }

        // 队列满 = 这个客户端读得太慢，继续堆积只会拖垮内存，直接断开。
        Log.Warn($"[#{Id}] {DisplayName} 发送队列已满（{_outbound.Reader.Count} 条待发），判定为慢客户端并断开");
        Dispose();
        return false;
    }

    public bool Send(string command, JsonValue? payload) => Send(Wire.Serialize(command, payload));

    public IAsyncEnumerable<string> ReadOutboundAsync(CancellationToken cancellationToken)
        => _outbound.Reader.ReadAllAsync(cancellationToken);

    /// <summary>封闭发送队列：写循环会把已入队的行发完再退出。</summary>
    public void Complete()
    {
        Volatile.Write(ref _completing, 1);
        _outbound.Writer.TryComplete();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        try
        {
            _cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // ignore
        }

        _outbound.Writer.TryComplete();

        try
        {
            Tcp.Close();
        }
        catch (Exception ex) when (ex is SocketException or ObjectDisposedException)
        {
            // ignore
        }

        // 注意：这里刻意不 Dispose() _cts。Dispose 与仍在等待该 token 的循环（读/写/心跳）
        // 并发时可能抛 ObjectDisposedException；CTS 不含计时器，交给 GC 回收即可。
    }

    /// <summary>
    /// 请求成功应答，并把请求里的 id 原样回显，方便客户端做请求/应答配对。
    /// 如果应答本身超过单次传输上限，就降级成一条错误应答（而不是硬发出去把客户端判成超长行）。
    /// </summary>
    public void ReplyOk(JsonValue request, params (string Key, JsonValue Value)[] fields)
    {
        JsonValue reply = BuildReply(request, fields);
        string line = Wire.Serialize(Wire.CmdOk, reply);
        int bytes = TransferBudget.LineBytes(line);

        if (bytes > _maxLineBytes)
        {
            Log.Warn($"[#{Id}] 应答 {bytes} 字节超过单次传输上限 {_maxLineBytes} 字节，已降级为 ERR reply_too_large");
            Send(Wire.CmdErr, BuildReply(request,
                ("code", "reply_too_large"),
                ("msg", $"应答内容超过服务器的单次传输上限 {_maxLineBytes} 字节，请缩小请求范围（或让服务器调大 --max-line）")));
            return;
        }

        Send(line);
    }

    /// <summary>请求失败应答。</summary>
    public void ReplyError(JsonValue request, string code, string message)
        => Send(Wire.CmdErr, BuildReply(request, ("code", code), ("msg", message)));

    /// <summary>日志里用的短预览，避免把整行超长内容打进日志。</summary>
    private static string Preview(string line)
    {
        const int MaxPreview = 80;
        string head = line.Length <= MaxPreview ? line : line.Substring(0, MaxPreview) + "…";
        return head.Replace("\n", "\\n").Replace("\r", "\\r");
    }

    private static JsonValue BuildReply(JsonValue request, params (string Key, JsonValue Value)[] fields)
    {
        List<(string Name, JsonValue Value)> all = new();

        long? id = request.GetInt64("id");
        if (id.HasValue)
        {
            all.Add(("id", JsonValue.Of(id.Value)));
        }

        foreach ((string key, JsonValue value) in fields)
        {
            all.Add((key, value));
        }

        return JsonValue.Object(all.ToArray());
    }
}
