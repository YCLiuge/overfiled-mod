using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Chat.Client;
using Chat.Protocol;

namespace Chat.Demo
{
    /// <summary>
    /// 命令行示例客户端：连上服务器后可以直接聊天。
    /// 它演示了 Chat.Client.dll 的全部用法（连接、加频道、发消息、收消息事件、列频道、探活、退出）。
    /// </summary>
    internal static class Program
    {
        private static async Task<int> Main(string[] args)
        {
            TryUseUtf8Console();

            Options options;
            try
            {
                options = Options.Parse(args);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("参数错误: " + ex.Message);
                return 2;
            }

            if (options.Help)
            {
                Options.PrintUsage();
                return 0;
            }

            if (options.SmokeGame)
            {
                return await GameSmokeTest.RunAsync(options.Host, options.Port).ConfigureAwait(false);
            }

            if (options.Smoke)
            {
                return await SmokeTest.RunAsync(options.Host, options.Port).ConfigureAwait(false);
            }

            if (options.SmokeLimits)
            {
                return await SmokeTest.RunLimitsAsync(options.Host, options.Port).ConfigureAwait(false);
            }

            if (options.SmokeLarge)
            {
                return await SmokeTest.RunLargeAsync(options.Host, options.Port).ConfigureAwait(false);
            }

            return await RunInteractiveAsync(options).ConfigureAwait(false);
        }

        private static async Task<int> RunInteractiveAsync(Options options)
        {
            CancellationTokenSource shutdown = new CancellationTokenSource();
            ConsoleCancelEventHandler onCancel = (sender, eventArgs) =>
            {
                eventArgs.Cancel = true; // 走优雅退出而不是直接杀进程
                shutdown.Cancel();
            };

            Console.CancelKeyPress += onCancel;

            try
            {
                using (ChatClient client = new ChatClient())
                {
                    client.MessageReceived += message => Console.WriteLine(
                        "[" + message.Timestamp.ToLocalTime().ToString("HH:mm:ss") + "] <" + message.From + "> #" + message.Channel + ": " + message.Text);

                    client.ChannelEvent += channelEvent => Console.WriteLine(
                        "* " + channelEvent.Nick + (channelEvent.Type == "join" ? " 加入 " : " 离开 ") + "#" + channelEvent.Channel +
                        (channelEvent.Reason == null ? string.Empty : "（" + channelEvent.Reason + "）"));

                    client.Disconnected += reason => Console.WriteLine("!! 连接结束: " + reason);

                    try
                    {
                        await client.ConnectAsync(options.Host, options.Port, options.Nick, shutdown.Token).ConfigureAwait(false);
                    }
                    catch (ChatException ex)
                    {
                        Console.Error.WriteLine("握手失败 [" + ex.Code + "] " + ex.Message);
                        return 1;
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine("无法连接 " + options.Host + ":" + options.Port + " —— " + ex.Message);
                        return 1;
                    }

                    string current = options.Channel;

                    if (!ChannelsContain(client.Channels, current))
                    {
                        try
                        {
                            await client.JoinAsync(current, shutdown.Token).ConfigureAwait(false);
                        }
                        catch (ChatException ex)
                        {
                            Console.Error.WriteLine("加入 #" + current + " 失败 [" + ex.Code + "] " + ex.Message);
                            return 1;
                        }
                    }

                    Console.WriteLine("已连接 " + options.Host + ":" + options.Port + "，昵称 " + client.Nick + "，当前频道 #" + current);
                    Console.WriteLine("服务器: " + client.Server);
                    Console.WriteLine("直接输入内容即发送到当前频道；/help 查看命令，/quit 退出。");

                    while (!shutdown.IsCancellationRequested && client.IsConnected)
                    {
                        string? input = Console.ReadLine();
                        if (input == null)
                        {
                            break;
                        }

                        input = input.Trim();
                        if (input.Length == 0)
                        {
                            continue;
                        }

                        if (input[0] != '/')
                        {
                            await SendAsync(client, current, input, shutdown.Token).ConfigureAwait(false);
                            continue;
                        }

                        string[] parts = input.Split(new char[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
                        string verb = parts[0].ToLowerInvariant();
                        string? argument = parts.Length > 1 ? parts[1].Trim() : null;

                        try
                        {
                            switch (verb)
                            {
                                case "/help":
                                    PrintHelp();
                                    break;

                                case "/join":
                                    if (argument == null)
                                    {
                                        Console.WriteLine("用法: /join <频道名>");
                                        break;
                                    }

                                    IReadOnlyList<string> members = await client.JoinAsync(argument, shutdown.Token).ConfigureAwait(false);
                                    current = argument.TrimStart('#');
                                    Console.WriteLine("已加入 #" + current + "，成员: " + string.Join(", ", members));
                                    break;

                                case "/leave":
                                    string target = argument == null ? current : argument.TrimStart('#');
                                    IReadOnlyList<string> left = await client.LeaveAsync(target, shutdown.Token).ConfigureAwait(false);
                                    Console.WriteLine("已离开 #" + target + "，剩余成员: " + string.Join(", ", left));
                                    break;

                                case "/switch":
                                    if (argument == null)
                                    {
                                        Console.WriteLine("用法: /switch <频道名>");
                                        break;
                                    }

                                    await client.JoinAsync(argument, shutdown.Token).ConfigureAwait(false);
                                    current = argument.TrimStart('#');
                                    Console.WriteLine("当前频道切换为 #" + current);
                                    break;

                                case "/list":
                                    ChannelInfo[] channels = await client.ListAsync(shutdown.Token).ConfigureAwait(false);
                                    if (channels.Length == 0)
                                    {
                                        Console.WriteLine("服务器上还没有任何频道");
                                        break;
                                    }

                                    foreach (ChannelInfo channel in channels)
                                    {
                                        Console.WriteLine("  " + channel);
                                    }

                                    break;

                                case "/ping":
                                    Console.WriteLine("  " + await client.PingAsync(shutdown.Token).ConfigureAwait(false) + "（往返正常）");
                                    break;

                                case "/who":
                                    Console.WriteLine("  昵称 " + client.Nick + "，所在频道: " + string.Join(", ", client.Channels));
                                    break;

                                case "/quit":
                                case "/exit":
                                    await client.QuitAsync("bye", CancellationToken.None).ConfigureAwait(false);
                                    return 0;

                                default:
                                    Console.WriteLine("未知命令 " + verb + "，输入 /help 查看帮助");
                                    break;
                            }
                        }
                        catch (ChatException ex)
                        {
                            Console.WriteLine("  ! 服务器拒绝 [" + ex.Code + "] " + ex.Message);
                        }
                        catch (Exception ex)
                        {
                            if (ex is TimeoutException || ex is IOException || ex is InvalidOperationException)
                            {
                                Console.WriteLine("  ! " + ex.Message);
                            }
                            else
                            {
                                throw;
                            }
                        }
                    }
                }
            }
            finally
            {
                Console.CancelKeyPress -= onCancel;
                shutdown.Dispose();
            }

            Console.WriteLine("已退出。");
            return 0;
        }

        private static async Task SendAsync(ChatClient client, string channel, string text, CancellationToken cancellationToken)
        {
            try
            {
                int delivered = await client.SendAsync(channel, text, cancellationToken).ConfigureAwait(false);
                Console.WriteLine("  → #" + channel + " 已投递给 " + delivered + " 人（不含自己）");
            }
            catch (ChatException ex)
            {
                Console.WriteLine("  ! 发送失败 [" + ex.Code + "] " + ex.Message);
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

        private static void PrintHelp()
        {
            Console.WriteLine("  /join <频道>     加入频道");
            Console.WriteLine("  /leave [频道]    离开频道（默认当前频道）");
            Console.WriteLine("  /switch <频道>   加入并切换当前频道");
            Console.WriteLine("  /list            列出服务器上所有频道与成员");
            Console.WriteLine("  /who             显示自己的昵称与所在频道");
            Console.WriteLine("  /ping            应用层探活");
            Console.WriteLine("  /quit            正常退出");
            Console.WriteLine("  其它输入         作为消息发送到当前频道");
        }

        private static void TryUseUtf8Console()
        {
            try
            {
                Console.OutputEncoding = Encoding.UTF8;
            }
            catch (IOException)
            {
                // 输出被重定向时不支持设置编码，忽略
            }
        }

        private sealed class Options
        {
            private static readonly Random Random = new Random();

            public string Host { get; private set; }

            public int Port { get; private set; }

            public string Nick { get; private set; }

            public string Channel { get; private set; }

            public bool Smoke { get; private set; }

            public bool SmokeGame { get; private set; }

            public bool SmokeLimits { get; private set; }

            public bool SmokeLarge { get; private set; }

            public bool Help { get; private set; }

            private Options()
            {
                Host = "127.0.0.1";
                Port = Wire.DefaultPort;
                Nick = "user" + Random.Next(100, 999).ToString();
                Channel = "general";
            }

            public static Options Parse(string[] args)
            {
                Options options = new Options();

                for (int i = 0; i < args.Length; i++)
                {
                    string arg = args[i];

                    string Value()
                    {
                        if (i + 1 >= args.Length)
                        {
                            throw new ArgumentException("参数 " + arg + " 缺少取值");
                        }

                        return args[++i];
                    }

                    switch (arg.ToLowerInvariant())
                    {
                        case "--host":
                            options.Host = Value();
                            break;
                        case "--port":
                            int port;
                            if (!int.TryParse(Value(), out port))
                            {
                                throw new ArgumentException("--port 需要整数");
                            }

                            options.Port = port;
                            break;
                        case "--nick":
                            options.Nick = Value();
                            break;
                        case "--channel":
                            options.Channel = Value().TrimStart('#');
                            break;
                        case "--smoke-game":
                            options.SmokeGame = true;
                            break;
                        case "--smoke":
                            options.Smoke = true;
                            break;
                        case "--smoke-limits":
                            options.SmokeLimits = true;
                            break;
                        case "--smoke-large":
                            options.SmokeLarge = true;
                            break;
                        case "--help":
                        case "-h":
                            options.Help = true;
                            break;
                        default:
                            throw new ArgumentException("未知参数: " + arg);
                    }
                }

                return options;
            }

            public static void PrintUsage()
            {
                Console.WriteLine("Chat.Demo —— 频道聊天客户端示例（引用 Chat.Client.dll）");
                Console.WriteLine();
                Console.WriteLine("用法: Chat.Demo [选项]");
                Console.WriteLine();
                Console.WriteLine("  --host <地址>     服务器地址，默认 127.0.0.1");
                Console.WriteLine("  --port <端口>     服务器端口，默认 " + Wire.DefaultPort);
                Console.WriteLine("  --nick <昵称>     登录昵称，默认随机");
                Console.WriteLine("  --channel <频道>  进入的频道，默认 general");
                Console.WriteLine("  --smoke           跑一遍端到端自检（需要服务器已在运行），全部通过时退出码为 0");
                Console.WriteLine("  --smoke-limits    单次传输上限专项自检（需服务器以 --max-line 512 --max-text 600 启动）");
                Console.WriteLine("  --smoke-large     大消息自检（默认配置即可，验证 5 万字符的长消息）");
                Console.WriteLine("  --smoke-game      游戏房间身份、私密转发、容量、断线关闭专项自检");
                Console.WriteLine("  --help            显示本帮助");
            }
        }
    }
}
