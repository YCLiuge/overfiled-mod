using System;
using System.Linq;
using System.Threading.Tasks;

namespace mod.st
{
    public sealed partial class NetChater
    {
        public string Status => HideEndpoint(IsConnecting ? "正在连接" : !IsConnected ? "未连接" :
            "已连接，昵称：" + Nick + (ChannelReady ? "；频道：" + DesiredChannel : "；等待游戏频道同步"));

        /// <summary>Run from the main-thread command dispatcher.</summary>
        public void Command(string[] args)
        {
            try
            {
                if (args.Length == 0)
                {
                    if (IsConnected || IsConnecting) Log(Status);
                    else Connect(nick: TipMsgCore.i.mainplayername);
                    return;
                }
                string verb = args[0].ToLowerInvariant();
                if (verb == "status" && args.Length == 1) { Log(Status); return; }
                if (verb == "disconnect" && args.Length == 1) { Disconnect(); return; }
                if (verb == "reconnect" && args.Length == 1)
                {
                    Disconnect(); Connect(nick: TipMsgCore.i.mainplayername); return;
                }
                if (verb == "public" && args.Length == 1) { Connect(PublicHost, PublicPort, TipMsgCore.i.mainplayername); return; }
                if (verb == "local" && args.Length == 1) { Connect("127.0.0.1", 9000, TipMsgCore.i.mainplayername); return; }
                if (verb == "connect")
                {
                    if (args.Length == 1) { Connect(nick: TipMsgCore.i.mainplayername); return; }
                    if (args.Length != 2) throw new ArgumentException("用法：net connect [主机:端口]；IPv6 使用 [地址]:端口");
                    ParseEndpoint(args[1], out string host, out int port);
                    Connect(host, port, TipMsgCore.i.mainplayername); return;
                }
                // Preserve the original `net host:port` command.
                if (args.Length == 1 && (args[0].Contains(':') || args[0].Contains('.')))
                {
                    ParseEndpoint(args[0], out string host, out int port);
                    Connect(host, port, TipMsgCore.i.mainplayername); return;
                }
                throw new ArgumentException("用法：net [public|local|connect [主机:端口]|disconnect|reconnect|status]");
            }
            catch (Exception e) { Log(HideEndpoint(e.Message)); }
        }
        public static void ParseEndpoint(string value, out string host, out int port)
        {
            if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("请输入服务器地址");
            string input = value.Trim();
            if (!input.Contains("://", StringComparison.Ordinal)) input = "tcp://" + input;
            if (!Uri.TryCreate(input, UriKind.Absolute, out var uri) ||
                !string.Equals(uri.Scheme, "tcp", StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(uri.Host) || uri.UserInfo.Length != 0 ||
                (uri.AbsolutePath != "" && uri.AbsolutePath != "/") || uri.Query.Length != 0 || uri.Fragment.Length != 0)
                throw new ArgumentException("服务器格式错误，示例：localhost:9000 或 [::1]:9000");
            host = uri.Host.Trim('[', ']');
            port = uri.Port == -1 ? (host.Equals(PublicHost, StringComparison.OrdinalIgnoreCase) ? PublicPort : 9000) : uri.Port;
            if (port < 1 || port > 65535) throw new ArgumentException("端口必须在 1–65535 范围内");
        }
    }
}
