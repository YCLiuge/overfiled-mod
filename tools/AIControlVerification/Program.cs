using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using Logic.Control;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

// No game objects, game references, running-game requests, packages, or test framework.
internal static class Program
{
    private static int _passed;
    private static readonly List<string> Failures = new();
    private static readonly List<string> Observations = new();

    private static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        Console.WriteLine("AIControlHttpServer standalone HTTP verification (.NET 6)");
        Console.WriteLine($"Runtime: {Environment.Version}; framework: {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}; architecture: {System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}; UTC: {DateTimeOffset.UtcNow:O}");
        try
        {
            if (args.Contains("--core-smoke"))
            {
                var reservation = new TcpListener(IPAddress.Loopback, 0);
                int port;
                try { reservation.Start(); port = ((IPEndPoint)reservation.LocalEndpoint).Port; }
                finally { reservation.Stop(); }
                using var core = new AIControlHttpServer((_, _) => null, () => null, () => null,
                    _ => null, () => null, "smoke", Console.WriteLine);
                core.Start(port);
                Console.WriteLine($"PASS direct core startup at ephemeral port {port}");
                return 0;
            }
            if (args.Contains("--listener-smoke"))
            {
                // Isolates the OS/runtime listener from the actual transport source.
                var reservation = new TcpListener(IPAddress.Loopback, 0);
                int port;
                try { reservation.Start(); port = ((IPEndPoint)reservation.LocalEndpoint).Port; }
                finally { reservation.Stop(); }
                using var listener = new HttpListener();
                listener.Prefixes.Add($"http://127.0.0.1:{port}/api/");
                listener.Start();
                Console.WriteLine($"PASS raw System.Net.HttpListener startup at ephemeral port {port}");
                return 0;
            }
            return await Verify();
        }
        catch (Exception ex)
        {
            Console.WriteLine("STARTUP/INFRASTRUCTURE FAILURE: " + ex);
            return 1;
        }
    }

    private static async Task<int> Verify()
    {
        using var fixture = new Fixture();
        Console.WriteLine($"Own listener: {fixture.Client.BaseAddress}; session: {fixture.Server.SessionId}");
        await Run("health before/after designated Tick and static catalog", () => HealthAndCatalog(fixture));
        await Run("queued command, receipts, idempotency and conflict", () => QueueAndReceipts(fixture));
        await Run("waiting request executes only after observed enqueue and Tick", () => WaitingRequest(fixture));
        await Run("concurrent state/history responses remain correlated", () => ConcurrentReads(fixture));
        await Run("chat is queued and uses simulated main thread", () => Chat(fixture));
        await Run("HTTP validation and request size limits", () => Validation(fixture));
        await Run("callback exception returns 422 with failed receipt", () => CallbackException(fixture));
        await Run("PowerShell client health, command, duplicate and failure smoke", () => PowerShellClient(fixture));
        await Run("15-second expiration never executes later", () => Expiration(fixture));
        await Run("all game-facing callbacks ran on actual designated Tick thread", () => ThreadChecks(fixture));
        await Run("unexpected listener failure cancels work and allows same-port recovery", FailureRecovery);
        await Run("Dispose cancels queued work; fresh instance starts new session", () => StopAndRestart(fixture));
        Console.WriteLine($"\nRESULT: {_passed} passed, {Failures.Count} failed, {Observations.Count} implementation observations");
        foreach (var failure in Failures) Console.WriteLine("FAILURE: " + failure);
        foreach (var observation in Observations) Console.WriteLine("OBSERVATION: " + observation);
        return Failures.Count == 0 ? 0 : 1;
    }

    private static async Task Run(string name, Func<Task> test)
    {
        var timer = Stopwatch.StartNew();
        try
        {
            await test();
            _passed++;
            Console.WriteLine($"PASS {name} ({timer.ElapsedMilliseconds} ms)");
        }
        catch (Exception ex)
        {
            Failures.Add(name + ": " + ex.Message);
            Console.WriteLine($"FAIL {name}: {ex}");
        }
    }

    private static async Task HealthAndCatalog(Fixture f)
    {
        var before = await f.Get("health", 200);
        Equal(false, (bool)before.Data["mainThreadActive"], "no initial main thread activity");
        Equal(JTokenType.Null, before.Data["lastTickAt"].Type, "initial tick is null");
        Equal(0, (int)before.Data["pending"], "initial queue");
        Equal("verification-1.0", (string)before.Data["moduleVersion"], "injected version");
        Equal(1, (int)before.Data["protocolVersion"], "protocol");
        Equal(f.Port, (int)before.Data["port"], "listener port");
        Equal(true, (bool)before.Data["running"], "listener running");
        await f.Pump.Tick();
        var after = await f.Get("health", 200);
        Equal(true, (bool)after.Data["mainThreadActive"], "tick visible in health");
        Check(after.Data["lastTickAt"].Type != JTokenType.Null, "tick timestamp set");
        Equal(f.Server.SessionId, after.SessionId, "envelope session");
        var catalog = await f.Get("commands", 200);
        Equal("verification-catalog", (string)catalog.Data["marker"], "injected catalog");
        Equal("echo", (string)catalog.Data["commands"][0], "catalog commands");
        Equal(0, f.GameCalls.Count, "health/catalog do not invoke queued callbacks");
    }

    private static async Task QueueAndReceipts(Fixture f)
    {
        var baseline = f.Executions.Count;
        var accepted = await f.Post("command", new { id = "once", target = "logic", command = "echo once", waitMs = 0 }, 202);
        Equal("queued", (string)accepted.Data["status"], "accepted receipt");
        Equal("once", (string)accepted.Data["id"], "caller id");
        await f.WaitPending(1);
        Equal(baseline, f.Executions.Count, "HTTP thread did not execute queued command");
        var queued = await f.Get("commands/once", 200);
        Equal("queued", (string)queued.Data["status"], "query queued receipt");
        await f.Post("command", new { id = "once", target = "logic", command = "echo once", waitMs = 0 }, 202);
        await f.WaitPending(1);
        await f.Post("command", new { id = "once", target = "logic", command = "different", waitMs = 0 }, 409);
        await f.Post("command", new { id = "once", target = "host", command = "echo once", waitMs = 0 }, 409);
        await f.PumpUntilEmpty();
        Equal(baseline + 1, f.Executions.Count, "duplicate executes only once");
        var done = await f.Get("commands/once", 200);
        Equal("completed", (string)done.Data["status"], "completed receipt");
        Equal("logic", (string)done.Data["result"]["target"], "result target");
        Equal("echo once", (string)done.Data["result"]["command"], "result command");
        Check(done.Data["completedAt"].Type != JTokenType.Null, "completion timestamp");
        var replay = await f.Post("command", new { id = "once", target = "logic", command = "echo once", waitMs = 0 }, 200);
        Check(JToken.DeepEquals(done.Data, replay.Data), "completed duplicate returns same receipt");
        await f.Pump.Tick();
        Equal(baseline + 1, f.Executions.Count, "completed duplicate does not reexecute");
        await f.Get("commands/missing-receipt", 404);
    }

    private static async Task WaitingRequest(Fixture f)
    {
        var baseline = f.Executions.Count;
        var request = f.Post("command", new { id = "waiting", command = "echo waiting", waitMs = 5000 }, 200);
        await f.WaitPending(1);
        Check(!request.IsCompleted, "waiting request must stay pending before Tick");
        Equal(baseline, f.Executions.Count, "no execution before Tick");
        await f.PumpUntilEmpty();
        var response = await request;
        Equal("completed", (string)response.Data["status"], "waiting receipt completes");
        Equal("echo waiting", (string)response.Data["result"]["command"], "waiting result");
        Equal(baseline + 1, f.Executions.Count, "one execution");
    }

    private static async Task ConcurrentReads(Fixture f)
    {
        // Wait until every request is accepted before pumping; do not assume SendAsync enqueued it.
        const int pairs = 6;
        var callsBefore = f.GameCalls.Count;
        var states = Enumerable.Range(0, pairs).Select(_ => f.Get("state", 200)).ToArray();
        var histories = Enumerable.Range(0, pairs).Select(_ => f.Get("history", 200)).ToArray();
        await f.WaitPending(pairs * 2, TimeSpan.FromSeconds(1));
        Equal(callsBefore, f.GameCalls.Count, "concurrent read callbacks not on HTTP threads");
        await f.PumpUntilEmpty();
        var stateResults = await Task.WhenAll(states);
        var historyResults = await Task.WhenAll(histories);
        var receiptIds = new HashSet<string>();
        var callbackIds = new HashSet<int>();
        foreach (var state in stateResults)
        {
            Equal("state", (string)state.Data["kind"], "state receipt kind");
            Equal("completed", (string)state.Data["status"], "state completion");
            Equal("STATE", (string)state.Data["result"]["marker"], "state result never history");
            Check(receiptIds.Add((string)state.Data["id"]), "state receipt ids unique");
            Check(callbackIds.Add((int)state.Data["result"]["sequence"]), "state calls distinct");
            var receipt = await f.Get("commands/" + (string)state.Data["id"], 200);
            Check(JToken.DeepEquals(state.Data, receipt.Data), "state response matches its receipt");
        }
        foreach (var history in historyResults)
        {
            Equal(JTokenType.Array, history.Data.Type, "history preserves successful array shape");
            Equal("HISTORY", (string)history.Data[0]["marker"], "history never state result");
            Check(callbackIds.Add((int)history.Data[0]["sequence"]), "history calls distinct");
        }
        Equal(pairs * 2, callbackIds.Count, "every concurrent callback has its own payload");
    }

    private static async Task Chat(Fixture f)
    {
        var input = new string('x', 49) + "😀tail";
        var queued = await f.Post("chat", new { id = "chat", context = input, waitMs = 0 }, 202);
        Equal("queued", (string)queued.Data["status"], "chat queued");
        await f.PumpUntilEmpty();
        var done = await f.Get("commands/chat", 200);
        Equal("chat", (string)done.Data["kind"], "chat kind");
        Equal(new string('x', 49), (string)done.Data["result"]["text"], "chat avoids splitting surrogate at 50 UTF-16 chars");
        await f.Post("chat", new { id = "chat", context = input, waitMs = 0 }, 200);
        Equal(1, f.GameCalls.Count(c => c.Kind == "chat"), "chat duplicate does not reexecute");
    }

    private static async Task Validation(Fixture f)
    {
        var pendingBefore = (int)(await f.Get("health", 200)).Data["pending"];
        await f.Raw(HttpMethod.Post, "command", "{", "application/json", 400);
        await f.Raw(HttpMethod.Post, "command", "[]", "application/json", 400);
        await f.Raw(HttpMethod.Get, "command", null, null, 405);
        await f.Raw(HttpMethod.Post, "state", "{}", "application/json", 405);
        await f.Raw(HttpMethod.Post, "health", "{}", "application/json", 405);
        await f.Raw(HttpMethod.Post, "command", "{}", "text/plain", 415);
        await f.Raw(HttpMethod.Post, "command", null, null, 415);
        await f.Get("unknown-route", 404);
        await f.Post("command", new { command = "line1\nline2", waitMs = 0 }, 400);
        await f.Post("command", new { command = "line1\rline2", waitMs = 0 }, 400);
        await f.Post("command", new { command = "", waitMs = 0 }, 400);
        await f.Post("command", new { command = 123, waitMs = 0 }, 400);
        await f.Post("command", new { command = "echo", target = "invalid", waitMs = 0 }, 400);
        await f.Post("command", new { command = "echo", id = "illegal/id", waitMs = 0 }, 400);
        await f.Post("command", new { command = "echo", waitMs = -1 }, 400);
        await f.Post("command", new { command = "echo", waitMs = 5001 }, 400);
        await f.Post("command", new { command = "echo", waitMs = "0" }, 400);
        await f.Post("chat", new { context = " " }, 400);
        var huge = JsonConvert.SerializeObject(new { command = new string('a', 17000), waitMs = 0 });
        await f.Raw(HttpMethod.Post, "command", huge, "application/json", 413);
        await f.Raw(HttpMethod.Post, "command", huge, "application/json", 413, chunked: true);
        var rejectedOrigin = new HttpRequestMessage(HttpMethod.Get, "health");
        rejectedOrigin.Headers.Add("Origin", "https://example.invalid");
        await f.Send(rejectedOrigin, 403);
        Equal(pendingBefore, (int)(await f.Get("health", 200)).Data["pending"], "invalid requests never enqueue");

        // Regression probes for validation fixes made by the parent agent in production.
        await f.Post("command", new { id = "probe-edge-newline", command = "\necho probe\n", waitMs = 0 }, 400);
        var multi = JsonConvert.SerializeObject(new { id = "probe-utf8-limit", command = "echo utf8", padding = new string('界', 6000), waitMs = 0 });
        Check(Encoding.UTF8.GetByteCount(multi) > 16384 && multi.Length < 16384, "UTF-8 probe crosses byte limit only");
        await f.Raw(HttpMethod.Post, "command", multi, "application/json", 413, chunked: true);
        Equal(pendingBefore, (int)(await f.Get("health", 200)).Data["pending"], "regression probes never enqueue");
    }

    private static async Task CallbackException(Fixture f)
    {
        var pending = f.Post("command", new { id = "failure", command = "throw", waitMs = 5000 }, 422);
        await f.WaitPending(1);
        await f.PumpUntilEmpty();
        var response = await pending;
        Equal("failed", (string)response.Data["status"], "failed response receipt");
        Equal("intentional verification failure", (string)response.Data["error"], "callback error preserved");
        var receipt = await f.Get("commands/failure", 200);
        Check(JToken.DeepEquals(response.Data, receipt.Data), "failed receipt remains queryable");
        await f.Post("command", new { id = "failure", command = "throw", waitMs = 0 }, 422);
        Equal(1, f.Executions.Count(x => x.Command == "throw"), "failed duplicate not retried");
        Check(f.Logs.Any(x => x.Contains("intentional verification failure")), "injected logger records callback failure");
    }

    private static async Task PowerShellClient(Fixture f)
    {
        var script = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../Invoke-GameCommand.ps1"));
        Check(File.Exists(script), "workspace PowerShell client exists");
        Process StartClient(params string[] arguments)
        {
            var start = new ProcessStartInfo("powershell.exe") { UseShellExecute = false };
            // Deliberately inherit stdout/stderr: no child-process pipe capture.
            foreach (var argument in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", script,
                "-BaseUri", $"http://127.0.0.1:{f.Port}", "-TimeoutSeconds", "10" }.Concat(arguments))
                start.ArgumentList.Add(argument);
            return Process.Start(start) ?? throw new InvalidOperationException("could not start PowerShell client");
        }
        async Task<int> Exit(Process process)
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await process.WaitForExitAsync(deadline.Token); }
            catch
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
                throw;
            }
            return process.ExitCode;
        }
        using (var health = StartClient("-Action", "Health"))
            Equal(0, await Exit(health), "PowerShell health exit code");
        var before = f.Executions.Count;
        using (var command = StartClient("-Command", "client-smoke", "-RequestId", "client-smoke-id"))
        {
            await f.WaitPending(1, TimeSpan.FromSeconds(8));
            Equal(before, f.Executions.Count, "PowerShell request has not executed before Tick");
            await f.PumpUntilEmpty();
            Equal(0, await Exit(command), "PowerShell command exit code");
        }
        Equal(before + 1, f.Executions.Count, "PowerShell command executed exactly once");
        var receipt = await f.Get("commands/client-smoke-id", 200);
        Equal("client-smoke", (string)receipt.Data["result"]["command"], "PowerShell result belongs to its command");
        using (var duplicate = StartClient("-Command", "client-smoke", "-RequestId", "client-smoke-id"))
            Equal(0, await Exit(duplicate), "PowerShell duplicate returns completed receipt");
        Equal(before + 1, f.Executions.Count, "PowerShell same-id command never reexecutes");
        using (var failure = StartClient("-Command", "throw", "-RequestId", "client-failure-id"))
        {
            await f.WaitPending(1, TimeSpan.FromSeconds(8));
            await f.PumpUntilEmpty();
            Check(await Exit(failure) != 0, "PowerShell callback failure exits nonzero (expected error output above)");
        }
        var failed = await f.Get("commands/client-failure-id", 200);
        Equal("failed", (string)failed.Data["status"], "PowerShell failed command has failed receipt");
    }

    private static async Task Expiration(Fixture f)
    {
        var executionsBefore = f.Executions.Count;
        await f.Post("command", new { id = "expires", command = "never execute expired", waitMs = 0 }, 202);
        await f.WaitPending(1);
        Console.WriteLine("  Waiting 15.1 seconds asynchronously for real queue expiration...");
        await Task.Delay(TimeSpan.FromSeconds(15.1));
        var staleHealth = await f.Get("health", 200);
        Equal(false, (bool)staleHealth.Data["mainThreadActive"], "health reports stale main thread");
        var receipt = await f.Get("commands/expires", 200);
        Equal("expired", (string)receipt.Data["status"], "receipt expires without Tick");
        var replay = await f.Post("command", new { id = "expires", command = "never execute expired", waitMs = 0 }, 408);
        Equal("expired", (string)replay.Data["status"], "duplicate reports timeout");
        await f.PumpUntilEmpty();
        Equal(executionsBefore, f.Executions.Count, "expired action never executes when Tick resumes");
    }

    private static Task ThreadChecks(Fixture f)
    {
        Check(f.GameCalls.Count > 0, "callbacks recorded");
        Check(f.Pump.LastActualTickThreadId != 0, "actual Tick thread recorded");
        foreach (var call in f.GameCalls)
            Equal(f.Pump.LastActualTickThreadId, call.ThreadId, $"{call.Kind} callback runs on recorded Tick thread");
        Console.WriteLine($"  {f.GameCalls.Count} queued callbacks on Tick thread {f.Pump.LastActualTickThreadId}; catalog thread {f.CatalogThreadId} (direct metadata callback).");
        return Task.CompletedTask;
    }

    private static async Task FailureRecovery()
    {
        using var broken = new Fixture();
        await broken.Post("command", new { id = "listener-fault", command = "never after fault", waitMs = 0 }, 202);
        await broken.WaitPending(1);
        var field = typeof(AIControlHttpServer).GetField("_listener", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Check(field != null, "listener fault injection field exists");
        ((HttpListener)field.GetValue(broken.Server)).Close();
        var deadline = Stopwatch.StartNew();
        while (broken.Server.IsRunning && deadline.Elapsed < TimeSpan.FromSeconds(3)) await Task.Delay(5);
        Equal(false, broken.Server.IsRunning, "unexpected listener closure clears running flag");
        await broken.Pump.Tick();
        Equal(0, broken.Executions.Count, "listener fault never executes queued action");
        broken.Server.Dispose();
        var port = broken.Port;
        using var recovered = new Fixture(port);
        await recovered.Get("health", 200);
        Check(recovered.Server.SessionId != broken.Server.SessionId, "recovery has fresh session");
        await recovered.Get("commands/listener-fault", 404);
        await recovered.Post("command", new { id = "recovered", command = "after recovery", waitMs = 0 }, 202);
        await recovered.PumpUntilEmpty();
        Equal(1, recovered.Executions.Count, "replacement listener on same port executes work");
    }

    private static async Task StopAndRestart(Fixture f)
    {
        var before = f.Executions.Count;
        await f.Post("command", new { id = "stopped", command = "never execute stopped", waitMs = 0 }, 202);
        await f.WaitPending(1);
        var session = f.Server.SessionId;
        f.Server.Dispose();
        await f.Pump.Tick();
        Equal(before, f.Executions.Count, "Dispose prevents queued execution");
        // Briefly reopen this disposed instance solely to query its cancelled receipt.
        // A real module reload uses a fresh core instance, checked below.
        f.Server.Start(f.Port);
        var cancelled = await f.Get("commands/stopped", 200);
        Equal("cancelled", (string)cancelled.Data["status"], "queued receipt marked cancelled");
        await f.Post("command", new { id = "stopped", command = "never execute stopped", waitMs = 0 }, 503);
        await f.Pump.Tick();
        Equal(before, f.Executions.Count, "cancelled duplicate never executes");
        f.Server.Dispose();
        using var next = new Fixture();
        var health = await next.Get("health", 200);
        Check(session != health.SessionId, "fresh instance gets new session");
        Equal(false, (bool)health.Data["mainThreadActive"], "fresh instance tick state reset");
        await next.Get("commands/stopped", 404);
        await next.Post("command", new { id = "stopped", command = "fresh session", waitMs = 0 }, 202);
        await next.PumpUntilEmpty();
        Equal(1, next.Executions.Count, "same client id works independently in new session");
    }

    private static void Check(bool condition, string detail)
    {
        if (!condition) throw new InvalidOperationException(detail);
    }

    private static void Equal<T>(T expected, T actual, string detail)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{detail}: expected {expected}, actual {actual}");
    }

    private sealed record Reply(int Code, JObject Body)
    {
        public JToken Data => Body["data"];
        public string SessionId => (string)Body["sessionId"];
    }

    private sealed class Fixture : IDisposable
    {
        public AIControlHttpServer Server { get; }
        public TickPump Pump { get; }
        public HttpClient Client { get; }
        public int Port { get; }
        public ConcurrentQueue<(string Target, string Command)> Executions { get; } = new();
        public ConcurrentQueue<(string Kind, int ThreadId)> GameCalls { get; } = new();
        public ConcurrentQueue<string> Logs { get; } = new();
        public int CatalogThreadId;
        private int _sequence;

        public Fixture(int? requestedPort = null)
        {
            // Release port 0 reservation before HttpListener.Start (the normal unavoidable bind race).
            if (requestedPort.HasValue) Port = requestedPort.Value;
            else
            {
                var reservation = new TcpListener(IPAddress.Loopback, 0);
                try
                {
                    reservation.Start();
                    Port = ((IPEndPoint)reservation.LocalEndpoint).Port;
                }
                finally { reservation.Stop(); } // TcpListener is not IDisposable on .NET 6.
            }
            Server = new AIControlHttpServer(
                (target, command) =>
                {
                    Record("execute");
                    Executions.Enqueue((target, command));
                    if (command == "throw") throw new InvalidOperationException("intentional verification failure");
                    return new { target, command, threadId = Environment.CurrentManagedThreadId };
                },
                () => { Record("state"); return new { marker = "STATE", sequence = ++_sequence }; },
                () => { Record("history"); return new[] { new { marker = "HISTORY", sequence = ++_sequence } }; },
                text => { Record("chat"); return new { text }; },
                () => { CatalogThreadId = Environment.CurrentManagedThreadId; return new { marker = "verification-catalog", commands = new[] { "echo" } }; },
                "verification-1.0", Logs.Enqueue);
            Server.Start(Port);
            Pump = new TickPump(Server);
            Client = new HttpClient(new SocketsHttpHandler { UseProxy = false, MaxConnectionsPerServer = 64 })
            {
                BaseAddress = new Uri($"http://127.0.0.1:{Port}/api/"),
                Timeout = TimeSpan.FromSeconds(10)
            };
        }

        private void Record(string kind)
        {
            var thread = Environment.CurrentManagedThreadId;
            GameCalls.Enqueue((kind, thread));
            Equal(Pump.LastActualTickThreadId, thread, "callback must execute on designated Tick thread");
        }

        public Task<Reply> Get(string path, int? expected = null) => Send(new HttpRequestMessage(HttpMethod.Get, path), expected);

        public Task<Reply> Post(string path, object body, int? expected = null) =>
            Raw(HttpMethod.Post, path, JsonConvert.SerializeObject(body), "application/json", expected);

        public Task<Reply> Raw(HttpMethod method, string path, string body, string mediaType, int? expected = null, bool chunked = false)
        {
            var message = new HttpRequestMessage(method, path);
            if (body != null)
            {
                message.Content = chunked ? new ChunkedContent(body) : new ByteArrayContent(Encoding.UTF8.GetBytes(body));
                if (mediaType != null) message.Content.Headers.ContentType = new MediaTypeHeaderValue(mediaType);
                if (chunked) message.Headers.TransferEncodingChunked = true;
            }
            return Send(message, expected);
        }

        public async Task<Reply> Send(HttpRequestMessage request, int? expected = null)
        {
            using (request)
            using (var response = await Client.SendAsync(request))
            {
                var text = await response.Content.ReadAsStringAsync();
                var code = (int)response.StatusCode;
                if (expected.HasValue) Equal(expected.Value, code, $"{request.Method} {request.RequestUri}: {text}");
                var json = JObject.Parse(text);
                Equal(code, (int)json["code"], "envelope code matches HTTP status");
                Equal(Server.SessionId, (string)json["sessionId"], "response belongs to own server session");
                Check(response.Headers.CacheControl?.NoStore == true, "response is no-store");
                return new Reply(code, json);
            }
        }

        public async Task WaitPending(int count, TimeSpan? timeout = null)
        {
            var timer = Stopwatch.StartNew();
            do
            {
                var reply = await Get("health", 200);
                if ((int)reply.Data["pending"] == count) return;
                await Task.Delay(5);
            } while (timer.Elapsed < (timeout ?? TimeSpan.FromSeconds(3)));
            throw new TimeoutException($"health pending never reached {count}");
        }

        public async Task PumpUntilEmpty()
        {
            for (var i = 0; i < 128; i++)
            {
                await Pump.Tick();
                if ((int)(await Get("health", 200)).Data["pending"] == 0) return;
            }
            throw new TimeoutException("Tick did not drain queue");
        }

        public void Dispose()
        {
            Server.Dispose();
            Pump.Dispose();
            Client.Dispose();
        }
    }

    private sealed class TickPump : IDisposable
    {
        private readonly BlockingCollection<TaskCompletionSource<bool>> _requests = new();
        private readonly Thread _thread;
        public int LastActualTickThreadId;

        public TickPump(AIControlHttpServer server)
        {
            _thread = new Thread(() =>
            {
                foreach (var completion in _requests.GetConsumingEnumerable())
                {
                    try
                    {
                        LastActualTickThreadId = Environment.CurrentManagedThreadId;
                        server.Tick();
                        completion.SetResult(true);
                    }
                    catch (Exception ex) { completion.SetException(ex); }
                }
            }) { IsBackground = true, Name = "Verification_Simulated_Main" };
            _thread.Start();
        }

        public Task Tick()
        {
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _requests.Add(completion);
            return completion.Task;
        }

        public void Dispose()
        {
            _requests.CompleteAdding();
            Check(_thread.Join(5000), "simulated main thread terminates");
            _requests.Dispose();
        }
    }

    private sealed class ChunkedContent : HttpContent
    {
        private readonly byte[] _bytes;
        public ChunkedContent(string text) => _bytes = Encoding.UTF8.GetBytes(text);
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext context) => stream.WriteAsync(_bytes, 0, _bytes.Length);
    }
}
