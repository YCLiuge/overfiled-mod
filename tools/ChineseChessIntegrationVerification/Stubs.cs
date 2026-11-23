namespace MelonLoader
{
    public static class MelonLogger
    {
        public static readonly List<string> Errors = new();
        public static void Error(string text) { Errors.Add(text); Console.WriteLine(text); }
    }
}
namespace Chat.Protocol
{
    public sealed class ChatException : Exception
    {
        public string Code { get; }
        public ChatException(string code, string message) : base(message) { Code = code; }
        // Match production: ToString deliberately omits StackTrace.
        public override string ToString() => "ChatException(" + Code + "): " + Message;
    }
}
namespace UnityEngine
{
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 operator +(Vector3 a, Vector3 b) => new(a.x + b.x, a.y + b.y, a.z + b.z);
        public static Vector3 operator *(Vector3 a, float scale) => new(a.x * scale, a.y * scale, a.z * scale);
    }
    public sealed class Transform
    {
        public Vector3 position = new(10, 20, 30), forward = new(0, 0, 1), eulerAngles = new(0, 45, 0);
    }
}
namespace mod
{
    public sealed class Player { public UnityEngine.Transform transform = new(); }
    public sealed class TipMsgCore
    {
        public static TipMsgCore i = new();
        public Player? mainplayer = new();
        public string mainplayername = "local";
        public static event Action<string, string>? Tip_actions;
        public static void Emit(string name, string text) => Tip_actions?.Invoke(name, text);
        public static int Subscriptions => Tip_actions?.GetInvocationList().Length ?? 0;
    }
}
namespace mod.st
{
    public sealed class NetChater
    {
        public static NetChater Instance { get; set; } = new();
        public Chat.Client.ChatClient? Client;
        public bool ChannelReady = true;
        public string DesiredChannel = "test";
        public int ChannelRevision;
        public string Host = "probe-host";
        public int Port = 9000;
    }
}
namespace Chat.Client
{
    public sealed class BoardChannelMessage
    {
        public string Channel { get; }
        public string AuthenticatedId { get; }
        public string Name { get; }
        public string Text { get; }
        public BoardChannelMessage(string channel, string id, string name, string text)
        { Channel = channel; AuthenticatedId = id; Name = name; Text = text; }
    }
    public sealed class ChatClient
    {
        public static readonly List<ChatClient> Peers = new();
        public readonly List<string> Sent = new();
        public Exception? SendFailure;
        public Func<CancellationToken, Task>? SendOverride;
        public string SessionId { get; }
        public bool IsConnected { get; set; } = true;
        public string Channel = "test";
        public event Action<BoardChannelMessage>? BoardMessageReceived;
        public event Action<string>? Disconnected;
        public int Subscriptions => BoardMessageReceived?.GetInvocationList().Length ?? 0;
        public ChatClient(string id) { SessionId = id; Peers.Add(this); }
        public void Emit(string sender, string text, string? channel = null) =>
            BoardMessageReceived?.Invoke(new BoardChannelMessage(channel ?? Channel, sender, "Authenticated " + sender, text));
        public Task BroadcastBoardAsync(string channel, string text, CancellationToken token = default)
        {
            if (SendFailure != null) throw SendFailure;
            if (SendOverride != null) return SendOverride(token);
            token.ThrowIfCancellationRequested();
            if (!IsConnected) throw new InvalidOperationException("disconnected");
            Sent.Add(text);
            foreach (var peer in Peers.Where(p => p.IsConnected && p.Channel == channel).ToArray())
                peer.Emit(SessionId, text, channel);
            return Task.CompletedTask;
        }
        public void Drop() { IsConnected = false; Disconnected?.Invoke("test disconnect"); }
    }
}
