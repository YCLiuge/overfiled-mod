using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Logic.Control
{
    // Transport and queue only. Game callbacks are invoked exclusively by Tick().
    internal sealed class AIControlHttpServer : IDisposable
    {
        private const int MaxQueue = 128;
        private const int MaxReceipts = 512;
        private readonly object _gate = new object();
        private readonly Queue<Work> _queue = new Queue<Work>();
        private readonly Dictionary<string, Work> _receipts = new Dictionary<string, Work>(StringComparer.Ordinal);
        private readonly Func<string, string, object> _execute;
        private readonly Func<object> _state;
        private readonly Func<object> _history;
        private readonly Func<string, object> _chat;
        private readonly Func<object> _catalog;
        private readonly Action<string> _log;
        private readonly string _version;
        private HttpListener _listener;
        private Thread _thread;
        private bool _running;
        private DateTimeOffset? _lastTick;
        public string SessionId { get; } = Guid.NewGuid().ToString("N");
        public int Port { get; private set; }
        public bool IsRunning { get { lock (_gate) return _running; } }

        public AIControlHttpServer(Func<string, string, object> execute, Func<object> state,
            Func<object> history, Func<string, object> chat, Func<object> catalog,
            string version, Action<string> log)
        {
            _execute = execute;
            _state = state;
            _history = history;
            _chat = chat;
            _catalog = catalog;
            _version = version;
            _log = log;
        }

        public void Start(int port)
        {
            if (port < 1 || port > 65535) throw new ArgumentOutOfRangeException(nameof(port));
            lock (_gate)
            {
                if (_running) return;
                var listener = new HttpListener();
                listener.Prefixes.Add($"http://127.0.0.1:{port}/api/");
                try { listener.Start(); }
                catch { listener.Close(); throw; }
                _listener = listener;
                Port = port;
                _running = true;
                _thread = new Thread(() => AcceptLoop(listener)) { IsBackground = true, Name = "AI_Control_HTTP" };
                _thread.Start();
            }
        }

        public void Dispose()
        {
            Thread thread;
            lock (_gate)
            {
                _running = false;
                CancelPending("模块卸载或服务停止，指令未执行");
                try { _listener?.Close(); } catch (HttpListenerException) { }
                thread = _thread;
            }
            if (thread != null && thread != Thread.CurrentThread) thread.Join(500);
        }

        private void AcceptLoop(HttpListener listener)
        {
            while (true)
            {
                try
                {
                    HttpListenerContext context = listener.GetContext();
                    _ = HandleAsync(context);
                }
                catch (Exception ex) when (ex is HttpListenerException || ex is ObjectDisposedException)
                {
                    lock (_gate)
                    {
                        if (!_running || listener != _listener) return;
                        _running = false;
                        CancelPending("HTTP 监听中断，指令未执行");
                    }
                    try { listener.Close(); } catch { }
                    _log("HTTP 监听异常，可通过游戏 api 指令重启: " + ex.Message);
                    return;
                }
            }
        }

        private async Task HandleAsync(HttpListenerContext context)
        {
            try
            {
                if (context.Request.RemoteEndPoint == null || !IPAddress.IsLoopback(context.Request.RemoteEndPoint.Address))
                    throw new ApiError(403, "只允许本机调用");
                // No browser CORS access; command clients send application/json without Origin.
                if (!string.IsNullOrEmpty(context.Request.Headers["Origin"]))
                    throw new ApiError(403, "此接口供本机指令客户端使用，不接受浏览器跨站请求");
                string path = context.Request.Url.AbsolutePath.TrimEnd('/');
                string method = context.Request.HttpMethod;
                if (path == "/api/health")
                {
                    RequireMethod(method, "GET");
                    object health;
                    lock (_gate)
                    {
                        health = new { protocolVersion = 1, moduleVersion = _version, running = _running,
                            port = Port, pending = _queue.Count, lastTickAt = _lastTick,
                            mainThreadActive = _lastTick.HasValue && (DateTimeOffset.UtcNow - _lastTick.Value).TotalSeconds < 3 };
                    }
                    await WriteAsync(context, 200, "success", health);
                    return;
                }
                if (path == "/api/commands")
                {
                    RequireMethod(method, "GET");
                    await WriteAsync(context, 200, "success", _catalog());
                    return;
                }
                if (path.StartsWith("/api/commands/", StringComparison.Ordinal))
                {
                    RequireMethod(method, "GET");
                    string id = Uri.UnescapeDataString(path.Substring("/api/commands/".Length));
                    object receipt;
                    lock (_gate)
                    {
                        if (!_receipts.TryGetValue(id, out Work found)) throw new ApiError(404, "回执不存在或已过期；模块重载后 sessionId 会改变");
                        Expire(found);
                        receipt = Snapshot(found);
                    }
                    await WriteAsync(context, 200, "success", receipt);
                    return;
                }

                Work work;
                int waitMs = 1500;
                if (path == "/api/command" || path == "/api/chat")
                {
                    RequireMethod(method, "POST");
                    JObject body = await ReadJsonAsync(context.Request);
                    string id = OptionalString(body, "id");
                    if (body["waitMs"] != null)
                    {
                        if (body["waitMs"].Type != JTokenType.Integer || !int.TryParse(body["waitMs"].ToString(), out waitMs) || waitMs < 0 || waitMs > 5000)
                            throw new ApiError(400, "waitMs 必须是 0 到 5000 的整数");
                    }
                    if (path == "/api/command")
                    {
                        string command = OptionalString(body, "command");
                        string target = OptionalString(body, "target") ?? "auto";
                        if (string.IsNullOrWhiteSpace(command) || command.Length > 2048 || command.Any(char.IsControl))
                            throw new ApiError(400, "command 必须是 1 到 2048 字符的单行指令");
                        command = command.Trim();
                        if (target != "auto" && target != "logic" && target != "host")
                            throw new ApiError(400, "target 只能是 auto、logic 或 host");
                        work = Enqueue(id, "command", target, command, () => _execute(target, command));
                    }
                    else
                    {
                        string text = OptionalString(body, "context");
                        if (string.IsNullOrWhiteSpace(text)) throw new ApiError(400, "context 不能为空");
                        // Preserve the existing chat endpoint's 50 UTF-16 character limit.
                        if (text.Length > 50) text = text.Substring(0, char.IsHighSurrogate(text[49]) ? 49 : 50);
                        string message = text;
                        work = Enqueue(id, "chat", null, text, () => _chat(message));
                    }
                }
                else if (path == "/api/state" || path == "/api/history")
                {
                    RequireMethod(method, "GET");
                    work = Enqueue(null, path == "/api/state" ? "state" : "history", null, null,
                        path == "/api/state" ? _state : _history);
                }
                else throw new ApiError(404, "未知接口；使用 GET /api/commands 查看指令列表");

                if (waitMs > 0) await Task.WhenAny(work.Done.Task, Task.Delay(waitMs));
                int code;
                object data;
                lock (_gate)
                {
                    Expire(work);
                    code = work.Status == "completed" ? 200 : work.Status == "failed" ? 422 :
                        work.Status == "expired" ? 408 : work.Status == "cancelled" ? 503 : 202;
                    // Keep old history clients' successful data array shape.
                    data = work.Kind == "history" && code == 200 ? work.Result : Snapshot(work);
                }
                await WriteAsync(context, code, code == 202 ? "已排队，使用回执 ID 查询；不要用新 ID 重发" :
                    code == 200 ? "success" : "执行未完成，详情见回执", data);
            }
            catch (ApiError ex) { await TryWriteAsync(context, ex.Code, ex.Message); }
            catch (JsonException) { await TryWriteAsync(context, 400, "请求体必须是合法 JSON 对象"); }
            catch (Exception ex)
            {
                _log("HTTP 请求异常: " + ex.Message);
                await TryWriteAsync(context, 500, ex.Message);
            }
            finally { try { context.Response.Close(); } catch { } }
        }

        private Work Enqueue(string id, string kind, string target, string input, Func<object> action)
        {
            id = id ?? Guid.NewGuid().ToString("N");
            if (!Regex.IsMatch(id, "^[A-Za-z0-9_-]{1,80}$")) throw new ApiError(400, "id 只允许 1 到 80 位字母、数字、下划线、短横线");
            lock (_gate)
            {
                if (!_running) throw new ApiError(503, "服务已停止");
                if (_receipts.TryGetValue(id, out Work existing))
                {
                    if (existing.Kind != kind || existing.Target != target || existing.Input != input)
                        throw new ApiError(409, "同一个 id 已用于另一条请求");
                    Expire(existing);
                    return existing;
                }
                foreach (Work old in _receipts.Values.Where(w => w.Done.Task.IsCompleted)
                    .OrderBy(w => w.CreatedAt).ToArray())
                {
                    if (_receipts.Count < MaxReceipts && (DateTimeOffset.UtcNow - old.CreatedAt).TotalMinutes < 5) break;
                    _receipts.Remove(old.Id);
                }
                if (_queue.Count >= MaxQueue || _receipts.Count >= MaxReceipts)
                    throw new ApiError(429, "主线程队列已满，请等待游戏恢复后重试");
                var work = new Work { Id = id, Kind = kind, Target = target, Input = input, Action = action };
                _receipts.Add(id, work);
                _queue.Enqueue(work);
                return work;
            }
        }

        // Called on Unity's main thread. Bound the amount of work done in one frame.
        public void Tick()
        {
            var timer = Stopwatch.StartNew();
            lock (_gate) { if (!_running) return; _lastTick = DateTimeOffset.UtcNow; }
            for (int i = 0; i < 16 && timer.ElapsedMilliseconds < 5; i++)
            {
                Work work;
                lock (_gate)
                {
                    if (!_running || _queue.Count == 0) break;
                    work = _queue.Dequeue();
                    Expire(work);
                    if (work.Status != "queued") continue;
                    work.Status = "running";
                }
                try
                {
                    object result = work.Action();
                    lock (_gate) Finish(work, "completed", result, null);
                    if (work.Kind == "command") _log($"指令已分发 [{work.Id}]: {work.Input}");
                }
                catch (Exception ex)
                {
                    lock (_gate) Finish(work, "failed", null, ex.Message);
                    _log($"请求失败 [{work.Id}]: {ex.Message}");
                }
                finally { work.Action = null; }
            }
        }

        // Caller holds _gate. Drop delegates so stopped modules cannot be invoked later.
        private void CancelPending(string reason)
        {
            while (_queue.Count > 0)
            {
                Work work = _queue.Dequeue();
                if (work.Status == "queued") Finish(work, "cancelled", null, reason);
                work.Action = null;
            }
        }

        private static void Expire(Work work)
        {
            if (work.Status == "queued" && (DateTimeOffset.UtcNow - work.CreatedAt).TotalSeconds >= 15)
            {
                Finish(work, "expired", null, "主线程 15 秒内未执行；该请求已取消，不会稍后补执行");
                work.Action = null;
            }
        }
        private static void Finish(Work work, string status, object result, string error)
        {
            work.Status = status;
            work.Result = result;
            work.Error = error;
            work.CompletedAt = DateTimeOffset.UtcNow;
            work.Done.TrySetResult(true);
        }
        private static object Snapshot(Work w) => new { id = w.Id, kind = w.Kind, status = w.Status,
            target = w.Target, command = w.Input, createdAt = w.CreatedAt, completedAt = w.CompletedAt,
            result = w.Result, error = w.Error };
        private static string OptionalString(JObject body, string name)
        {
            JToken token = body[name];
            if (token == null) return null;
            if (token.Type != JTokenType.String) throw new ApiError(400, name + " 必须是字符串");
            return token.Value<string>();
        }
        private static void RequireMethod(string actual, string expected)
        {
            if (actual != expected) throw new ApiError(405, "该接口要求 " + expected);
        }
        private static async Task<JObject> ReadJsonAsync(HttpListenerRequest request)
        {
            if (request.ContentType == null || !request.ContentType.StartsWith("application/json", StringComparison.OrdinalIgnoreCase))
                throw new ApiError(415, "Content-Type 必须是 application/json");
            if (request.ContentLength64 > 16384) throw new ApiError(413, "JSON 请求体最多 16 KiB");
            using (var bytes = new MemoryStream())
            {
                var buffer = new byte[1024];
                int count;
                while ((count = await request.InputStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    if (bytes.Length + count > 16384) throw new ApiError(413, "JSON 请求体最多 16 KiB");
                    bytes.Write(buffer, 0, count);
                }
                bytes.Position = 0;
                using (var reader = new StreamReader(bytes, Encoding.UTF8, true))
                    return JObject.Parse(await reader.ReadToEndAsync());
            }
        }
        private Task TryWriteAsync(HttpListenerContext context, int code, string message)
        {
            return WriteAsync(context, code, message, null);
        }
        private async Task WriteAsync(HttpListenerContext context, int code, string message, object data)
        {
            try
            {
                byte[] bytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new { code, msg = message, data, sessionId = SessionId }));
                context.Response.StatusCode = code;
                context.Response.ContentType = "application/json; charset=utf-8";
                context.Response.ContentLength64 = bytes.Length;
                context.Response.Headers["Cache-Control"] = "no-store";
                await context.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
            }
            catch (Exception ex) when (ex is HttpListenerException || ex is ObjectDisposedException || ex is IOException) { }
        }
        private sealed class Work
        {
            public string Id, Kind, Target, Input;
            public string Status = "queued";
            public DateTimeOffset CreatedAt = DateTimeOffset.UtcNow;
            public DateTimeOffset? CompletedAt;
            public Func<object> Action;
            public object Result;
            public string Error;
            public readonly TaskCompletionSource<bool> Done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        private sealed class ApiError : Exception
        {
            public int Code { get; }
            public ApiError(int code, string message) : base(message) { Code = code; }
        }
    }
}
