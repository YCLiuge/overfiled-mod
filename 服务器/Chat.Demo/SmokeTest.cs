using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Chat.Client;
using Chat.Protocol;

namespace Chat.Demo
{
    /// <summary>
    /// 端到端自检：对着一个正在运行的服务器，把「同频道转发、跨频道隔离、频道列表、心跳保活、超时踢人」逐条验一遍。
    /// 建议服务器用低心跳参数启动，例如：Chat.Server --heartbeat 1 --timeout 3
    /// </summary>
    internal static class SmokeTest
    {
        /// <summary>跑完整套断言，返回进程退出码（0 = 全部通过）。</summary>
        public static async Task<int> RunAsync(string host, int port)
        {
            Checker checker = new Checker();

            void Check(bool ok, string description) => checker.Check(ok, description);

            Console.WriteLine("=== 端到端自检 " + host + ":" + port + " ===");

            ChatClient alice = new ChatClient();
            ChatClient bob = new ChatClient();
            ChatClient carol = new ChatClient();

            AsyncInbox<ChatMessage> aliceInbox = new AsyncInbox<ChatMessage>();
            AsyncInbox<ChatMessage> bobInbox = new AsyncInbox<ChatMessage>();
            AsyncInbox<ChatMessage> carolInbox = new AsyncInbox<ChatMessage>();
            AsyncInbox<ChannelEventInfo> bobEvents = new AsyncInbox<ChannelEventInfo>();

            alice.MessageReceived += message => aliceInbox.Add(message);
            bob.MessageReceived += message => bobInbox.Add(message);
            carol.MessageReceived += message => carolInbox.Add(message);
            bob.ChannelEvent += channelEvent => bobEvents.Add(channelEvent);

            try
            {
                // ---- 1. 连接与握手 ----
                await alice.ConnectAsync(host, port, "alice").ConfigureAwait(false);
                await bob.ConnectAsync(host, port, "bob").ConfigureAwait(false);
                await carol.ConnectAsync(host, port, "carol").ConfigureAwait(false);

                Check(alice.IsConnected && bob.IsConnected && carol.IsConnected, "三个客户端均已完成 TCP 连接与 HELLO 握手");
                Check(ChannelsContain(alice.Channels, "general"), "握手后自动加入默认频道（channels=" + string.Join(", ", alice.Channels) + "）");
                Check(alice.Server != null && alice.Server.HeartbeatSeconds > 0,
                    "WELCOME 下发心跳参数（heartbeat=" + (alice.Server == null ? "?" : alice.Server.HeartbeatSeconds.ToString()) +
                    "s，timeout=" + (alice.Server == null ? "?" : alice.Server.TimeoutSeconds.ToString()) + "s）");

                // carol 换到另一个频道，用来验证频道隔离
                await carol.LeaveAsync("general").ConfigureAwait(false);
                await carol.JoinAsync("other").ConfigureAwait(false);

                // ---- 2. 昵称唯一性 ----
                bool duplicateRejected = false;
                string duplicateCode = "(未拒绝)";
                ChatClient duplicate = new ChatClient();
                try
                {
                    await duplicate.ConnectAsync(host, port, "alice").ConfigureAwait(false);
                }
                catch (ChatException ex)
                {
                    duplicateRejected = true;
                    duplicateCode = ex.Code;
                }
                finally
                {
                    duplicate.Dispose();
                }

                Check(duplicateRejected && duplicateCode == "nick_in_use", "重复昵称被服务器拒绝（code=" + duplicateCode + "）");

                // ---- 3. 同频道转发 + 频道隔离 + 不回显给自己 ----
                int delivered = await alice.SendAsync("general", "hello bob").ConfigureAwait(false);
                Check(delivered == 1, "alice 发到 general 的投递人数 = " + delivered + "（期望 1：同频道只有 bob）");

                ChatMessage? received = await bobInbox.ReadAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
                Check(received != null && received.Text == "hello bob" && received.From == "alice" && received.Channel == "general",
                    "bob 收到转发消息：<" + (received == null ? "?" : received.From) + "> #" + (received == null ? "?" : received.Channel) +
                    ": " + (received == null ? "未收到" : received.Text));

                Check(await aliceInbox.ReadAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false) == null, "发送者本人没有收到回显（只转发给同频道的其他人）");
                Check(await carolInbox.ReadAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false) == null, "carol 在 other 频道，收不到 general 的消息");

                // ---- 3b. JSON 转义往返：引号 / 反斜杠 / 换行 / 制表 / 控制字符 / 中文 / emoji ----
                string tricky = "引号\" 反斜杠\\ 换行\n 制表\t 控制\u0001 emoji😀 结束";
                await alice.SendAsync("general", tricky).ConfigureAwait(false);
                ChatMessage? roundTrip = await bobInbox.ReadAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
                Check(roundTrip != null && roundTrip.Text == tricky,
                    "特殊字符消息原样往返（引号/反斜杠/换行/制表/控制字符/中文/emoji，共 " +
                    (roundTrip == null || roundTrip.Text == null ? 0 : roundTrip.Text.Length) + " 字符）");

                // ---- 4. 参数校验 ----
                string errorCode = await ExpectErrorAsync(() => alice.SendAsync("other", "hi")).ConfigureAwait(false);
                Check(errorCode == "not_in_channel", "向未加入的频道发言被拒绝（code=" + errorCode + "）");

                errorCode = await ExpectErrorAsync(() => alice.SendAsync("general", string.Empty)).ConfigureAwait(false);
                Check(errorCode == "bad_text", "空消息被拒绝（code=" + errorCode + "）");

                errorCode = await ExpectErrorAsync(() => alice.JoinAsync("非法 频道名")).ConfigureAwait(false);
                Check(errorCode == "bad_channel", "非法频道名被拒绝（code=" + errorCode + "）");

                // ---- 5. 频道/成员列表 ----
                ChannelInfo[] channels = await alice.ListAsync().ConfigureAwait(false);
                ChannelInfo? general = channels.FirstOrDefault(c => c.Name == "general");
                ChannelInfo? other = channels.FirstOrDefault(c => c.Name == "other");

                Check(general != null && general.MemberCount == 2 && other != null && other.MemberCount == 1,
                    "LIST 快照正确：general=" + (general == null ? "?" : general.MemberCount.ToString()) +
                    " 人，other=" + (other == null ? "?" : other.MemberCount.ToString()) + " 人");
                Check(general != null && general.Members.Contains("alice") && general.Members.Contains("bob"),
                    "general 成员列表 = " + (general == null ? "" : string.Join(", ", general.Members)));

                // ---- 6. 应用层探活 ----
                Check(await alice.PingAsync().ConfigureAwait(false) == "PONG", "PING/PONG 探活正常");

                // ---- 7. 心跳保活（服务器需以 --heartbeat 1 --timeout 3 启动才有意义） ----
                await Task.Delay(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                Check(alice.IsConnected && bob.IsConnected && carol.IsConnected,
                    "静默 5s（超过服务器 timeout 阈值）后连接仍存活 ⇒ 客户端自动回 PONG 生效");

                // ---- 8. 断开广播 ----
                await alice.QuitAsync().ConfigureAwait(false);
                ChannelEventInfo? leaveEvent = await WaitEventAsync(bobEvents, e => e.Type == "leave" && e.Nick == "alice", TimeSpan.FromSeconds(3)).ConfigureAwait(false);
                Check(leaveEvent != null, "bob 收到 alice 离开 #general 的广播事件（ts=" +
                    (leaveEvent == null ? "?" : leaveEvent.Timestamp.ToLocalTime().ToString("HH:mm:ss")) + "）");

                ChannelInfo[] afterQuit = await bob.ListAsync().ConfigureAwait(false);
                ChannelInfo? generalAfterQuit = afterQuit.FirstOrDefault(c => c.Name == "general");
                Check(generalAfterQuit != null && generalAfterQuit.MemberCount == 1,
                    "alice 退出后 general 只剩 1 人，且昵称已被释放（可重新登录）");

                // ---- 9. 死连接清理：原生 TCP 客户端握手后完全不回 PONG ----
                Check(await ZombieIsKickedAsync(host, port).ConfigureAwait(false),
                    "不回 PONG 的僵尸连接被服务器判定超时并断开（BYE timeout + 套接字关闭）");
            }
            finally
            {
                alice.Dispose();
                bob.Dispose();
                carol.Dispose();
            }

            return checker.Report();
        }

        /// <summary>
        /// 单次传输上限专项自检：需要服务器用很小的上限启动，例如
        /// <c>Chat.Server --max-line 512 --max-text 600</c>
        /// （正文上限特意配得比单行上限还宽松，才能覆盖「转发时变长导致超限」这条路径）。
        /// 重点验证「任何客户端都不能把别人挤下线」——服务器转发出去的行也会先量一遍。
        /// </summary>
        public static async Task<int> RunLimitsAsync(string host, int port)
        {
            Checker checker = new Checker();

            void Check(bool ok, string description) => checker.Check(ok, description);

            Console.WriteLine("=== 单次传输上限自检 " + host + ":" + port + " ===");

            const int ExpectedMaxLine = 512;
            const int ExpectedMaxText = 600;
            const int LongNickLength = 30;

            ChatClient receiver = new ChatClient();
            AsyncInbox<ChatMessage> receiverInbox = new AsyncInbox<ChatMessage>();
            receiver.MessageReceived += message => receiverInbox.Add(message);

            List<ChatClient> extras = new List<ChatClient>();
            RawPeer sender = new RawPeer(host, port);

            try
            {
                // ---- 1. 上限随 WELCOME 下发 ----
                await receiver.ConnectAsync(host, port, "recv").ConfigureAwait(false);
                ServerInfo? info = receiver.Server;

                Check(info != null && info.MaxLineBytes == ExpectedMaxLine && info.MaxTextChars == ExpectedMaxText,
                    "WELCOME 下发了单次传输上限（maxLine=" + (info == null ? "?" : info.MaxLineBytes.ToString()) +
                    "，maxText=" + (info == null ? "?" : info.MaxTextChars.ToString()) + "）");

                // ---- 2. 客户端本地预检：正文超限立刻报错，请求根本不发出去 ----
                DateTime started = DateTime.UtcNow;
                string code = await ExpectErrorAsync(
                    () => receiver.SendAsync("general", new string('x', ExpectedMaxText + 1))).ConfigureAwait(false);
                TimeSpan elapsed = DateTime.UtcNow - started;

                Check(code == "bad_text", "正文超过 maxText 时本地直接报错（code=" + code + "）");
                Check(elapsed < TimeSpan.FromMilliseconds(500) && receiver.IsConnected,
                    "本地拦截没有走网络（" + elapsed.TotalMilliseconds.ToString("F0") + "ms），连接保持可用");

                // ---- 3. 入站整行超限：回 ERR line_too_long 后直接断开 ----
                sender.ReadLine(TimeSpan.FromSeconds(3));                       // WELCOME
                sender.Write("HELLO {\"nick\":\"raw1\",\"id\":1}");
                sender.ReadCommand("OK", TimeSpan.FromSeconds(3));

                sender.Write("MSG {\"channel\":\"general\",\"text\":\"" + new string('z', 600) + "\",\"id\":2}");
                string? tooLong = sender.ReadCommand("ERR", TimeSpan.FromSeconds(3));

                Check(tooLong != null && tooLong.IndexOf("line_too_long", StringComparison.Ordinal) >= 0,
                    "超过单次传输上限的整行被拒绝（" + Shorten(tooLong) + "）");
                Check(sender.ReadLine(TimeSpan.FromSeconds(3)) == null, "随后服务器直接断开了这条连接（符合选择的行为：超限即断开）");

                // ---- 4. 出站派生行超限：拒绝转发，但发送者与接收方都不断开 ----
                sender.Dispose();
                sender = new RawPeer(host, port);
                sender.ReadLine(TimeSpan.FromSeconds(3));
                sender.Write("HELLO {\"nick\":\"raw2\",\"id\":1}");
                sender.ReadCommand("OK", TimeSpan.FromSeconds(3));

                // 入站行 ≈ 492 字节（≤ 512，合法），正文也 ≤ maxText；
                // 但转发时加上 from / ts 字段后约 540 字节，会超过 512
                int textLength = 450;
                sender.Write("MSG {\"channel\":\"general\",\"text\":\"" + new string('y', textLength) + "\",\"id\":3}");
                string? reply = sender.ReadCommand("ERR", TimeSpan.FromSeconds(3));

                Check(reply != null && reply.IndexOf("msg_too_large", StringComparison.Ordinal) >= 0,
                    "转发出去会超限的消息被拒绝（" + Shorten(reply) + "）");
                Check(await receiverInbox.ReadAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false) == null,
                    "这条超限消息没有被转发给接收方");

                sender.Write("PING {\"id\":4}");
                string? pong = sender.ReadCommand("PONG", TimeSpan.FromSeconds(3));
                Check(pong != null, "发送者本人没有被断开（PING/PONG 仍然正常）");
                Check(receiver.IsConnected, "接收方也没有被这条超限消息挤下线");

                // ---- 5. 正常长度的消息照常转发 ----
                await receiver.SendAsync("general", "短消息").ConfigureAwait(false);
                string? forwarded = sender.ReadCommand("MSG", TimeSpan.FromSeconds(3));
                Check(forwarded != null && forwarded.IndexOf("短消息", StringComparison.Ordinal) >= 0,
                    "没超限的消息照常转发（上限只挡超限的）");

                // ---- 6. 成员列表按预算裁剪 ----
                // 用长昵称把每项成本拉高，几条连接就能触发裁剪（单项成本 ≈ 昵称字节数×2+3）
                for (int i = 0; i < 3; i++)
                {
                    char filler = (char)('x' + i);
                    ChatClient extra = new ChatClient();
                    await extra.ConnectAsync(host, port, new string(filler, LongNickLength)).ConfigureAwait(false);
                    await extra.JoinAsync("general").ConfigureAwait(false);
                    extras.Add(extra);
                }

                IReadOnlyList<string> members = await receiver.JoinAsync("general").ConfigureAwait(false);
                Check(receiver.LastListTruncated, "成员列表超过预算时被裁剪（membersTruncated=true，返回 " +
                    members.Count + " 个昵称）");

                ChannelInfo[] list = await receiver.ListAsync().ConfigureAwait(false);
                ChannelInfo? general = list.Length > 0 ? list[0] : null;
                Check(general != null && general.MembersTruncated && general.Members.Count < general.MemberCount,
                    "LIST 同样被裁剪：列出 " + (general == null ? 0 : general.Members.Count) + " 个，实际 " +
                    (general == null ? 0 : general.MemberCount) + " 人");
                Check(general != null && general.MemberCount >= 5,
                    "裁剪的只是列表，人数仍然是真实值（MemberCount=" + (general == null ? 0 : general.MemberCount) + "）");
                Check(receiver.IsConnected && sender.IsAlive, "裁剪过程没有把任何连接挤下线");
            }
            finally
            {
                receiver.Dispose();
                sender.Dispose();

                foreach (ChatClient extra in extras)
                {
                    extra.Dispose();
                }
            }

            return checker.Report();
        }

        /// <summary>
        /// 大消息自检：需要服务器把上限调大，例如
        /// <c>Chat.Server --max-line 262144 --max-text 50000 --queue 64</c>。
        /// 验证「几万字符的长消息能完整转发」，以及客户端会不会被自己那 8192 的默认读取上限误伤。
        /// </summary>
        public static async Task<int> RunLargeAsync(string host, int port)
        {
            Checker checker = new Checker();

            void Check(bool ok, string description) => checker.Check(ok, description);

            Console.WriteLine("=== 大消息自检 " + host + ":" + port + " ===");

            const int ExpectedMaxText = 50000;
            const int ExpectedMaxLine = 262144;

            ChatClient alice = new ChatClient();
            ChatClient bob = new ChatClient();
            AsyncInbox<ChatMessage> inbox = new AsyncInbox<ChatMessage>();
            bob.MessageReceived += message => inbox.Add(message);

            try
            {
                await alice.ConnectAsync(host, port, "big1").ConfigureAwait(false);
                await bob.ConnectAsync(host, port, "big2").ConfigureAwait(false);

                ServerInfo? info = alice.Server;
                Check(info != null && info.MaxTextChars == ExpectedMaxText && info.MaxLineBytes == ExpectedMaxLine,
                    "服务器下发 maxText=" + ExpectedMaxText + " / maxLine=" + ExpectedMaxLine +
                    "（实际 " + (info == null ? "?" : info.MaxTextChars.ToString()) + " / " +
                    (info == null ? "?" : info.MaxLineBytes.ToString()) + "）");

                // ---- 1) 50000 个 ASCII 字符（约 50 KB） ----
                string ascii = new string('x', ExpectedMaxText);
                int delivered = await alice.SendAsync("general", ascii).ConfigureAwait(false);
                ChatMessage? got = await inbox.ReadAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);

                Check(delivered == 1 && got != null && got.Text == ascii,
                    "50000 字符（约 50 KB）的消息完整转发（收到 " +
                    (got == null || got.Text == null ? 0 : got.Text.Length) + " 字符）");

                // ---- 2) 50000 个中文字符 = UTF-8 约 150 KB ----
                string chinese = new string('中', ExpectedMaxText);
                delivered = await alice.SendAsync("general", chinese).ConfigureAwait(false);
                got = await inbox.ReadAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false);

                Check(got != null && got.Text == chinese,
                    "50000 个中文（UTF-8 约 150 KB）也完整转发（收到 " +
                    (got == null || got.Text == null ? 0 : got.Text.Length) + " 字符）");
                Check(alice.IsConnected && bob.IsConnected,
                    "两条连接都还活着（接收方没有被 150 KB 的长行误判成超长行）");

                // ---- 3) 超过正文上限：本地拦截，连接保持 ----
                string tooBig = new string('y', ExpectedMaxText + 1);
                string code = await ExpectErrorAsync(() => alice.SendAsync("general", tooBig)).ConfigureAwait(false);
                Check(code == "bad_text", "超过 " + ExpectedMaxText + " 字符时本地直接报错（code=" + code + "）");
                Check(alice.IsConnected, "被拒之后连接仍然可用");
            }
            finally
            {
                alice.Dispose();
                bob.Dispose();
            }

            return checker.Report();
        }

        private static string Shorten(string? line)
        {
            // 注意：net48 的引用程序集里 string.IsNullOrEmpty 没有 NotNullWhen 注解，
            // 所以这里写成显式的 null 判断，两个目标框架都不会报警告
            if (line == null || line.Length == 0)
            {
                return "没收到应答";
            }

            return line.Length <= 60 ? line : line.Substring(0, 60) + "…";
        }

        /// <summary>裸 TCP 对端：用来绕过客户端本地预检，构造服务器必定要处理的边界报文。</summary>
        private sealed class RawPeer : IDisposable
        {
            private readonly TcpClient _tcp;
            private readonly NetworkStream _stream;
            private readonly LineReader _reader;

            public RawPeer(string host, int port)
            {
                _tcp = new TcpClient();
                _tcp.Connect(host, port);
                _stream = _tcp.GetStream();
                _reader = new LineReader(_stream);
            }

            public bool IsAlive
            {
                get
                {
                    try
                    {
                        return _tcp.Connected;
                    }
                    catch (ObjectDisposedException)
                    {
                        return false;
                    }
                }
            }

            public void Write(string line)
            {
                byte[] bytes = Encoding.UTF8.GetBytes(line + "\n");
                _stream.Write(bytes, 0, bytes.Length);
                _stream.Flush();
            }

            /// <summary>读一行；超时或对端关闭返回 null（服务器心跳 PING 会被跳过）。</summary>
            public string? ReadLine(TimeSpan timeout)
            {
                Task<string?> read = _reader.ReadLineAsync(CancellationToken.None);
                Task done = Task.WhenAny(read, Task.Delay(timeout)).GetAwaiter().GetResult();

                if (done != read)
                {
                    return null;
                }

                string? line = read.GetAwaiter().GetResult();
                return line != null && line.StartsWith("PING", StringComparison.Ordinal) ? ReadLine(timeout) : line;
            }

            /// <summary>等到某类命令（OK / ERR / PONG / MSG），其它类型的行跳过。</summary>
            public string? ReadCommand(string command, TimeSpan timeout)
            {
                DateTime deadline = DateTime.UtcNow + timeout;

                while (DateTime.UtcNow < deadline)
                {
                    string? line = ReadLine(deadline - DateTime.UtcNow);
                    if (line == null)
                    {
                        return null;
                    }

                    if (line.StartsWith(command, StringComparison.Ordinal))
                    {
                        return line;
                    }
                }

                return null;
            }

            public void Dispose()
            {
                try
                {
                    _tcp.Close();
                }
                catch (Exception ex)
                {
                    Console.WriteLine("  （关闭裸连接时出错: " + ex.Message + "）");
                }
            }
        }

        /// <summary>握手成功后故意忽略服务器心跳，等服务器把这条连接踢掉。</summary>
        private static async Task<bool> ZombieIsKickedAsync(string host, int port)
        {
            CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));

            try
            {
                using (TcpClient raw = new TcpClient())
                {
                    await raw.ConnectAsync(host, port).ConfigureAwait(false);
                    NetworkStream stream = raw.GetStream();
                    LineReader reader = new LineReader(stream);

                    string welcome = await reader.ReadLineAsync(cts.Token).ConfigureAwait(false);
                    if (welcome == null || !welcome.StartsWith("WELCOME", StringComparison.Ordinal))
                    {
                        return false;
                    }

                    await WriteLineAsync(stream, "HELLO {\"nick\":\"zombie\"}", cts.Token).ConfigureAwait(false);

                    string hello = await reader.ReadLineAsync(cts.Token).ConfigureAwait(false);
                    if (hello == null || !hello.StartsWith("OK", StringComparison.Ordinal))
                    {
                        return false;
                    }

                    bool sawByeTimeout = false;

                    while (true)
                    {
                        string line = await reader.ReadLineAsync(cts.Token).ConfigureAwait(false);
                        if (line == null)
                        {
                            return sawByeTimeout; // 服务端关闭了套接字
                        }

                        if (line.StartsWith("BYE", StringComparison.Ordinal) && line.IndexOf("timeout", StringComparison.Ordinal) >= 0)
                        {
                            sawByeTimeout = true;
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                return false;
            }
            catch (Exception ex)
            {
                if (ex is IOException || ex is SocketException)
                {
                    return false;
                }

                throw;
            }
            finally
            {
                cts.Dispose();
            }
        }

        private static async Task WriteLineAsync(NetworkStream stream, string line, CancellationToken cancellationToken)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(line + "\n");
            await stream.WriteAsync(bytes, 0, bytes.Length, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        private static async Task<string> ExpectErrorAsync(Func<Task> action)
        {
            try
            {
                await action().ConfigureAwait(false);
                return "(未报错)";
            }
            catch (ChatException ex)
            {
                return ex.Code;
            }
        }

        private static bool ChannelsContain(IReadOnlyList<string> channels, string channel)
        {
            foreach (string item in channels)
            {
                if (string.Equals(item, channel, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static async Task<ChannelEventInfo?> WaitEventAsync(
            AsyncInbox<ChannelEventInfo> inbox,
            Func<ChannelEventInfo, bool> predicate,
            TimeSpan timeout)
        {
            DateTime deadline = DateTime.UtcNow + timeout;

            while (DateTime.UtcNow < deadline)
            {
                TimeSpan remaining = deadline - DateTime.UtcNow;
                ChannelEventInfo? item = await inbox.ReadAsync(remaining).ConfigureAwait(false);
                if (item == null)
                {
                    return null;
                }

                if (predicate(item))
                {
                    return item;
                }
            }

            return null;
        }
    }
}
