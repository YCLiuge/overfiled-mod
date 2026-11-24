#if TUNNEL_VERIFY
using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OverField.PrivateServer;

internal static class TunnelCompatibility
{
    private static readonly string Key = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    private static readonly List<string> Logs = new();
    private static int checks, backendConnections;
    private static void Check(bool condition, string message) { checks++; if (!condition) throw new Exception(message); }
    private static void Log(string message) { lock (Logs) Logs.Add(message); }
    private sealed class OwnedListener : TcpListener, IDisposable
    {
        public OwnedListener(int port) : base(IPAddress.Loopback, port) { Start(); }
        public void Dispose() => Stop();
    }
    private static OwnedListener Listen(int port) => new(port);
    private static async Task<TcpClient> Connect(int port)
    { var c = new TcpClient(); await c.ConnectAsync(IPAddress.Loopback, port); return c; }
    private static async Task<byte[]> ReadAll(NetworkStream s)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var data = new MemoryStream(); byte[] buffer = new byte[8192]; int n;
        while ((n = await s.ReadAsync(buffer, timeout.Token)) != 0) data.Write(buffer, 0, n);
        return data.ToArray();
    }
    private static async Task Backend(TcpListener listener, bool http, CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                var c = await listener.AcceptTcpClientAsync(token); Interlocked.Increment(ref backendConnections);
                _ = Task.Run(async () =>
                {
                    using (c) try
                    {
                        var s = c.GetStream();
                        if (http)
                        {
                            byte[] b = new byte[4096]; int n = await s.ReadAsync(b, token);
                            if (n > 0) await s.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: 7\r\nConnection: close\r\n\r\nHTTP-OK"), token);
                        }
                        else { byte[] b = new byte[8192]; int n; while ((n = await s.ReadAsync(b, token)) > 0) await s.WriteAsync(b.AsMemory(0, n), token); }
                    }
                    catch (Exception) when (token.IsCancellationRequested) { }
                });
            }
        }
        catch (Exception) when (token.IsCancellationRequested) { }
    }
    public static async Task<int> Main(string[] args)
    {
        string goTool = Path.GetFullPath(args.Length > 0 ? args[0] : "../runtime-tools/go/bin/go.exe");
        string temp = Path.GetFullPath(".tunnel-test"); Directory.CreateDirectory(temp);
        string config = Path.Combine(temp, "server.json");
        using var stop = new CancellationTokenSource();
        var backends = new[] { Listen(0), Listen(0), Listen(0) };
        int serverPort; using (var reserve = Listen(0)) serverPort = ((IPEndPoint)reserve.LocalEndpoint).Port;
        string remote = "127.0.0.1:" + serverPort;
        // Use the exact production protocol implementation, changing test addresses only.
        string source = await File.ReadAllTextAsync("../TcpBridge/main.go");
        source = source.Replace("127.0.0.1:10086", remote);
        string[] addresses = { "127.0.0.1:18080", "127.0.0.1:11000", "127.0.0.1:12000" };
        for (int i = 0; i < addresses.Length; i++) source = source.Replace(addresses[i], backends[i].LocalEndpoint.ToString());
        string fixtureSource = Path.Combine(temp, "main.go"); await File.WriteAllTextAsync(fixtureSource, source);
        string goExe = Path.Combine(temp, "tunnel-fixture.exe");
        var buildInfo = new ProcessStartInfo(goTool) { UseShellExecute = false, ArgumentList = { "build", "-o", goExe, fixtureSource } };
        buildInfo.Environment["GOCACHE"] = Path.GetFullPath("../TcpBridge/.gocache");
        string goTemp = Path.Combine(temp, "gotmp"); Directory.CreateDirectory(goTemp);
        buildInfo.Environment["GOTMPDIR"] = goTemp; buildInfo.Environment["GOTOOLCHAIN"] = "local";
        using (var build = Process.Start(buildInfo)!) { await build.WaitForExitAsync(); if (build.ExitCode != 0) throw new Exception("Go fixture build failed"); }
        await File.WriteAllTextAsync(config, JsonSerializer.Serialize(new { Mode = "server", Listen = remote, Key }));
        var jobs = backends.Select((l, i) => Backend(l, i == 0, stop.Token)).ToArray();
        using var server = Process.Start(new ProcessStartInfo(goExe) { UseShellExecute = false, ArgumentList = { "-config", config } }) ?? throw new Exception("Could not start Go server");
        try
        {
            for (int i = 0; ; i++)
            {
                try { using var ready = await Connect(serverPort); break; }
                catch when (i < 100 && !server.HasExited) { await Task.Delay(20); }
            }
            using (var tunnel = new TunnelClient(remote, Key, Log))
            {
                tunnel.Start(); tunnel.Start(); // idempotent
                foreach (int p in new[] { 11001, 12001 })
                {
                    using var c = await Connect(p); byte[] payload = RandomNumberGenerator.GetBytes(256 * 1024 + 37);
                    var send = Task.Run(async () => { await c.GetStream().WriteAsync(payload); c.Client.Shutdown(SocketShutdown.Send); });
                    byte[] received = await ReadAll(c.GetStream()); await send;
                    Check(payload.SequenceEqual(received), "Go/C# multi-record echo or half-close mismatch");
                }
                using var http = await Connect(18081);
                await http.GetStream().WriteAsync(Encoding.ASCII.GetBytes("GET /proxy-check HTTP/1.1\r\nHost: test\r\nConnection: close\r\n\r\n"));
                Check(Encoding.ASCII.GetString(await ReadAll(http.GetStream())).EndsWith("HTTP-OK"), "Go/C# HTTP route failed");
            }
            Check(backendConnections == 3, "Unexpected backend count");
            int before = backendConnections;
            using (var wrong = new TunnelClient(remote, new string('0', 64), Log))
            {
                wrong.Start(); using var c = await Connect(11001); await c.GetStream().WriteAsync(new byte[] { 1 });
                try { Check((await ReadAll(c.GetStream())).Length == 0, "Wrong key accepted"); } catch (IOException) { checks++; }
            }
            Check(backendConnections == before, "Wrong key dialed backend");
            using (var occupied = Listen(11001))
            using (var badBind = new TunnelClient(remote, Key, Log))
            {
                bool failed = false; try { badBind.Start(); } catch (IOException) { failed = true; }
                Check(failed, "Bind failure not synchronous"); using var freed = Listen(18081); checks++;
            }
            foreach (string attack in new[] { "oversize", "tamper", "truncated" }) await MaliciousServer(attack);
            await StalledDispose();
            lock (Logs) Check(Logs.All(l => !l.Contains(Key) && !l.Contains(new string('0', 64))), "Secret leaked to logs");
            Console.WriteLine($"PASS: {checks} C#/Go tunnel compatibility and lifecycle assertions.");
            return 0;
        }
        finally
        {
            stop.Cancel(); foreach (var l in backends) l.Stop(); await Task.WhenAll(jobs);
            if (!server.HasExited) server.Kill(true); await server.WaitForExitAsync();
            File.Delete(config);
        }
    }
    private static async Task Exact(Stream s, byte[] data) { int p = 0; while (p < data.Length) { int n = await s.ReadAsync(data.AsMemory(p)); if (n == 0) throw new IOException(); p += n; } }
    private static async Task MaliciousServer(string attack)
    {
        using var fake = Listen(0); int port = ((IPEndPoint)fake.LocalEndpoint).Port;
        var server = Task.Run(async () =>
        {
            using var c = await fake.AcceptTcpClientAsync(); var s = c.GetStream();
            byte[] hello = RandomNumberGenerator.GetBytes(44); Encoding.ASCII.GetBytes("OFTCP001").CopyTo(hello, 0); await s.WriteAsync(hello);
            byte[] client = new byte[36]; await Exact(s, client);
            byte[] header = new byte[4]; await Exact(s, header); await Exact(s, new byte[(int)BinaryPrimitives.ReadUInt32BigEndian(header)]);
            if (attack == "oversize") { BinaryPrimitives.WriteUInt32BigEndian(header, 32786); await s.WriteAsync(header); return; }
            if (attack == "truncated") { BinaryPrimitives.WriteUInt32BigEndian(header, 18); await s.WriteAsync(header); await s.WriteAsync(new byte[] { 1 }); return; }
            byte[] label = Encoding.ASCII.GetBytes("TcpBridge/v1/s2c");
            byte[] input = label.Concat(hello.Skip(8).Take(32)).Concat(client.Take(32)).ToArray();
            byte[] k = HMACSHA256.HashData(Convert.FromHexString(Key), input);
            using var aes = new AesGcm(k); byte[] packet = new byte[22], nonce = new byte[12]; Array.Copy(hello, 40, nonce, 0, 4);
            BinaryPrimitives.WriteUInt32BigEndian(packet, 18); aes.Encrypt(nonce, new byte[] { 4, 0 }, packet.AsSpan(4, 2), packet.AsSpan(6, 16), packet.AsSpan(0, 4)); packet[^1] ^= 1; await s.WriteAsync(packet);
        });
        using (var tunnel = new TunnelClient("127.0.0.1:" + port, Key, Log))
        {
            tunnel.Start(); using var local = await Connect(11001);
            try { Check((await ReadAll(local.GetStream())).Length == 0, "Malicious " + attack + " accepted"); } catch (IOException) { checks++; }
        }
        await server;
    }
    private static async Task StalledDispose()
    {
        using var fake = Listen(0); int port = ((IPEndPoint)fake.LocalEndpoint).Port;
        using var tunnel = new TunnelClient("127.0.0.1:" + port, Key, Log); tunnel.Start();
        using var local = await Connect(12001); using var remote = await fake.AcceptTcpClientAsync();
        tunnel.Dispose(); tunnel.Dispose();
        Check((await ReadAll(local.GetStream())).Length == 0, "Dispose left local connection open");
        Check((await ReadAll(remote.GetStream())).Length == 0, "Dispose left stalled handshake open");
        using var freed = Listen(12001); checks++;
    }
}
#endif
