using System;

namespace Chat.Client
{
    /// <summary>别人发到频道里的消息。</summary>
    public sealed class ChatMessage
    {
        /// <summary>创建一条消息。</summary>
        public ChatMessage(string channel, string from, string text, DateTimeOffset timestamp)
        {
            Channel = channel;
            From = from;
            Text = text;
            Timestamp = timestamp;
        }

        /// <summary>消息所在频道。</summary>
        public string Channel { get; private set; }

        /// <summary>发送者昵称。</summary>
        public string From { get; private set; }

        /// <summary>消息正文。</summary>
        public string Text { get; private set; }

        /// <summary>服务器打的时间戳（UTC）。</summary>
        public DateTimeOffset Timestamp { get; private set; }

        /// <summary>便于日志输出。</summary>
        public override string ToString()
        {
            return "<" + From + "> #" + Channel + ": " + Text;
        }
    }

    /// <summary>频道内有人加入或离开。</summary>
    public sealed class ChannelEventInfo
    {
        /// <summary>创建一个频道事件。</summary>
        public ChannelEventInfo(string type, string channel, string nick, DateTimeOffset timestamp, string reason)
        {
            Type = type;
            Channel = channel;
            Nick = nick;
            Timestamp = timestamp;
            Reason = reason;
        }

        /// <summary>事件类型：<c>join</c> 或 <c>leave</c>。</summary>
        public string Type { get; private set; }

        /// <summary>发生事件的频道。</summary>
        public string Channel { get; private set; }

        /// <summary>相关用户昵称。</summary>
        public string Nick { get; private set; }

        /// <summary>服务器打的时间戳（UTC）。</summary>
        public DateTimeOffset Timestamp { get; private set; }

        /// <summary>离开原因（仅离开事件有值，例如「对端关闭连接」「客户端主动退出」）。</summary>
        public string Reason { get; private set; }

        /// <summary>便于日志输出。</summary>
        public override string ToString()
        {
            return Nick + " " + Type + " #" + Channel;
        }
    }

    /// <summary><see cref="ChatClient.ListAsync"/> 返回的频道信息。</summary>
    public sealed class ChannelInfo
    {
        /// <summary>创建一个频道信息。</summary>
        public ChannelInfo(string name, int memberCount, System.Collections.Generic.IReadOnlyList<string> members, bool membersTruncated = false)
        {
            Name = name;
            MemberCount = memberCount;
            Members = members ?? new string[0];
            MembersTruncated = membersTruncated;
        }

        /// <summary>频道名（不含 <c>#</c>）。</summary>
        public string Name { get; private set; }

        /// <summary>成员数量（真实总数，即使 <see cref="Members"/> 被裁剪过也准确）。</summary>
        public int MemberCount { get; private set; }

        /// <summary>成员昵称列表。</summary>
        public System.Collections.Generic.IReadOnlyList<string> Members { get; private set; }

        /// <summary>成员列表是否被服务器的单次传输上限裁剪过（只列出了前一部分成员）。</summary>
        public bool MembersTruncated { get; private set; }

        /// <summary>便于日志输出。</summary>
        public override string ToString()
        {
            return "#" + Name + " (" + MemberCount + " 人" + (MembersTruncated ? "，成员列表已裁剪" : string.Empty) + "): " +
                   string.Join(", ", Members);
        }
    }

    /// <summary>服务器在 <c>WELCOME</c> 里下发的参数。</summary>
    public sealed class ServerInfo
    {
        /// <summary>创建一份服务器信息。</summary>
        public ServerInfo(
            string server,
            string version,
            int heartbeatSeconds,
            int timeoutSeconds,
            int handshakeTimeoutSeconds,
            string defaultChannel,
            int maxLineBytes = 0,
            int maxTextChars = 0)
        {
            Server = server;
            Version = version;
            HeartbeatSeconds = heartbeatSeconds;
            TimeoutSeconds = timeoutSeconds;
            HandshakeTimeoutSeconds = handshakeTimeoutSeconds;
            DefaultChannel = defaultChannel;
            MaxLineBytes = maxLineBytes;
            MaxTextChars = maxTextChars;
        }

        /// <summary>服务名。</summary>
        public string Server { get; private set; }

        /// <summary>协议版本。</summary>
        public string Version { get; private set; }

        /// <summary>服务器发心跳 PING 的间隔秒数；0 表示服务器没开心跳。</summary>
        public int HeartbeatSeconds { get; private set; }

        /// <summary>服务器判定死连接的静默秒数。</summary>
        public int TimeoutSeconds { get; private set; }

        /// <summary>握手限时秒数。</summary>
        public int HandshakeTimeoutSeconds { get; private set; }

        /// <summary>握手后自动加入的默认频道；空字符串表示没有。</summary>
        public string DefaultChannel { get; private set; }

        /// <summary>服务器的单次传输上限（一行请求/应答的最大字节数）；0 表示服务器没有下发。</summary>
        public int MaxLineBytes { get; private set; }

        /// <summary>服务器的单条消息正文上限（字符数）；0 表示服务器没有下发。</summary>
        public int MaxTextChars { get; private set; }

        /// <summary>便于日志输出。</summary>
        public override string ToString()
        {
            return Server + " v" + Version + "（心跳 " + HeartbeatSeconds + "s，超时 " + TimeoutSeconds + "s，默认频道 #" + DefaultChannel +
                   "，单次传输上限 " + MaxLineBytes + " 字节，正文上限 " + MaxTextChars + " 字符）";
        }
    }
}
