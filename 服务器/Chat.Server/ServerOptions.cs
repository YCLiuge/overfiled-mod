using Chat.Protocol;

namespace Chat.Server;

/// <summary>命令行配置（只从命令行读取，暂不支持环境变量或配置文件）。</summary>
internal sealed class ServerOptions
{
    public string Host { get; set; } = "0.0.0.0";

    public int Port { get; set; } = Wire.DefaultPort;

    /// <summary>握手后自动加入的默认频道；设为空字符串表示不自动加入。</summary>
    public string DefaultChannel { get; set; } = "general";

    /// <summary>服务器主动发 PING 的间隔秒数；0 表示关闭心跳。</summary>
    public int HeartbeatSeconds { get; set; } = 15;

    /// <summary>超过该秒数没收到任何数据（含 PONG）就断开连接。</summary>
    public int TimeoutSeconds { get; set; } = 45;

    /// <summary>连接建立后必须在该秒数内完成 HELLO 握手。</summary>
    public int HandshakeTimeoutSeconds { get; set; } = 10;

    /// <summary>
    /// 单次传输（一行请求/应答）的字节上限；超过的入站行直接断开连接。
    /// 默认 256 KB：配合 <see cref="MaxTextChars"/> = 50000，50000 个中文（约 150 KB）也装得下。
    /// </summary>
    public int MaxLineBytes { get; set; } = Wire.DefaultLineBytes;

    /// <summary>单条消息正文的字符上限，默认 50000。</summary>
    public int MaxTextChars { get; set; } = Validation.DefaultTextChars;

    /// <summary>
    /// 单个连接的发送队列容量（单位：条数）。默认 64。
    /// 账要这么算：队列里一条最坏占 <see cref="MaxLineBytes"/> 字节，64 × 256 KB ≈ 16 MB，
    /// 就是单个慢客户端的缓冲上限；调大之前先把这笔内存乘一遍并发数。
    /// </summary>
    public int OutboundQueueCapacity { get; set; } = 64;

    public bool ShowHelp { get; set; }

    /// <summary>单行上限的允许范围（下限防止把协议自己卡死，上限防止配置本身变成内存风险）。</summary>
    public const int MinLineBytes = 256;

    public const int MaxLineBytesLimit = Wire.MaxSupportedLineBytes;

    /// <summary>消息正文的字符上限范围。</summary>
    public const int MaxTextCharsLimit = 65536;

    public static ServerOptions Parse(string[] args)
    {
        ServerOptions options = new();

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            string? inlineValue = null;

            int equals = arg.IndexOf('=');
            if (arg.StartsWith("--", StringComparison.Ordinal) && equals > 2)
            {
                inlineValue = arg[(equals + 1)..];
                arg = arg[..equals];
            }

            string? Value()
            {
                if (inlineValue is not null)
                {
                    return inlineValue;
                }

                if (i + 1 >= args.Length)
                {
                    throw new ArgumentException($"参数 {arg} 缺少取值");
                }

                return args[++i];
            }

            int IntValue(string name)
                => int.TryParse(Value(), out int parsed) ? parsed : throw new ArgumentException($"参数 {name} 需要整数");

            switch (arg.ToLowerInvariant())
            {
                case "--host":
                case "-h":
                    options.Host = Value()!;
                    break;
                case "--port":
                case "-p":
                    options.Port = IntValue(arg);
                    break;
                case "--channel":
                case "--default-channel":
                    options.DefaultChannel = (Value() ?? string.Empty).TrimStart('#');
                    break;
                case "--heartbeat":
                    options.HeartbeatSeconds = IntValue(arg);
                    break;
                case "--timeout":
                    options.TimeoutSeconds = IntValue(arg);
                    break;
                case "--handshake-timeout":
                    options.HandshakeTimeoutSeconds = IntValue(arg);
                    break;
                case "--max-line":
                    options.MaxLineBytes = IntValue(arg);
                    break;
                case "--max-text":
                    options.MaxTextChars = IntValue(arg);
                    break;
                case "--queue":
                    options.OutboundQueueCapacity = IntValue(arg);
                    break;
                case "--verbose":
                    Log.Verbose = true;
                    break;
                case "--help":
                case "-?":
                    options.ShowHelp = true;
                    break;
                default:
                    throw new ArgumentException($"未知参数: {arg}");
            }
        }

        if (options.Port is < 1 or > 65535)
        {
            throw new ArgumentException($"端口不合法: {options.Port}（应为 1-65535）");
        }

        if (options.HeartbeatSeconds < 0)
        {
            throw new ArgumentException("--heartbeat 不能为负数");
        }

        if (options.HeartbeatSeconds > 0 && options.TimeoutSeconds <= options.HeartbeatSeconds)
        {
            throw new ArgumentException("--timeout 必须大于 --heartbeat，否则连接会在下一拍就被判超时");
        }

        if (options.MaxLineBytes < MinLineBytes || options.MaxLineBytes > MaxLineBytesLimit)
        {
            throw new ArgumentException(
                $"--max-line 必须在 {MinLineBytes}~{MaxLineBytesLimit} 字节之间（当前 {options.MaxLineBytes}）");
        }

        if (options.MaxTextChars < 1 || options.MaxTextChars > MaxTextCharsLimit)
        {
            throw new ArgumentException(
                $"--max-text 必须在 1~{MaxTextCharsLimit} 个字符之间（当前 {options.MaxTextChars}）");
        }

        if (options.OutboundQueueCapacity < 16)
        {
            throw new ArgumentException("--queue 至少为 16，否则正常客户端也会被判成慢客户端");
        }

        return options;
    }

    /// <summary>
    /// 单条消息正文转成 UTF-8 后的估算字节数：按约 4 字节/字符算
    /// （中文 3 字节/字符，emoji 是 4 字节/2 个 char，所以 4 已经偏保守）。
    /// 只有全是控制字符时才会被转义成 <c>\uXXXX</c> 的 6 字节/字符，那属于刻意构造的极端情况。
    /// </summary>
    public int EstimatedTextBytes => MaxTextChars * 4;

    /// <summary>转发一条消息时除正文外的固定开销上限（频道名 64 + 昵称 32 + 时间戳与骨架）。</summary>
    public const int ForwardHeadroomBytes = 192;

    /// <summary>正文上限与单行上限是否匹配；不匹配时启动会提示，超长消息会被 <c>msg_too_large</c> 拒掉。</summary>
    public bool TextFitsLine => EstimatedTextBytes + ForwardHeadroomBytes <= MaxLineBytes;

    public static void PrintUsage()
    {
        Console.WriteLine($$"""
            {{Wire.ServerName}} 频道聊天 TCP 服务器 v{{Wire.Version}}

            用法: Chat.Server [选项]

              --host <地址>             监听地址，默认 0.0.0.0（全部网卡；本机测试可用 127.0.0.1）
              --port <端口>             监听端口，默认 {{Wire.DefaultPort}}
              --channel <频道名>        握手后自动加入的频道，默认 general；传空字符串则不自动加入
              --heartbeat <秒>          服务器 PING 间隔，默认 15；0 = 关闭心跳
              --timeout <秒>            判定死连接的静默时长，默认 45（必须大于 heartbeat）
              --handshake-timeout <秒>  必须在此时间内完成 HELLO，默认 10
              --max-line <字节>         单次传输（一行）的字节上限，默认 {{Wire.DefaultLineBytes}}，允许 {{MinLineBytes}}~{{MaxLineBytesLimit}}
                                        · 入站超过上限：回 ERR line_too_long 后直接断开连接
                                        · 出站超过上限：服务器永远不会发出这样的行（转发会被拒、应答会降级成 ERR）
              --max-text <字符>         单条消息正文的字符上限，默认 {{Validation.DefaultTextChars}}
              --queue <条>              单连接发送队列容量，默认 64（一条最坏占 --max-line 字节，注意内存账）
              --verbose                 打印调试日志
              --help                    显示本帮助

            协议速览（UTF-8 文本行，\n 分帧）:
              JOIN  {"channel":"general"}                -> OK {"channel":"general","members":["alice"]}
              MSG   {"channel":"general","text":"hi"}    -> OK {"delivered":2}，并转发给同频道其他人
              HELLO {"nick":"alice"} / LEAVE / LIST / PING / QUIT
            """);
    }
}
