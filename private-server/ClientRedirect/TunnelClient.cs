using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace OverField.PrivateServer;

// The wire format is shared with TcpBridge (OFTCP001); no HTTP/TLS transport.
internal sealed class TunnelClient : IDisposable
{
    private const int MaxPayload = 32768;
    private readonly string host;
    private readonly int port;
    private readonly byte[] key;
    private readonly Action<string> log;
    private readonly object gate = new();
    private readonly CancellationTokenSource shutdown = new();
    private readonly List<TcpListener> listeners = new();
    private readonly HashSet<TcpClient> connections = new();
    private readonly HashSet<Task> tasks = new();
    private bool started, disposed;

    public TunnelClient(string remote, string key, Action<string> log)
    {
        this.log = log ?? (_ => { });
        // Parse a TCP authority only. Never include input/config values in errors.
        if (remote == null || !Uri.TryCreate("tcp://" + remote, UriKind.Absolute, out var uri) ||
            uri.Port < 1 || uri.Port > 65535 || uri.AbsolutePath != "/" ||
            uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0)
            throw new ArgumentException("Tunnel remote must be host:port.");
        host = uri.DnsSafeHost;
        port = uri.Port;
        try { this.key = Convert.FromHexString(key ?? ""); }
        catch { throw new ArgumentException("Tunnel key must contain 64 hexadecimal characters."); }
        if (this.key.Length != 32)
            throw new ArgumentException("Tunnel key must contain 64 hexadecimal characters.");
    }

    // Bind every port synchronously: callers see errors before activating redirects.
    public void Start()
    {
        lock (gate)
        {
            if (disposed) throw new ObjectDisposedException(nameof(TunnelClient));
            if (started) return;
            try
            {
                foreach (int p in new[] { 18081, 11001, 12001 })
                {
                    var listener = new TcpListener(IPAddress.Loopback, p);
                    listener.Server.ExclusiveAddressUse = true;
                    listener.Start();
                    listeners.Add(listener);
                }
                started = true;
                string[] routes = { "http", "game", "log" };
                for (int i = 0; i < listeners.Count; i++) Track(AcceptLoop(listeners[i], routes[i]));
            }
            catch
            {
                foreach (var listener in listeners) listener.Stop();
                listeners.Clear();
                throw new IOException("Tunnel loopback listener could not bind; check local ports.");
            }
        }
    }

    private void Track(Task task)
    {
        lock (gate) tasks.Add(task);
        _ = task.ContinueWith(t =>
        {
            // Observe even an unexpected fault, without exposing configuration.
            if (t.IsFaulted) { _ = t.Exception; SafeLog("Tunnel background task stopped."); }
            lock (gate) tasks.Remove(t);
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    private void SafeLog(string message) { try { log(message); } catch { /* logging must not break shutdown */ } }
    private bool Register(TcpClient client)
    {
        lock (gate)
        {
            if (disposed) { client.Dispose(); return false; }
            connections.Add(client);
            return true;
        }
    }
    private void Release(TcpClient client) { lock (gate) connections.Remove(client); client.Dispose(); }

    private async Task AcceptLoop(TcpListener listener, string route)
    {
        try
        {
            while (!shutdown.IsCancellationRequested)
            {
                var local = await listener.AcceptTcpClientAsync(shutdown.Token).ConfigureAwait(false);
                if (!Register(local)) return;
                Track(Handle(local, route));
            }
        }
        catch (Exception) when (shutdown.IsCancellationRequested) { }
        catch { SafeLog("Tunnel listener stopped unexpectedly."); }
    }

    private async Task Handle(TcpClient local, string route)
    {
        var remote = new TcpClient();
        if (!Register(remote)) { Release(local); return; }
        try
        {
            using var connect = CancellationTokenSource.CreateLinkedTokenSource(shutdown.Token);
            connect.CancelAfter(TimeSpan.FromSeconds(5));
            await remote.ConnectAsync(host, port, connect.Token).ConfigureAwait(false);
            using var handshake = CancellationTokenSource.CreateLinkedTokenSource(shutdown.Token);
            handshake.CancelAfter(TimeSpan.FromSeconds(8));
            using var records = await Authenticate(remote.GetStream(), route, handshake.Token).ConfigureAwait(false);
            using var copying = CancellationTokenSource.CreateLinkedTokenSource(shutdown.Token);
            var outgoing = SendLocal(local, remote, records, copying.Token);
            var incoming = ReceiveRemote(local, records, copying.Token);
            try
            {
                var first = await Task.WhenAny(outgoing, incoming).ConfigureAwait(false);
                await first.ConfigureAwait(false);
                // A normal half-close may still have a final response to drain.
                copying.CancelAfter(TimeSpan.FromSeconds(30));
                await Task.WhenAll(outgoing, incoming).ConfigureAwait(false);
            }
            finally
            {
                copying.Cancel();
                remote.Close();
                local.Close();
                // Observe both pumps, including the one interrupted by peer errors.
                try { await Task.WhenAll(outgoing, incoming).ConfigureAwait(false); } catch { }
            }
        }
        catch (Exception) when (shutdown.IsCancellationRequested) { }
        catch { SafeLog("Tunnel " + route + ": connection, authentication or forwarding failed."); }
        finally { Release(remote); Release(local); }
    }

    private static async Task SendLocal(TcpClient local, TcpClient remote, Records records, CancellationToken token)
    {
        byte[] buffer = new byte[MaxPayload + 1];
        buffer[0] = 1;
        while (true)
        {
            int n = await local.GetStream().ReadAsync(buffer.AsMemory(1, MaxPayload), token).ConfigureAwait(false);
            if (n == 0) break;
            await records.Send(buffer.AsMemory(0, n + 1), token).ConfigureAwait(false);
        }
        await records.Send(new byte[] { 2 }, token).ConfigureAwait(false);
        remote.Client.Shutdown(SocketShutdown.Send);
    }

    private static async Task ReceiveRemote(TcpClient local, Records records, CancellationToken token)
    {
        while (true)
        {
            byte[] data = await records.Receive(token).ConfigureAwait(false);
            if (data.Length == 1 && data[0] == 2) { local.Client.Shutdown(SocketShutdown.Send); return; }
            if (data.Length < 2 || data[0] != 1) throw new IOException("Invalid tunnel data record.");
            await local.GetStream().WriteAsync(data.AsMemory(1), token).ConfigureAwait(false);
        }
    }

    private async Task<Records> Authenticate(NetworkStream stream, string route, CancellationToken token)
    {
        byte[] hello = new byte[44];
        await ReadExactly(stream, hello, token).ConfigureAwait(false);
        if (!hello.AsSpan(0, 8).SequenceEqual(Encoding.ASCII.GetBytes("OFTCP001")))
            throw new IOException("Unsupported tunnel protocol.");
        byte[] client = RandomNumberGenerator.GetBytes(36);
        await stream.WriteAsync(client, token).ConfigureAwait(false);
        byte[] sendKey = Derive(hello, client, "c2s");
        byte[] receiveKey = Derive(hello, client, "s2c");
        Records? records = null;
        try
        {
            records = new Records(stream, sendKey, receiveKey, client.AsSpan(32, 4).ToArray(), hello.AsSpan(40, 4).ToArray());
            byte[] request = new byte[2 + route.Length]; request[0] = 3;
            Encoding.ASCII.GetBytes(route).CopyTo(request, 2);
            await records.Send(request, token).ConfigureAwait(false);
            byte[] status = await records.Receive(token).ConfigureAwait(false);
            if (status.Length != 2 || status[0] != 4 || status[1] != 0)
                throw new IOException("Tunnel authentication or backend check failed.");
            return records;
        }
        catch { records?.Dispose(); throw; }
        finally { CryptographicOperations.ZeroMemory(sendKey); CryptographicOperations.ZeroMemory(receiveKey); }
    }

    private byte[] Derive(byte[] hello, byte[] client, string direction)
    {
        byte[] label = Encoding.ASCII.GetBytes("TcpBridge/v1/" + direction);
        byte[] input = new byte[label.Length + 64];
        label.CopyTo(input, 0); Array.Copy(hello, 8, input, label.Length, 32); Array.Copy(client, 0, input, label.Length + 32, 32);
        // Key is retained until Dispose; no connection startup can outlive zeroing under the gate.
        lock (gate) { if (disposed) throw new ObjectDisposedException(nameof(TunnelClient)); return HMACSHA256.HashData(key, input); }
    }

    private static async Task ReadExactly(Stream stream, Memory<byte> data, CancellationToken token)
    {
        while (data.Length != 0)
        {
            int n = await stream.ReadAsync(data, token).ConfigureAwait(false);
            if (n == 0) throw new IOException("Truncated tunnel record.");
            data = data[n..];
        }
    }

    private sealed class Records : IDisposable
    {
        private readonly NetworkStream stream;
        private readonly AesGcm send, receive;
        private readonly byte[] sendPrefix, receivePrefix;
        private ulong sendCounter, receiveCounter;
        public Records(NetworkStream stream, byte[] sendKey, byte[] receiveKey, byte[] sendPrefix, byte[] receivePrefix)
        { this.stream = stream; send = new AesGcm(sendKey); receive = new AesGcm(receiveKey); this.sendPrefix = sendPrefix; this.receivePrefix = receivePrefix; }
        private static byte[] Nonce(byte[] prefix, ulong counter)
        { byte[] nonce = new byte[12]; prefix.CopyTo(nonce, 0); BinaryPrimitives.WriteUInt64BigEndian(nonce.AsSpan(4), counter); return nonce; }
        // There is exactly one writer and one reader, including the handshake.
        public async Task Send(ReadOnlyMemory<byte> payload, CancellationToken token)
        {
            if (payload.Length < 1 || payload.Length > MaxPayload + 1 || sendCounter == ulong.MaxValue)
                throw new IOException("Tunnel record limit exceeded.");
            byte[] packet = new byte[4 + payload.Length + 16];
            BinaryPrimitives.WriteUInt32BigEndian(packet, (uint)(payload.Length + 16));
            send.Encrypt(Nonce(sendPrefix, sendCounter++), payload.Span, packet.AsSpan(4, payload.Length), packet.AsSpan(4 + payload.Length, 16), packet.AsSpan(0, 4));
            await stream.WriteAsync(packet, token).ConfigureAwait(false);
        }
        public async Task<byte[]> Receive(CancellationToken token)
        {
            byte[] header = new byte[4]; await ReadExactly(stream, header, token).ConfigureAwait(false);
            uint length = BinaryPrimitives.ReadUInt32BigEndian(header);
            if (length < 17 || length > MaxPayload + 17 || receiveCounter == ulong.MaxValue)
                throw new IOException("Invalid tunnel record length.");
            byte[] ciphertext = new byte[(int)length]; await ReadExactly(stream, ciphertext, token).ConfigureAwait(false);
            byte[] plaintext = new byte[ciphertext.Length - 16];
            receive.Decrypt(Nonce(receivePrefix, receiveCounter++), ciphertext.AsSpan(0, plaintext.Length), ciphertext.AsSpan(plaintext.Length, 16), plaintext, header);
            return plaintext;
        }
        public void Dispose() { send.Dispose(); receive.Dispose(); }
    }

    public void Dispose()
    {
        TcpListener[] listening;
        TcpClient[] connected;
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            listening = listeners.ToArray();
            connected = connections.ToArray();
            listeners.Clear();
            connections.Clear();
            CryptographicOperations.ZeroMemory(key);
        }
        // Socket cancellation may resume continuations; do it outside the gate.
        shutdown.Cancel();
        foreach (var listener in listening) listener.Stop();
        foreach (var client in connected) client.Dispose();
    }
}
