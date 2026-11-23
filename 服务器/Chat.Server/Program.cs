using System.Text;
using Chat.Protocol;

namespace Chat.Server;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        TryUseUtf8Console();

        ServerOptions options;
        try
        {
            options = ServerOptions.Parse(args);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("参数错误: " + ex.Message);
            Console.Error.WriteLine();
            ServerOptions.PrintUsage();
            return 2;
        }

        if (options.ShowHelp)
        {
            ServerOptions.PrintUsage();
            return 0;
        }

        using CancellationTokenSource shutdown = new();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true; // 让 Ctrl+C 走优雅关闭，而不是直接杀进程
            Console.WriteLine();
            Log.Info("收到 Ctrl+C，准备关闭…");
            shutdown.Cancel();
        };

        ChatHub hub = new();
        ChatTcpServer server = new(options, hub);

        try
        {
            await server.RunAsync(shutdown.Token);
            return 0;
        }
        catch (Exception ex)
        {
            Log.Error("服务器异常退出: " + ex);
            return 1;
        }
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
}
