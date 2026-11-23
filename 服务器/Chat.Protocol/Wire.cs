using System;
using System.Globalization;

#nullable disable

namespace Chat.Protocol
{
    /// <summary>
    /// 线上协议定义：TCP 之上的 UTF-8 文本行，每行是一条「命令 + 可选 JSON 负载」。
    /// <code>
    /// 客户端 -&gt; 服务器:  HELLO {"nick":"alice","id":1}
    /// 服务器 -&gt; 客户端:  OK    {"id":1,"nick":"alice","channels":["general"]}
    /// </code>
    /// 行分隔符统一为 <c>\n</c>，负载里不能出现换行。
    /// <para>本文件是共享源码：服务器和客户端各自把它编进自己的程序集，因此客户端 DLL 零外部依赖。</para>
    /// </summary>
    public static class Wire
    {
        /// <summary>协议版本。</summary>
        public const string Version = "1.0";

        /// <summary>服务器标识，出现在 WELCOME 里。</summary>
        public const string ServerName = "dsh-chat";

        /// <summary>默认端口。</summary>
        public const int DefaultPort = 9000;

        /// <summary>单次传输（一行）的字节上限默认值：256 KB，够放下 50000 字符的中文长消息。</summary>
        public const int DefaultLineBytes = 256 * 1024;

        /// <summary>协议允许的单行字节数硬上限（服务器的 <c>--max-line</c> 上限也是这个值）。</summary>
        public const int MaxSupportedLineBytes = 1024 * 1024;

        // ---------------------------------------------------------- 客户端 -> 服务器

        /// <summary><c>HELLO {"nick":"alice"}</c> —— 必须的第一条命令，成功后自动加入默认频道。</summary>
        public const string CmdHello = "HELLO";

        public const string CmdGameCreate = "GAME_CREATE";
        public const string CmdGameJoin = "GAME_JOIN";
        public const string CmdGameLeave = "GAME_LEAVE";
        public const string CmdGameSend = "GAME_SEND";
        public const string CmdGameMessage = "GAME_MSG";
        public const string CmdGameEvent = "GAME_EVENT";

        /// <summary><c>JOIN {"channel":"general"}</c> —— 加入频道。</summary>
        public const string CmdJoin = "JOIN";

        /// <summary><c>LEAVE {"channel":"general"}</c> —— 离开频道。</summary>
        public const string CmdLeave = "LEAVE";

        /// <summary><c>MSG {"channel":"general","text":"你好"}</c> —— 发消息，只转发给同频道的其他人。</summary>
        public const string CmdMsg = "MSG";

        /// <summary><c>LIST</c> —— 查询频道与成员。</summary>
        public const string CmdList = "LIST";

        /// <summary><c>PING {"id":9}</c> —— 应用层探活。</summary>
        public const string CmdPing = "PING";

        /// <summary><c>PONG</c> —— 应答服务器心跳。</summary>
        public const string CmdPong = "PONG";

        /// <summary><c>QUIT {"reason":"bye"}</c> —— 正常退出。</summary>
        public const string CmdQuit = "QUIT";

        // ---------------------------------------------------------- 服务器 -> 客户端

        /// <summary>连接建立后服务器发的第一行，携带心跳等参数。</summary>
        public const string CmdWelcome = "WELCOME";

        /// <summary>请求成功，负载里会回显请求的 <c>id</c>。</summary>
        public const string CmdOk = "OK";

        /// <summary>请求失败，负载为 <c>{"code":"..","msg":"..","id":9}</c>。</summary>
        public const string CmdErr = "ERR";

        /// <summary>频道事件，负载为 <c>{"type":"join|leave","channel":..,"nick":..,"ts":..}</c>。</summary>
        public const string CmdEvent = "EVENT";

        /// <summary>服务器即将断开连接，负载为 <c>{"reason":".."}</c>。</summary>
        public const string CmdBye = "BYE";

        /// <summary>把命令与负载序列化成一行（不含换行符）。<paramref name="payload"/> 为空或 Undefined 时只输出命令。</summary>
        public static string Serialize(string command, JsonValue payload = null)
        {
            if (string.IsNullOrEmpty(command))
            {
                throw new ArgumentException("命令不能为空", nameof(command));
            }

            if (payload == null || payload.Kind == JsonKind.Undefined)
            {
                return command;
            }

            return command + " " + payload.ToJson();
        }

        /// <summary>当前时间的 ISO-8601（往返格式）文本，用作协议里的 <c>ts</c> 字段。</summary>
        public static string TimestampNow()
        {
            return DateTimeOffset.UtcNow.ToString("o", CultureInfo.InvariantCulture);
        }

        /// <summary>解析一行。返回 false 表示这一行格式非法（<paramref name="error"/> 说明原因）。</summary>
        public static bool TryParse(string line, out string command, out JsonValue payload, out string error)
        {
            command = string.Empty;
            payload = JsonValue.Undefined;
            error = string.Empty;

            if (line == null)
            {
                error = "行为 null";
                return false;
            }

            string trimmed = line.Trim();
            if (trimmed.Length == 0)
            {
                error = "空行";
                return false;
            }

            int space = trimmed.IndexOf(' ');
            if (space < 0)
            {
                command = trimmed.ToUpperInvariant();
                return true;
            }

            command = trimmed.Substring(0, space).ToUpperInvariant();

            string tail = trimmed.Substring(space + 1).Trim();
            if (tail.Length == 0)
            {
                return true;
            }

            try
            {
                payload = JsonValue.Parse(tail);
                return true;
            }
            catch (FormatException ex)
            {
                error = "JSON 负载解析失败: " + ex.Message;
                return false;
            }
        }
    }
}
