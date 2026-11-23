using Chat.Client;
using Il2CppInutan;
using MelonLoader;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace mod.st
{
    /// <summary>Host-owned connection. Modules borrow Client and serialize room controls with ChannelGate.</summary>
    public sealed partial class NetChater : IDisposable
    {
        public static NetChater Instance { get; private set; } = new();
        public const string PublicHost = "113.219.237.106";
        public const int PublicPort = 15602;
        private readonly object _state = new();
        private readonly ConcurrentQueue<Action> _main = new();
        private ChatClient? _client;
        private CancellationTokenSource? _connection;
        private Task _connectTask = Task.CompletedTask;
        private int _generation, _channelRevision;
        private string _activeHost = "";
        private int _activePort;
        private string _desiredChannel = "", _joinedChannel = "";
        private bool _channelBusy, _disposed;
        private DateTime _retryAt;
        public ChatClient? Client => _client;
        public SemaphoreSlim ChannelGate { get; } = new(1, 1);
        public string DesiredChannel => _desiredChannel;
        public int ChannelRevision => Volatile.Read(ref _channelRevision);
        public bool ChannelReady => IsConnected && !_channelBusy && _desiredChannel.Length > 0 &&
            _joinedChannel == _desiredChannel && Channels.Count == 1 && Channels[0] == _desiredChannel;
        public bool IsConnected => _client?.IsConnected == true;
        public bool IsConnecting { get; private set; }
        public string? Nick => _client?.Nick;
        public IReadOnlyList<string> Channels => _client?.Channels ?? Array.Empty<string>();
        public string Host = PublicHost;
        public int Port = PublicPort;
        public string BaseNick = "ModPlayer";
        public event Action<ChatMessage>? OnMessageReceived;
        public event Action<ChannelEventInfo>? OnChannelEvent;
        public event Action<bool, string>? OnConnectionChanged;
        public event Action? OnChannelReady;
        private bool Current(int generation, ChatClient client) => !_disposed && generation == Volatile.Read(ref _generation) && ReferenceEquals(_client, client);
        private void Queue(int generation, Action action) => _main.Enqueue(() => { if (!_disposed && generation == _generation) action(); });
        public static string HideEndpoint(string text) => (text ?? "").Replace(PublicHost + ":" + PublicPort, "公网服务器").Replace(PublicHost, "公网服务器");
        private static void Log(string text) => MelonLogger.Msg("[Chat] " + HideEndpoint(text));

        public void Connect(string? host = null, int? port = null, string? nick = null)
        {
            _ = ConnectObserved(host ?? Host, port ?? Port, nick ?? BaseNick);
        }
        private async Task ConnectObserved(string host, int port, string nick)
        {
            try { await ConnectAsync(host, port, nick).ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            catch (Exception e) { _main.Enqueue(() => Log("连接失败：" + e.Message)); }
        }
        public Task ConnectAsync(string host, int port, string nick, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(host) || port < 1 || port > 65535) throw new ArgumentException("服务器地址或端口无效");
            if (string.IsNullOrWhiteSpace(nick)) throw new ArgumentException("请进入游戏取得当前角色名后连接");
            host = host.Trim().Trim('[', ']');
            lock (_state)
            {
                if (_disposed) throw new ObjectDisposedException(nameof(NetChater));
                cancellationToken.ThrowIfCancellationRequested();
                if (string.Equals(_activeHost, host, StringComparison.OrdinalIgnoreCase) && _activePort == port)
                {
                    if (IsConnected) return Task.CompletedTask;
                    if (IsConnecting && !_connectTask.IsCompleted) return _connectTask.WaitAsync(cancellationToken);
                }
                Host = host; Port = port; BaseNick = nick;
                _activeHost = host; _activePort = port;
                InvalidateConnection();
                int generation = _generation;
                var lifetime = new CancellationTokenSource();
                _connection = lifetime;
                IsConnecting = true;
                _connectTask = Task.Run(() => ConnectCore(host, port, nick, generation, lifetime.Token));
                return _connectTask.WaitAsync(cancellationToken);
            }
        }
        private void InvalidateConnection()
        {
            Interlocked.Increment(ref _generation);
            Interlocked.Increment(ref _channelRevision);
            _joinedChannel = ""; _channelBusy = false; _retryAt = default;
            _connection?.Cancel();
            _connection?.Dispose(); _connection = null;
            var old = _client; _client = null; old?.Dispose();
            IsConnecting = false;
        }
        private async Task ConnectCore(string host, int port, string nick, int generation, CancellationToken token)
        {
            ChatClient? client = null;
            bool locked = false;
            try
            {
                await ChannelGate.WaitAsync(token).ConfigureAwait(false); locked = true;
                lock (_state)
                {
                    token.ThrowIfCancellationRequested();
                    if (_disposed || generation != _generation) throw new OperationCanceledException(token);
                    client = new ChatClient(); _client = client;
                }
                ChatClient connection = client;
                connection.MessageReceived += msg =>
                {
                    int revision = ChannelRevision;
                    Queue(generation, () => { if (Current(generation, connection) && revision == ChannelRevision && ChannelReady && msg.Channel == DesiredChannel) OnMessageReceived?.Invoke(msg); });
                };
                connection.ChannelEvent += evt =>
                {
                    int revision = ChannelRevision;
                    Queue(generation, () =>
                    {
                        if (!Current(generation, connection) || revision != ChannelRevision || !ChannelReady || evt.Channel != DesiredChannel) return;
                        // Legacy NetPlayer handlers use Add/indexers: suppress duplicate and already-removed roster events.
                        if (NetPlayer.root != null && ((evt.Type == "join" && NetPlayer.Instance.players.ContainsKey(evt.Nick)) ||
                            (evt.Type == "leave" && !NetPlayer.Instance.players.ContainsKey(evt.Nick)))) return;
                        OnChannelEvent?.Invoke(evt);
                    });
                };
                connection.Disconnected += reason => Queue(generation, () =>
                {
                    if (!Current(generation, connection)) return;
                    lock (_state) { if (Current(generation, connection)) InvalidateConnection(); }
                    ResetRoster();
                    OnConnectionChanged?.Invoke(false, HideEndpoint(reason));
                    Log("连接已断开：" + reason);
                });
                await connection.ConnectAsync(host, port, nick, token, nicknameRetryCount: 10).ConfigureAwait(false);
                Queue(generation, () => Log("已连接，昵称：" + connection.Nick + "；等待游戏频道同步"));
            }
            catch (Exception e)
            {
                lock (_state)
                {
                    if (generation == _generation) { _client = null; client?.Dispose(); }
                }
                Queue(generation, () => { ResetRoster(); OnConnectionChanged?.Invoke(false, HideEndpoint(e.Message)); });
                throw;
            }
            finally
            {
                if (locked) ChannelGate.Release();
                lock (_state) { if (generation == _generation) IsConnecting = false; }
            }
        }
        public void Disconnect()
        {
            int generation;
            lock (_state) { InvalidateConnection(); generation = _generation; }
            Queue(generation, () => { ResetRoster(); OnConnectionChanged?.Invoke(false, "主动断开"); Log("已断开连接"); });
        }
        /// <summary>Call on the Unity main thread every frame, even outside the home scene.</summary>
        public void Tick()
        {
            if (_disposed) return;
            string desired = IHomeSystem.sys == null ? "" : IHomeSystem.sys._channelId_k__BackingField.ToString();
            if (desired != _desiredChannel)
            {
                _desiredChannel = desired; _joinedChannel = "";
                Interlocked.Increment(ref _channelRevision); _retryAt = default;
                ResetRoster();
            }
            for (int i = 0; i < 256 && _main.TryDequeue(out var action); i++)
                try { action(); } catch (Exception e) { Log("事件处理失败：" + e.Message); }
            ChatClient? client = _client;
            if (client?.IsConnected != true || _channelBusy || DateTime.UtcNow < _retryAt || ChannelReady ||
                (desired.Length == 0 && client.Channels.Count == 0)) return;
            _channelBusy = true;
            int generation = _generation, revision = ChannelRevision;
            CancellationToken token = _connection?.Token ?? default;
            _ = SyncChannel(client, desired, generation, revision, token);
        }
        private async Task SyncChannel(ChatClient client, string desired, int generation, int revision, CancellationToken token)
        {
            bool locked = false;
            try
            {
                await ChannelGate.WaitAsync(token).ConfigureAwait(false); locked = true;
                if (!Current(generation, client) || revision != ChannelRevision) return;
                foreach (string channel in client.Channels.ToArray())
                    if (channel != desired) await client.LeaveAsync(channel, token).ConfigureAwait(false);
                if (!Current(generation, client) || revision != ChannelRevision) return;
                IReadOnlyList<string> members = Array.Empty<string>();
                if (desired.Length > 0)
                {
                    if (!client.Channels.Contains(desired)) members = await client.JoinAsync(desired, token).ConfigureAwait(false);
                    else members = (await client.ListAsync(token).ConfigureAwait(false)).FirstOrDefault(c => c.Name == desired)?.Members ?? Array.Empty<string>();
                }
                Queue(generation, () =>
                {
                    if (!Current(generation, client) || revision != ChannelRevision) return;
                    _joinedChannel = desired;
                    _channelBusy = false;
                    RebuildRoster(members);
                    if (desired.Length > 0)
                    {
                        Log("已连接，昵称：" + client.Nick + "；同步频道：" + desired);
                        OnConnectionChanged?.Invoke(true, "频道已同步"); OnChannelReady?.Invoke();
                    }
                });
            }
            catch (OperationCanceledException) { }
            catch (Exception e) { Queue(generation, () => { if (revision == ChannelRevision) Log("频道同步失败：" + e.Message); }); }
            finally
            {
                if (locked) ChannelGate.Release();
                Queue(generation, () => { _channelBusy = false; _retryAt = DateTime.UtcNow.AddSeconds(revision == ChannelRevision ? 1 : 0); });
            }
        }
        internal void RefreshRoster() { _joinedChannel = ""; _retryAt = default; }
        private static void ResetRoster() { if (NetPlayer.root != null) NetPlayer.Instance.reset(); }
        private static void RebuildRoster(IReadOnlyList<string> members)
        {
            NetPlayer.Instance.reset();
            foreach (string member in members.Distinct())
            {
                var go = new GameObject(member); go.transform.SetParent(NetPlayer.root.transform);
                NetPlayer.Instance.players[member] = go;
                NetPlayer.Instance.FollowplayersList.TryAdd(member, new());
            }
        }
        public async void Send(string text)
        {
            var client = _client; int generation = _generation, revision = ChannelRevision;
            if (client == null || !ChannelReady) { Log("频道尚未就绪，无法发送"); return; }
            try
            {
                await ChannelGate.WaitAsync().ConfigureAwait(false);
                try { if (Current(generation, client) && revision == ChannelRevision && ChannelReady) await client.SendAsync(_desiredChannel, text).ConfigureAwait(false); }
                finally { ChannelGate.Release(); }
            }
            catch (Exception e) { Queue(generation, () => Log("发送失败：" + e.Message)); }
        }
        // Compatibility: subscriptions are controlled exclusively by the game's current channel.
        public void Join(string channel) { Log("频道自动跟随游戏，无需手动加入"); }
        public void Leave(string channel) { Log("频道自动跟随游戏；使用 net disconnect 断开"); }
        public void Dispose()
        {
            lock (_state) { if (_disposed) return; _disposed = true; InvalidateConnection(); }
            while (_main.TryDequeue(out _)) { }
        }
    }
}
