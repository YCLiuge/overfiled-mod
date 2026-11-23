using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Chat.Protocol;

namespace Chat.Server;

/// <summary>
/// 频道聊天 TCP 服务器。
/// 每个连接一条读循环 + 一条写循环；写操作走有界队列，保证多线程广播时不会把半行写乱，
/// 也不会因为某个慢客户端阻塞其他用户的转发。
/// </summary>
internal sealed class ChatTcpServer(ServerOptions options, ChatHub hub)
{
    private readonly ConcurrentDictionary<long, ClientSession> _sessions = new();
    private readonly GameRelay _games = new(options);

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        IPAddress address = ResolveBindAddress(options.Host);
        TcpListener listener = new(address, options.Port);
        listener.Start();

        Log.Info($"聊天服务器已启动，监听 {address}:{options.Port}");
        Log.Info($"构建 {typeof(ChatTcpServer).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false).OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion}｜支持 BOARD -> BOARDMSG（正文 24000 UTF-8 字节）");
        Log.Info($"默认频道 #{options.DefaultChannel}｜心跳 {options.HeartbeatSeconds}s｜死连接超时 {options.TimeoutSeconds}s");
        Log.Info($"单次传输上限（一行）{options.MaxLineBytes} 字节｜消息正文上限 {options.MaxTextChars} 字符");

        if (!options.TextFitsLine)
        {
            Log.Warn($"提示：--max-text {options.MaxTextChars} 字符按 UTF-8 最多约占 {options.EstimatedTextBytes} 字节，" +
                     $"加上转发时的频道名/昵称/时间戳可能超过单行上限 {options.MaxLineBytes} 字节；" +
                     "这类超长消息会被拒绝（ERR msg_too_large），连接不受影响。" +
                     "想让任意合法正文都能转发，可调大 --max-line 或调小 --max-text。");
        }

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                TcpClient tcp;
                try
                {
                    tcp = await listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (SocketException ex)
                {
                    Log.Warn($"接受连接失败: {ex.Message}");
                    continue;
                }

                _ = Task.Run(() => HandleClientAsync(tcp, cancellationToken), CancellationToken.None);
            }
        }
        finally
        {
            listener.Stop();
            await ShutdownAsync().ConfigureAwait(false);
        }
    }

    private async Task HandleClientAsync(TcpClient tcp, CancellationToken serverToken)
    {
        tcp.NoDelay = true;

        ClientSession session = new(tcp, options);
        _sessions[session.Id] = session;

        using CancellationTokenSource sessionCts = CancellationTokenSource.CreateLinkedTokenSource(serverToken);
        CancellationToken token = sessionCts.Token;
        string reason = "对端关闭连接";

        Log.Info($"[#{session.Id}] 新连接 {session.Remote}，当前连接数 {_sessions.Count}");

        Task? writerTask = null;
        Task? heartbeatTask = null;

        try
        {
            NetworkStream stream = tcp.GetStream();
            Task writer = Task.Run(() => WriteLoopAsync(session, stream, token), CancellationToken.None);
            writerTask = writer;

            session.Send(Wire.CmdWelcome, JsonValue.Object(
                ("server", Wire.ServerName),
                ("version", Wire.Version),
                ("heartbeat", options.HeartbeatSeconds),
                ("timeout", options.TimeoutSeconds),
                ("handshakeTimeout", options.HandshakeTimeoutSeconds),
                ("defaultChannel", options.DefaultChannel),
                ("maxLine", options.MaxLineBytes),
                ("maxText", options.MaxTextChars)));

            if (options.HeartbeatSeconds > 0)
            {
                heartbeatTask = Task.Run(() => HeartbeatLoopAsync(session, writer, token), CancellationToken.None);
            }

            LineReader reader = new(stream, options.MaxLineBytes);
            bool handshaken = false;

            while (!token.IsCancellationRequested)
            {
                string? line;
                if (!handshaken)
                {
                    // 握手阶段限时：连上来却不发 HELLO 的连接会被回收
                    using CancellationTokenSource handshakeCts = CancellationTokenSource.CreateLinkedTokenSource(token);
                    handshakeCts.CancelAfter(TimeSpan.FromSeconds(options.HandshakeTimeoutSeconds));
                    try
                    {
                        line = await reader.ReadLineAsync(handshakeCts.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (!token.IsCancellationRequested)
                    {
                        reason = $"握手超时（{options.HandshakeTimeoutSeconds}s 内未收到 HELLO）";
                        session.Send(Wire.CmdBye, JsonValue.Object(("reason", "handshake_timeout")));
                        break;
                    }
                }
                else
                {
                    line = await reader.ReadLineAsync(token).ConfigureAwait(false);
                }

                if (line is null)
                {
                    reason = session.Nick is null ? "连接在握手前关闭" : "对端关闭连接";
                    break;
                }

                session.Touch();

                if (line.Length == 0)
                {
                    continue;
                }

                if (!Wire.TryParse(line, out string command, out JsonValue payload, out string parseError))
                {
                    session.ReplyError(payload, "bad_request", parseError);
                    continue;
                }

                if (!await DispatchAsync(session, command, payload, token).ConfigureAwait(false))
                {
                    reason = "客户端主动退出";
                    break;
                }

                if (session.Nick is not null)
                {
                    handshaken = true;
                }
            }
        }
        catch (LineTooLongException)
        {
            reason = $"收到的单行超过单次传输上限（{options.MaxLineBytes} 字节）";
            Log.Warn($"[#{session.Id}] {session.DisplayName} 发来的单行超过 {options.MaxLineBytes} 字节，直接断开");
            session.ReplyError(JsonValue.Undefined, "line_too_long",
                $"单次传输上限为 {options.MaxLineBytes} 字节，本连接将被断开");
        }
        catch (OperationCanceledException)
        {
            reason = serverToken.IsCancellationRequested ? "服务器关闭" : "会话被取消";
        }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
        {
            reason = "连接中断: " + ex.Message;
        }
        catch (Exception ex)
        {
            reason = "服务器内部错误: " + ex.Message;
            Log.Error($"[#{session.Id}] 处理连接时异常: {ex}");
            session.ReplyError(JsonValue.Undefined, "internal_error", "服务器内部错误");
        }
        finally
        {
            await CleanupAsync(session, sessionCts, writerTask, heartbeatTask, reason).ConfigureAwait(false);
        }
    }

    /// <summary>处理一条命令，返回 false 表示应当结束这个连接。</summary>
    private async Task<bool> DispatchAsync(ClientSession session, string command, JsonValue payload, CancellationToken token)
    {
        switch (command)
        {
            case "BOARD":
                return HandleBoard(session, payload);

            case Wire.CmdHello:
                return HandleHello(session, payload);

            case Wire.CmdGameCreate:
            case Wire.CmdGameJoin:
            case Wire.CmdGameLeave:
            case Wire.CmdGameSend:
                return _games.Handle(session, command, payload);

            case Wire.CmdJoin:
                return HandleJoin(session, payload);

            case Wire.CmdLeave:
                return HandleLeave(session, payload);

            case Wire.CmdMsg:
                return HandleMessage(session, payload);

            case Wire.CmdList:
                return HandleList(session, payload);

            case Wire.CmdPing:
                // 应用层探活：有 id 就带上，客户端据此配对；没有 id 就是裸 PONG
                long? pingId = payload.GetInt64("id");
                session.Send(Wire.CmdPong, pingId.HasValue ? JsonValue.Object(("id", pingId.Value)) : null);
                return true;

            case Wire.CmdPong:
                // 活跃时间已在读到这一行时刷新，这里无需处理
                return true;

            case Wire.CmdQuit:
                session.Send(Wire.CmdBye, JsonValue.Object(("reason", payload.GetString("reason") ?? "bye")));
                await Task.Delay(50, CancellationToken.None).ConfigureAwait(false); // 给写循环一点时间把 BYE 发出去
                return false;

            default:
                session.ReplyError(payload, "unknown_command", $"未知命令: {command}");
                return true;
        }
    }

    // BOARD is a dedicated opaque board transport. Never convert it to ordinary MSG.
    private bool HandleBoard(ClientSession session, JsonValue payload)
    {
        if (!EnsureHandshaken(session, payload)) return true;
        if (!Validation.TryChannel(payload.GetString("channel"), out string channel, out string channelError))
        {
            session.ReplyError(payload, "bad_channel", channelError);
            return true;
        }
        if (session.Channels.Count != 1)
        {
            session.ReplyError(payload, "single_channel_required", "Board commands require exactly one joined channel");
            return true;
        }
        if (!session.Channels.ContainsKey(channel) || !hub.TryGet(channel, out ChannelRoom room) || !room.Contains(session.Id))
        {
            session.ReplyError(payload, "not_in_channel", "Join the board channel first");
            return true;
        }
        // Board payload is opaque (the client validates moves), but must be a nonempty string.
        if (!Validation.TryText(payload.GetString("text"), options.MaxTextChars, out string text, out string textError))
        {
            session.ReplyError(payload, "bad_text", textError);
            return true;
        }
        int textBytes = Encoding.UTF8.GetByteCount(text);
        if (textBytes > 24000)
        {
            session.ReplyError(payload, "board_msg_too_large", "Board text exceeds 24000 UTF-8 bytes");
            return true;
        }
        string line = Wire.Serialize("BOARDMSG", JsonValue.Object(
            ("channel", room.Name), ("fromId", session.SessionId), ("from", session.Nick), ("text", text)));
        // Serialization adds identity and JSON escaping. Reject before enqueue, instead of
        // silently dropping at ClientSession.Send and incorrectly returning OK delivered=0.
        if (TransferBudget.LineBytes(line) > options.MaxLineBytes)
        {
            session.ReplyError(payload, "msg_too_large", "Forwarded board message exceeds line limit");
            return true;
        }
        if (!session.TryGameBudget(textBytes))
        {
            session.ReplyError(payload, "board_rate_limited", "Game transport budget: 128 messages and 1 MiB per second per connection");
            return true;
        }
        int delivered = room.Broadcast(line, session.Id);
        session.ReplyOk(payload, ("delivered", delivered));
        return true;
    }

    private bool HandleHello(ClientSession session, JsonValue payload)
    {
        if (session.Nick is not null)
        {
            session.ReplyError(payload, "already_handshaken", "已经完成握手，无需重复 HELLO");
            return true;
        }

        if (!Validation.TryNick(payload.GetString("nick"), out string nick, out string nickError))
        {
            session.ReplyError(payload, "bad_nick", nickError);
            return true;
        }

        if (!hub.TryClaimNick(nick, session))
        {
            session.ReplyError(payload, "nick_in_use", $"昵称 {nick} 已被占用，请换一个");
            return true;
        }

        session.Nick = nick;
        session.Touch();

        if (!string.IsNullOrEmpty(options.DefaultChannel) &&
            Validation.TryChannel(options.DefaultChannel, out string defaultChannel, out _))
        {
            JoinChannel(session, defaultChannel, announce: false);
        }

        session.ReplyOk(payload,
            ("sessionId", session.SessionId),
            ("nick", nick),
            ("server", Wire.ServerName),
            ("version", Wire.Version),
            ("channels", JsonValue.Strings(session.Channels.Keys.ToArray())));

        Log.Info($"[#{session.Id}] {session.Remote} 握手成功，昵称 {nick}");
        return true;
    }

    private bool HandleJoin(ClientSession session, JsonValue payload)
    {
        if (!EnsureHandshaken(session, payload))
        {
            return true;
        }

        if (!Validation.TryChannel(payload.GetString("channel"), out string channel, out string channelError))
        {
            session.ReplyError(payload, "bad_channel", channelError);
            return true;
        }

        if (!JoinChannel(session, channel, announce: true))
        {
            ChannelRoom existing = hub.GetOrCreate(channel);
            session.ReplyOk(payload,
                ("channel", existing.Name),
                ("members", FitMembers(existing.Nicks(), existing.Name, out bool alreadyCut)),
                ("membersTruncated", alreadyCut),
                ("alreadyMember", true));
            return true;
        }

        ChannelRoom room = hub.GetOrCreate(channel);
        session.ReplyOk(payload,
            ("channel", room.Name),
            ("members", FitMembers(room.Nicks(), room.Name, out bool cut)),
            ("membersTruncated", cut));
        return true;
    }

    private bool HandleLeave(ClientSession session, JsonValue payload)
    {
        if (!EnsureHandshaken(session, payload))
        {
            return true;
        }

        if (!Validation.TryChannel(payload.GetString("channel"), out string channel, out string channelError))
        {
            session.ReplyError(payload, "bad_channel", channelError);
            return true;
        }

        if (!hub.TryGet(channel, out ChannelRoom room) || !room.Contains(session.Id))
        {
            session.ReplyError(payload, "not_in_channel", $"你不在频道 #{channel} 中");
            return true;
        }

        // Relay cleanup runs before channel removal, with no chat/hub lock held.
        _games.LeaveChannel(session, room.Name);
        room.Remove(session);
        session.Channels.TryRemove(channel, out _);

        session.ReplyOk(payload,
            ("channel", room.Name),
            ("members", FitMembers(room.Nicks(), room.Name, out bool membersCut)),
            ("membersTruncated", membersCut));
        room.Broadcast(
            Wire.Serialize(Wire.CmdEvent, JsonValue.Object(
                ("type", "leave"),
                ("channel", room.Name),
                ("nick", session.Nick),
                ("ts", Wire.TimestampNow()))),
            session.Id);
        hub.RemoveIfEmpty(channel);

        Log.Info($"[#{session.Id}] {session.DisplayName} 离开 #{room.Name}（剩余 {room.Count} 人）");
        return true;
    }

    private bool HandleMessage(ClientSession session, JsonValue payload)
    {
        if (!EnsureHandshaken(session, payload))
        {
            return true;
        }

        if (!Validation.TryChannel(payload.GetString("channel"), out string channel, out string channelError))
        {
            session.ReplyError(payload, "bad_channel", channelError);
            return true;
        }

        if (!Validation.TryText(payload.GetString("text"), options.MaxTextChars, out string text, out string textError))
        {
            session.ReplyError(payload, "bad_text", textError);
            return true;
        }

        if (!hub.TryGet(channel, out ChannelRoom room) || !room.Contains(session.Id))
        {
            session.ReplyError(payload, "not_in_channel", $"你不在频道 #{channel} 中，请先 JOIN");
            return true;
        }

        string line = Wire.Serialize(Wire.CmdMsg, JsonValue.Object(
            ("channel", room.Name),
            ("from", session.Nick),
            ("text", text),
            ("ts", Wire.TimestampNow())));

        // 转发出去的行比发进来的行多出 from / ts 字段，所以必须再量一次：
        // 否则发送者只要发一条「刚好不超过上限」的消息，就能让接收方收到超长行而被断开。
        int lineBytes = TransferBudget.LineBytes(line);
        if (lineBytes > options.MaxLineBytes)
        {
            Log.Warn($"[#{session.Id}] {session.DisplayName} -> #{room.Name} 的消息转发后需要 {lineBytes} 字节，" +
                     $"超过单次传输上限 {options.MaxLineBytes} 字节，已拒绝（连接保留）");
            session.ReplyError(payload, "msg_too_large",
                $"这条消息转发给其他人时需要 {lineBytes} 字节（含频道名、昵称、时间戳），" +
                $"超过服务器的单次传输上限 {options.MaxLineBytes} 字节；请把消息改短一些");
            return true;
        }

        // 只转发给同频道的其他人，发送者自己拿到的是 OK 回执（delivered = 人数）
        int delivered = room.Broadcast(line, session.Id);
        session.ReplyOk(payload, ("channel", room.Name), ("delivered", delivered));

        Log.Debug($"[#{session.Id}] {session.Nick} -> #{room.Name}（{delivered} 人接收）: {text}");
        return true;
    }

    private bool HandleList(ClientSession session, JsonValue payload)
    {
        if (!EnsureHandshaken(session, payload))
        {
            return true;
        }

        // 按单次传输预算逐条填充：宁可少列几个频道，也不能让这条应答超过上限
        // （否则客户端会收到自己解析不了的行，被自己的读取器判成超长行而断开）
        int budget = options.MaxLineBytes - TransferBudget.ReplyReserveBytes;
        List<JsonValue> channels = new();
        bool channelsTruncated = false;
        bool membersTruncated = false;
        int used = 0;

        foreach (ChannelRoom channelRoom in hub.All())
        {
            int remaining = budget - used;
            if (remaining < TransferBudget.MinListBudget * 2)
            {
                channelsTruncated = true;
                break;
            }

            JsonValue members = TransferBudget.FitStrings(
                channelRoom.Nicks(),
                Math.Max(TransferBudget.MinListBudget, remaining / 2),
                out bool membersCut);
            membersTruncated |= membersCut;

            JsonValue entry = JsonValue.Object(
                ("name", channelRoom.Name),
                ("count", channelRoom.Count),
                ("members", members),
                ("membersTruncated", membersCut));

            int cost = TransferBudget.LineBytes(entry.ToJson());
            if (used + cost > budget)
            {
                channelsTruncated = true;
                break;
            }

            channels.Add(entry);
            used += cost;
        }

        if (channelsTruncated)
        {
            Log.Warn($"[#{session.Id}] 频道数量超出单次传输预算（{options.MaxLineBytes} 字节），LIST 只返回了 {channels.Count} 个频道");
        }

        session.ReplyOk(payload,
            ("channels", JsonValue.Array(channels.ToArray())),
            ("online", hub.OnlineCount),
            ("joined", FitMembers(session.Channels.Keys.ToArray(), null, out bool joinedCut)),
            ("truncated", channelsTruncated || joinedCut),
            ("membersTruncated", membersTruncated));
        return true;
    }

    /// <summary>把连接加入频道；返回 false 表示本来就已经在频道里。</summary>
    private bool JoinChannel(ClientSession session, string channel, bool announce)
    {
        ChannelRoom room = hub.GetOrCreate(channel);

        if (!room.Add(session))
        {
            return false;
        }

        session.Channels[channel] = 0;

        if (announce)
        {
            room.Broadcast(
                Wire.Serialize(Wire.CmdEvent, JsonValue.Object(
                    ("type", "join"),
                    ("channel", room.Name),
                    ("nick", session.Nick),
                    ("ts", Wire.TimestampNow()))),
                session.Id);
            Log.Info($"[#{session.Id}] {session.DisplayName} 加入 #{room.Name}（当前 {room.Count} 人）");
        }

        return true;
    }

    /// <summary>按单次传输预算裁剪成员列表（JOIN / LEAVE / LIST 的应答都走这里）。</summary>
    private JsonValue FitMembers(string[] nicks, string? channel, out bool truncated)
    {
        int nameBytes = channel is null ? 0 : Encoding.UTF8.GetByteCount(channel);
        return TransferBudget.FitStrings(nicks, TransferBudget.ListBudget(options.MaxLineBytes, nameBytes), out truncated);
    }

    private static bool EnsureHandshaken(ClientSession session, JsonValue payload)
    {
        if (session.Nick is not null)
        {
            return true;
        }

        session.ReplyError(payload, "not_handshaken", "请先发送 HELLO {\"nick\":\"你的昵称\"}");
        return false;
    }

    private static async Task WriteLoopAsync(ClientSession session, NetworkStream stream, CancellationToken token)
    {
        try
        {
            await foreach (string line in session.ReadOutboundAsync(token).ConfigureAwait(false))
            {
                byte[] bytes = Encoding.UTF8.GetBytes(line + "\n");
                await stream.WriteAsync(bytes, token).ConfigureAwait(false);
            }

            await stream.FlushAsync(token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // 正常关闭
        }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
        {
            Log.Debug($"[#{session.Id}] 写循环结束: {ex.Message}");
        }
    }

    private async Task HeartbeatLoopAsync(ClientSession session, Task writerTask, CancellationToken token)
    {
        using PeriodicTimer timer = new(TimeSpan.FromSeconds(options.HeartbeatSeconds));
        TimeSpan timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);

        try
        {
            while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
            {
                TimeSpan idle = DateTime.UtcNow - session.LastSeenUtc;
                if (idle > timeout)
                {
                    Log.Warn($"[#{session.Id}] {session.DisplayName} 静默 {idle.TotalSeconds:F0}s 超过 {options.TimeoutSeconds}s，判定为死连接");

                    // 关键顺序：先把 BYE 交给写循环并等它发完，再关闭套接字；
                    // 否则客户端只会看到连接被重置，收不到断开原因。
                    session.Send(Wire.CmdBye, JsonValue.Object(("reason", "timeout")));
                    session.Complete();
                    await Task.WhenAny(writerTask, Task.Delay(TimeSpan.FromSeconds(1), CancellationToken.None)).ConfigureAwait(false);
                    session.Dispose(); // 关闭套接字，读循环会立刻抛错并进入清理流程
                    return;
                }

                session.Send(Wire.CmdPing); // 裸 PING，客户端应回裸 PONG
            }
        }
        catch (OperationCanceledException)
        {
            // 会话结束
        }
    }

    private async Task CleanupAsync(ClientSession session, CancellationTokenSource sessionCts, Task? writerTask, Task? heartbeatTask, string reason)
    {
        session.Complete();
        _games.Disconnect(session, reason);

        // 1) 退出所有频道并广播离开事件
        foreach (string channel in session.Channels.Keys.ToArray())
        {
            if (!hub.TryGet(channel, out ChannelRoom room))
            {
                continue;
            }

            room.Remove(session);
            room.Broadcast(
                Wire.Serialize(Wire.CmdEvent, JsonValue.Object(
                    ("type", "leave"),
                    ("channel", room.Name),
                    ("nick", session.Nick),
                    ("reason", reason),
                    ("ts", Wire.TimestampNow()))),
                session.Id);
            hub.RemoveIfEmpty(channel);
        }

        session.Channels.Clear();

        // 2) 释放昵称，让同一昵称可以重新登录
        hub.ReleaseNick(session);
        _sessions.TryRemove(session.Id, out _);

        // 3) 先等写循环把队列里剩下的行（例如 BYE）发完，再取消会话并关闭套接字
        if (writerTask is not null)
        {
            await Task.WhenAny(writerTask, Task.Delay(TimeSpan.FromMilliseconds(300))).ConfigureAwait(false);
        }

        try
        {
            await sessionCts.CancelAsync().ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            // ignore
        }

        if (heartbeatTask is not null)
        {
            await Task.WhenAny(heartbeatTask, Task.Delay(TimeSpan.FromMilliseconds(300))).ConfigureAwait(false);
        }

        session.Dispose();

        Log.Info($"[#{session.Id}] {session.DisplayName} 断开: {reason}（当前连接数 {_sessions.Count}）");
    }

    private async Task ShutdownAsync()
    {
        Log.Info("正在关闭：通知所有客户端并清理连接…");

        foreach (ClientSession session in _sessions.Values)
        {
            session.Send(Wire.CmdBye, JsonValue.Object(("reason", "server_shutdown")));
            session.Complete();
        }

        await Task.Delay(TimeSpan.FromMilliseconds(300)).ConfigureAwait(false);

        foreach (ClientSession session in _sessions.Values)
        {
            session.Dispose();
        }

        Log.Info("服务器已停止");
    }

    private static IPAddress ResolveBindAddress(string host)
    {
        if (host is "0.0.0.0" or "*" or "+")
        {
            return IPAddress.Any;
        }

        if (host is "::" or "[::]")
        {
            return IPAddress.IPv6Any;
        }

        if (IPAddress.TryParse(host, out IPAddress? parsed))
        {
            return parsed;
        }

        IPAddress[] resolved = Dns.GetHostAddresses(host);
        return resolved.Length > 0
            ? resolved[0]
            : throw new ArgumentException($"无法解析主机名: {host}");
    }
}
