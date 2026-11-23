using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Chat.Protocol;

namespace Chat.Client
{
    /// <summary>
    /// 频道聊天客户端。
    /// <para>
    /// 典型用法：<see cref="ConnectAsync"/> 连接握手 → <see cref="JoinAsync"/> 进频道 → <see cref="SendAsync"/> 发言；
    /// 收到的消息通过 <see cref="MessageReceived"/>、<see cref="ChannelEvent"/> 事件回调抛出。
    /// </para>
    /// <para>
    /// 内部会自动应答服务器心跳 <c>PING</c>（回 <c>PONG</c>），所以长时间空闲也不会被服务器判死连接。
    /// 一个实例对应一条 TCP 连接；要模拟多个用户就创建多个实例。
    /// </para>
    /// <para>本类只使用 .NET Standard 2.0 的 API，.NET Framework 4.6.2+ 可直接引用。</para>
    /// </summary>
    /// <example>
    /// <code>
    /// using (ChatClient client = new ChatClient())
    /// {
    ///     client.MessageReceived += m =&gt; Console.WriteLine(m);
    ///     await client.ConnectAsync("127.0.0.1", 9000, "alice");
    ///     await client.JoinAsync("room1");
    ///     int delivered = await client.SendAsync("room1", "大家好");
    /// }
    /// </code>
    /// </example>
    public sealed partial class ChatClient : IDisposable
    {
        private readonly SemaphoreSlim _writeLock = new SemaphoreSlim(1, 1);
        private readonly ConcurrentDictionary<long, TaskCompletionSource<JsonValue>> _pending =
            new ConcurrentDictionary<long, TaskCompletionSource<JsonValue>>();

        private TcpClient? _tcp;
        private NetworkStream? _stream;
        private LineReader? _reader;
        private CancellationTokenSource? _cts;
        private Task? _receiveLoop;
        private long _requestId;
        private int _closed = 1;
        private int _disposed;

        /// <summary>创建一个尚未连接的客户端。</summary>
        public ChatClient()
        {
            RequestTimeout = TimeSpan.FromSeconds(10);
            ConnectTimeout = TimeSpan.FromSeconds(5);
            Channels = new string[0];
        }

        /// <summary>等待服务器应答的超时时间，默认 10 秒。超时会抛 <see cref="TimeoutException"/>。</summary>
        public TimeSpan RequestTimeout { get; set; }

        /// <summary>TCP 建连超时，默认 5 秒。</summary>
        public TimeSpan ConnectTimeout { get; set; }

        /// <summary>握手后的昵称；<see cref="ConnectAsync"/> 成功之前为 <c>null</c>。</summary>
        public string? Nick { get; private set; }

        /// <summary>当前所在频道（由服务器回执同步）。</summary>
        public IReadOnlyList<string> Channels { get; private set; }

        /// <summary>服务器信息；收到 <c>WELCOME</c> 之前为 <c>null</c>。</summary>
        public ServerInfo? Server { get; private set; }

        /// <summary>连接是否仍然有效。</summary>
        public bool IsConnected
        {
            get { return Volatile.Read(ref _closed) == 0 && _stream != null; }
        }

        /// <summary>
        /// 上一次 <see cref="JoinAsync"/> / <see cref="LeaveAsync"/> / <see cref="ListAsync"/> 返回的列表
        /// 是否被服务器的单次传输上限裁剪过（只返回了列表的前一部分）。
        /// 服务器永远不会为了塞下超长应答而断开客户端，代价就是列表可能被裁剪。
        /// </summary>
        public bool LastListTruncated { get; private set; }

        /// <summary>收到别人发到频道的消息时触发（在后台接收线程上触发）。</summary>
        public event Action<ChatMessage>? MessageReceived;

        /// <summary>频道内有人加入或离开时触发（在后台接收线程上触发）。</summary>
        public event Action<ChannelEventInfo>? ChannelEvent;

        /// <summary>连接结束时触发，参数是断开原因（服务器踢下线、网络中断、主动退出都会触发）。</summary>
        public event Action<string>? Disconnected;

        /// <summary>连接服务器并完成 <c>HELLO</c> 握手（成功后会自动加入服务器的默认频道）。</summary>
        /// <param name="host">服务器地址。</param>
        /// <param name="port">服务器端口。</param>
        /// <param name="nick">登录昵称，服务器要求全局唯一。</param>
        /// <param name="cancellationToken">取消标记。</param>
        /// <exception cref="ChatException">服务器拒绝了握手（例如 <c>nick_in_use</c>）。</exception>
        /// <exception cref="TimeoutException">建连或等待应答超时。</exception>
        /// <exception cref="IOException">网络层错误。</exception>
        public Task ConnectAsync(string host, int port, string nick, CancellationToken cancellationToken = default(CancellationToken))
        {
            return ConnectAsync(host, port, nick, cancellationToken, 0);
        }

        /// <summary>
        /// 连接并握手；仅在 HELLO 返回 nick_in_use 时，在同一连接上每 100ms 重试。
        /// 昵称始终保持原值；nicknameRetryCount 为初次请求之后的重试次数，范围为 0–10。
        /// </summary>
        public async Task ConnectAsync(string host, int port, string nick, CancellationToken cancellationToken, int nicknameRetryCount)
        {
            if (nicknameRetryCount < 0 || nicknameRetryCount > 10)
            {
                throw new ArgumentOutOfRangeException(nameof(nicknameRetryCount), "昵称重试次数必须在 0–10 范围内");
            }
            if (host == null || host.Trim().Length == 0)
            {
                throw new ArgumentException("服务器地址不能为空", nameof(host));
            }

            if (nick == null || nick.Trim().Length == 0)
            {
                throw new ArgumentException("昵称不能为空", nameof(nick));
            }

            if (_stream != null)
            {
                throw new InvalidOperationException("该实例已经连接，请先 Dispose 再新建一个实例");
            }

            cancellationToken.ThrowIfCancellationRequested();

            TcpClient tcp = new TcpClient();
            tcp.NoDelay = true;

            // netstandard2.0 没有 ConnectAsync(host, port, token)，这里自己加超时
            Task connectTask = tcp.ConnectAsync(host, port);
            Task winner = await Task.WhenAny(connectTask, Task.Delay(ConnectTimeout)).ConfigureAwait(false);

            if (winner != connectTask)
            {
                tcp.Close();

                if (cancellationToken.IsCancellationRequested)
                {
                    throw new OperationCanceledException("连接已取消", cancellationToken);
                }

                throw new TimeoutException("连接 " + host + ":" + port.ToString(CultureInfo.InvariantCulture) +
                                           " 超时（" + ConnectTimeout.TotalSeconds.ToString("F0", CultureInfo.InvariantCulture) + "s）");
            }

            await connectTask.ConfigureAwait(false); // 连不上会在这里抛出真实异常

            _tcp = tcp;
            _stream = tcp.GetStream();
            LineReader reader = new LineReader(_stream);
            _reader = reader;
            _cts = new CancellationTokenSource();
            Volatile.Write(ref _closed, 0);

            // 服务器先发 WELCOME，客户端拿到心跳参数后再握手
            string? welcome = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (welcome == null)
            {
                throw new IOException("连接在收到服务器问候之前就被关闭了");
            }

            HandleLine(welcome);

            // 服务器可能把单行上限配得比默认值大（例如为了支持几万字符的长消息）。
            // 拿到 WELCOME 里的 maxLine 之后，把本地读取器的上限同步过去；
            // 否则收到长消息时客户端会用自己的默认上限把它判成超长行、白白断开连接。
            if (Server != null && Server.MaxLineBytes > reader.MaxLineBytes)
            {
                int limit = Server.MaxLineBytes;
                if (limit > Wire.MaxSupportedLineBytes)
                {
                    limit = Wire.MaxSupportedLineBytes; // 协议硬上限，防止服务端报一个天文数字
                }

                reader.MaxLineBytes = limit;
            }

            _receiveLoop = Task.Run(new Func<Task>(() => ReceiveLoopAsync(_cts.Token)));

            JsonValue reply;
            for (int retry = 0; ; retry++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    reply = await RequestAsync(
                        Wire.CmdHello,
                        new List<(string, JsonValue)> { ("nick", nick) },
                        cancellationToken).ConfigureAwait(false);
                    break;
                }
                catch (ChatException e) when (e.Code == "nick_in_use" && retry < nicknameRetryCount)
                {
                    // Dispose of the old client may still be draining its QUIT. Never change
                    // the requested identity or evict the holder; allow a short bounded wait.
                    await Task.Delay(100, cancellationToken).ConfigureAwait(false);
                }
            }

            SessionId = reply.GetString("sessionId") ?? string.Empty;
            Nick = reply.GetString("nick") ?? nick;
            Channels = reply.GetStringArray("channels");
        }

        /// <summary>加入频道，返回加入后的成员昵称列表。</summary>
        /// <param name="channel">频道名（可以带或不带 <c>#</c>）。</param>
        /// <param name="cancellationToken">取消标记。</param>
        /// <exception cref="ChatException">频道名非法（<c>bad_channel</c>）或尚未握手（<c>not_handshaken</c>）。</exception>
        public async Task<IReadOnlyList<string>> JoinAsync(string channel, CancellationToken cancellationToken = default(CancellationToken))
        {
            JsonValue reply = await RequestAsync(
                Wire.CmdJoin,
                new List<(string, JsonValue)> { ("channel", channel) },
                cancellationToken).ConfigureAwait(false);

            Channels = MergeChannel(Channels, reply.GetString("channel") ?? channel);
            LastListTruncated = reply.GetBoolean("membersTruncated") ?? false;
            return reply.GetStringArray("members");
        }

        /// <summary>离开频道，返回剩余成员昵称列表。</summary>
        /// <param name="channel">频道名。</param>
        /// <param name="cancellationToken">取消标记。</param>
        /// <exception cref="ChatException">不在该频道里（<c>not_in_channel</c>）。</exception>
        public async Task<IReadOnlyList<string>> LeaveAsync(string channel, CancellationToken cancellationToken = default(CancellationToken))
        {
            JsonValue reply = await RequestAsync(
                Wire.CmdLeave,
                new List<(string, JsonValue)> { ("channel", channel) },
                cancellationToken).ConfigureAwait(false);

            string name = reply.GetString("channel") ?? channel;
            List<string> remaining = new List<string>();
            foreach (string item in Channels)
            {
                if (!string.Equals(item, name, StringComparison.OrdinalIgnoreCase))
                {
                    remaining.Add(item);
                }
            }

            Channels = remaining.ToArray();
            LastListTruncated = reply.GetBoolean("membersTruncated") ?? false;
            return reply.GetStringArray("members");
        }

        /// <summary>向频道发消息。返回服务器实际投递到的人数（不含自己）。</summary>
        /// <param name="channel">频道名，必须是自己已经加入的频道。</param>
        /// <param name="text">消息正文，长度受服务器下发的正文上限约束。</param>
        /// <param name="cancellationToken">取消标记。</param>
        /// <exception cref="ChatException">
        /// <c>not_in_channel</c> / <c>bad_text</c> / <c>bad_channel</c>；
        /// 若消息转发出去会超过服务器的单次传输上限，也会抛出 <c>line_too_long</c>（本地拦截，不会真的发出去）。
        /// </exception>
        public async Task<int> SendAsync(string channel, string text, CancellationToken cancellationToken = default(CancellationToken))
        {
            // 先用服务器下发的正文上限自检：省一次往返，也给调用方一个明确的错误码
            ServerInfo? server = Server;
            if (server != null && server.MaxTextChars > 0 && text != null && text.Length > server.MaxTextChars)
            {
                throw new ChatException("bad_text",
                    "消息 " + text.Length + " 个字符，超过服务器的正文上限 " + server.MaxTextChars +
                    " 字符；消息没有发出去，连接仍然可用");
            }

            JsonValue reply = await RequestAsync(
                Wire.CmdMsg,
                new List<(string, JsonValue)> { ("channel", channel), ("text", text) },
                cancellationToken).ConfigureAwait(false);

            long? delivered = reply.GetInt64("delivered");
            return delivered.HasValue ? (int)delivered.Value : 0;
        }

        /// <summary>查询服务器上的全部频道与成员。</summary>
        /// <param name="cancellationToken">取消标记。</param>
        public async Task<ChannelInfo[]> ListAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            JsonValue reply = await RequestAsync(Wire.CmdList, null, cancellationToken).ConfigureAwait(false);

            JsonValue channels;
            if (!reply.TryGetProperty("channels", out channels) || channels.Kind != JsonKind.Array)
            {
                return new ChannelInfo[0];
            }

            List<ChannelInfo> result = new List<ChannelInfo>();
            bool truncated = reply.GetBoolean("truncated") ?? false;

            foreach (JsonValue item in channels.Items)
            {
                long? count = item.GetInt64("count");
                bool membersTruncated = item.GetBoolean("membersTruncated") ?? false;
                truncated |= membersTruncated;

                result.Add(new ChannelInfo(
                    item.GetString("name") ?? "?",
                    count.HasValue ? (int)count.Value : 0,
                    item.GetStringArray("members"),
                    membersTruncated));
            }

            LastListTruncated = truncated;
            return result.ToArray();
        }

        /// <summary>应用层探活：发 <c>PING</c> 等服务器的 <c>PONG</c>。成功返回字符串 <c>"PONG"</c>。</summary>
        /// <param name="cancellationToken">取消标记。</param>
        public async Task<string> PingAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            await RequestAsync(Wire.CmdPing, null, cancellationToken).ConfigureAwait(false);
            return "PONG";
        }

        /// <summary>带理由正常退出（服务器会回 <c>BYE</c> 并关闭连接）。</summary>
        /// <param name="reason">退出理由，会发给服务器。</param>
        /// <param name="cancellationToken">取消标记。</param>
        public async Task QuitAsync(string reason = "bye", CancellationToken cancellationToken = default(CancellationToken))
        {
            if (!IsConnected)
            {
                return;
            }

            try
            {
                await SendRawAsync(
                    Wire.Serialize(Wire.CmdQuit, JsonValue.Object(("reason", reason))),
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // 反正要断开了，发不出去也无所谓
                System.Diagnostics.Debug.WriteLine(ex);
            }

            Close("客户端主动退出");
        }

        /// <summary>
        /// 关闭连接：会尽量先发一条 <c>QUIT</c>（最多等 200ms），然后关闭套接字。
        /// 用它包住实例即可：<c>using (ChatClient client = new ChatClient()) { ... }</c>。
        /// </summary>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1)
            {
                return;
            }

            if (IsConnected)
            {
                try
                {
                    Task quit = SendRawAsync(
                        Wire.Serialize(Wire.CmdQuit, JsonValue.Object(("reason", "dispose"))),
                        CancellationToken.None);
                    quit.Wait(TimeSpan.FromMilliseconds(200));
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(ex);
                }
            }

            Close("客户端已释放");
        }

        // ------------------------------------------------------------------ 内部实现

        private async Task<JsonValue> RequestAsync(string command, List<(string Name, JsonValue Value)>? fields, CancellationToken cancellationToken)
        {
            if (_stream == null || !IsConnected)
            {
                throw new InvalidOperationException("尚未连接到服务器");
            }

            long id = Interlocked.Increment(ref _requestId);

            List<(string Name, JsonValue Value)> body = fields ?? new List<(string, JsonValue)>();
            body.Add(("id", JsonValue.Of(id)));

            string line = Wire.Serialize(command, JsonValue.Object(body.ToArray()));

            // 发送前先按服务器下发的单次传输上限自检：超限的行会被服务器直接断开连接
            // （line_too_long）。与其等 10 秒超时、或者干脆被踢，不如立刻给出明确错误。
            ServerInfo? server = Server;
            if (server != null && server.MaxLineBytes > 0)
            {
                int bytes = Encoding.UTF8.GetByteCount(line) + 1;
                if (bytes > server.MaxLineBytes)
                {
                    throw new ChatException("line_too_long",
                        "这条 " + command + " 请求序列化后 " + bytes + " 字节，超过服务器的单次传输上限 " +
                        server.MaxLineBytes + " 字节；请求没有发出去，请缩短内容或让服务器调大 --max-line");
                }
            }

            TaskCompletionSource<JsonValue> completion =
                new TaskCompletionSource<JsonValue>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pending[id] = completion;

            try
            {
                await SendRawAsync(line, cancellationToken).ConfigureAwait(false);

                using (CancellationTokenSource timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    timeoutCts.CancelAfter(RequestTimeout);
                    using (timeoutCts.Token.Register(() =>
                    {
                        if (cancellationToken.IsCancellationRequested)
                        {
                            completion.TrySetCanceled();
                        }
                        else
                        {
                            completion.TrySetException(new TimeoutException(
                                "等待 " + command + " 应答超时（" +
                                RequestTimeout.TotalSeconds.ToString("F0", CultureInfo.InvariantCulture) + "s）"));
                        }
                    }))
                    {
                        return await completion.Task.ConfigureAwait(false);
                    }
                }
            }
            finally
            {
                TaskCompletionSource<JsonValue>? removed;
                _pending.TryRemove(id, out removed);
            }
        }

        private async Task SendRawAsync(string line, CancellationToken cancellationToken)
        {
            NetworkStream? stream = _stream;
            if (stream == null)
            {
                throw new InvalidOperationException("尚未连接到服务器");
            }

            byte[] bytes = Encoding.UTF8.GetBytes(line + "\n");

            await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await stream.WriteAsync(bytes, 0, bytes.Length, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _writeLock.Release();
            }
        }

        private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
        {
            string reason = "服务器关闭了连接";

            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    // _reader 在 ConnectAsync 里创建后才启动本循环，这里必定非空
                    string? line = await _reader!.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                    if (line == null)
                    {
                        break;
                    }

                    if (line.Length == 0)
                    {
                        continue;
                    }

                    HandleLine(line);

                    if (Volatile.Read(ref _closed) == 1)
                    {
                        break;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                reason = "客户端主动断开";
            }
            catch (LineTooLongException)
            {
                reason = "收到的单行超过长度上限";
            }
            catch (Exception ex)
            {
                if (ex is IOException || ex is SocketException || ex is ObjectDisposedException)
                {
                    reason = "网络中断: " + ex.Message;
                }
                else
                {
                    reason = "接收循环异常: " + ex.Message;
                }
            }
            finally
            {
                Close(reason);
            }
        }

        private void HandleLine(string line)
        {
            string command;
            JsonValue payload;
            string error;

            if (!Wire.TryParse(line, out command, out payload, out error))
            {
                return;
            }

            switch (command)
            {
                case "BOARDMSG":
                    HandleBoardLine(payload);
                    return;
                case Wire.CmdWelcome:
                    Server = new ServerInfo(
                        payload.GetString("server") ?? "unknown",
                        payload.GetString("version") ?? "?",
                        ToInt32(payload.GetInt64("heartbeat")),
                        ToInt32(payload.GetInt64("timeout")),
                        ToInt32(payload.GetInt64("handshakeTimeout")),
                        payload.GetString("defaultChannel") ?? string.Empty,
                        ToInt32(payload.GetInt64("maxLine")),
                        ToInt32(payload.GetInt64("maxText")));
                    return;

                case Wire.CmdPing:
                    // 服务器心跳：必须回裸 PONG，否则会被判死连接
                    SendRawSafe(Wire.CmdPong);
                    return;

                case Wire.CmdPong:
                    long? pongId = payload.GetInt64("id");
                    if (pongId.HasValue)
                    {
                        ResolvePending(pongId.Value, payload);
                    }

                    return;

                case Wire.CmdGameMessage:
                case Wire.CmdGameEvent:
                    HandleGameLine(command, payload);
                    return;

                case Wire.CmdOk:
                    TrackGameReply(payload);
                    long? okId = payload.GetInt64("id");
                    if (okId.HasValue)
                    {
                        ResolvePending(okId.Value, payload);
                    }

                    return;

                case Wire.CmdErr:
                    long? errorId = payload.GetInt64("id");
                    if (errorId.HasValue)
                    {
                        FailPending(errorId.Value, new ChatException(
                            payload.GetString("code") ?? "error",
                            payload.GetString("msg") ?? "服务器返回错误"));
                    }

                    return;

                case Wire.CmdMsg:
                    Raise(MessageReceived, new ChatMessage(
                        payload.GetString("channel") ?? string.Empty,
                        payload.GetString("from") ?? string.Empty,
                        payload.GetString("text") ?? string.Empty,
                        ParseTimestamp(payload)));
                    return;

                case Wire.CmdEvent:
                    Raise(ChannelEvent, new ChannelEventInfo(
                        payload.GetString("type") ?? string.Empty,
                        payload.GetString("channel") ?? string.Empty,
                        payload.GetString("nick") ?? string.Empty,
                        ParseTimestamp(payload),
                        payload.GetString("reason")));
                    return;

                case Wire.CmdBye:
                    Close("服务器断开连接: " + (payload.GetString("reason") ?? "bye"));
                    return;
            }
        }

        private void SendRawSafe(string line)
        {
            Task task = SendRawAsync(line, CancellationToken.None);
            task.ContinueWith(
                t =>
                {
                    Exception? ignored = t.Exception; // 观察异常，避免未处理的任务异常
                    if (ignored != null)
                    {
                        System.Diagnostics.Debug.WriteLine(ignored.Message);
                    }
                },
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.Default);
        }

        private void ResolvePending(long id, JsonValue payload)
        {
            TaskCompletionSource<JsonValue>? completion;
            if (_pending.TryRemove(id, out completion) && completion != null)
            {
                completion.TrySetResult(payload);
            }
        }

        private void FailPending(long id, Exception error)
        {
            TaskCompletionSource<JsonValue>? completion;
            if (_pending.TryRemove(id, out completion) && completion != null)
            {
                completion.TrySetException(error);
            }
        }

        private void Close(string reason)
        {
            if (Interlocked.Exchange(ref _closed, 1) == 1)
            {
                return;
            }

            CancellationTokenSource? cts = _cts;
            if (cts != null)
            {
                try
                {
                    cts.Cancel();
                }
                catch (ObjectDisposedException)
                {
                    // ignore
                }
            }

            foreach (KeyValuePair<long, TaskCompletionSource<JsonValue>> entry in _pending)
            {
                entry.Value.TrySetException(new IOException("连接已关闭: " + reason));
            }

            _pending.Clear();
            _gameRooms.Clear();

            if (_stream != null)
            {
                try
                {
                    _stream.Dispose();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(ex);
                }
            }

            if (_tcp != null)
            {
                try
                {
                    _tcp.Close();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(ex);
                }
            }

            Action<string>? handler = Disconnected;
            if (handler != null)
            {
                try
                {
                    handler(reason);
                }
                catch (Exception ex)
                {
                    // 订阅者抛异常不应该影响断开流程
                    System.Diagnostics.Debug.WriteLine(ex);
                }
            }
        }

        private static void Raise<T>(Action<T>? handler, T argument)
        {
            if (handler == null)
            {
                return;
            }

            try
            {
                handler(argument);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(ex);
            }
        }

        private static int ToInt32(long? value)
        {
            return value.HasValue ? (int)value.Value : 0;
        }

        private static DateTimeOffset ParseTimestamp(JsonValue payload)
        {
            string raw = payload.GetString("ts");
            if (raw != null)
            {
                DateTimeOffset parsed;
                if (DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out parsed))
                {
                    return parsed;
                }
            }

            return DateTimeOffset.UtcNow;
        }

        private static IReadOnlyList<string> MergeChannel(IReadOnlyList<string> channels, string channel)
        {
            foreach (string item in channels)
            {
                if (string.Equals(item, channel, StringComparison.OrdinalIgnoreCase))
                {
                    return channels;
                }
            }

            List<string> merged = new List<string>(channels);
            merged.Add(channel);
            return merged.ToArray();
        }
    }
}
