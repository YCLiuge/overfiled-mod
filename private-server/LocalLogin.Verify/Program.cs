using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OverField.PrivateServer;

// No server, credentials or test framework packages required. Every request goes through
// the production LocalLoginClient and a protocol-decoding HttpMessageHandler.
internal static class Program
{
    private const string Endpoint = "http://127.0.0.1:5000/sdk/";
    private const string Name = "fixture_账号+&= name";
    private const string Password = "fixture_password+&=密";
    private const string Uid = "10042";
    private static readonly string Root = Path.Combine(AppContext.BaseDirectory, "LocalLogin.Verify-" + Guid.NewGuid().ToString("N"));
    private static int passed, failed;

    public static async Task<int> Main()
    {
        Directory.CreateDirectory(Root);
        try
        {
            foreach (var mode in Enum.GetValues<LocalCheckLoginCrypto>())
            {
                await Run($"{mode}: login -> session check -> gateway token", () => LoginFlow(mode));
                await Run($"{mode}: autoLogin rotates and persists before failed check; new client resumes", () => Rotation(mode));
            }
            foreach (var result in new[] { "false", "\"true\"", "1", "null", "{}", "[]", "missing" })
            {
                await Run($"login result {result} rejected, account retained", () => RejectResult(result, false));
                await Run($"check result {result} rejected, rotated auth retained", () => RejectResult(result, true));
            }
            foreach (var failure in new[] { "network", "http", "json", "data", "identity", "missing-session" })
                await Run($"{failure} failure preserves original identity on retry", () => FailureRetainsAccount(failure));
            await Run("first-use transport failure persists same generated credentials on retry", FirstUseFailure);
            await Run("endpoint isolation rejects reuse without HTTP and permits separate files", EndpointIsolation);
            await Run("unbound login state and corrupt account fail without HTTP", InvalidAccounts);
            await Run("cancellation after auth response commits rotation before cancelled check", CancellationRotation);
            Console.WriteLine($"LocalLogin.Verify: {passed} passed, {failed} failed");
            return failed == 0 ? 0 : 1;
        }
        finally
        {
            // Root is generated exclusively by this test run; no user account path is accepted.
            Directory.Delete(Root, recursive: true);
        }
    }

    private static async Task Run(string label, Func<Task> test)
    {
        try { await test(); passed++; Console.WriteLine("PASS " + label); }
        catch (Exception ex) { failed++; Console.Error.WriteLine("FAIL " + label + ": " + ex); }
    }

    private static string NewPath() => Path.Combine(Root, Guid.NewGuid().ToString("N"), "account.json");
    private static void Seed(string path, string auth = "", string endpoint = Endpoint, string uid = "")
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(new { endpoint, username = Name, password = Password, auth_token = auth, uid }));
    }
    private static JsonElement Account(string path) => JsonDocument.Parse(File.ReadAllText(path)).RootElement.Clone();
    private static string Text(JsonElement obj, string key) => obj.GetProperty(key).GetString()!;
    private static void Equal<T>(T expected, T actual, string reason)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception(reason + $" (expected {expected}, got {actual})");
    }
    private static void Assert(bool value, string reason) { if (!value) throw new Exception(reason); }
    private static async Task Fails(Func<Task> action)
    {
        try { await action(); }
        catch (InvalidOperationException) { return; }
        throw new Exception("Expected InvalidOperationException");
    }
    private static HttpResponseMessage Reply(string json, HttpStatusCode code = HttpStatusCode.OK) => new(code) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    private static HttpResponseMessage Login(string auth = "auth-new", string session = "session-new", string name = Name, string uid = Uid) =>
        Reply(JsonSerializer.Serialize(new { result = true, data = new { authToken = auth, userData = new { uid, username = name, token = session } } }));
    private static HttpResponseMessage Check() => Reply(JsonSerializer.Serialize(new { result = true, data = new { uid = Uid, user_name = Name, user_token = "gateway-only" } }));
    private static void Identity(JsonElement account, string username = Name, string password = Password)
    {
        Equal(username, Text(account, "username"), "username retained");
        Equal(password, Text(account, "password"), "password retained");
    }
    private static void CheckRequest(Request request, string token = "session-new")
    {
        Equal("v2/users/checkLogin", request.Route, "check route");
        Equal(Uid, Text(request.Body, "uid"), "uid");
        Equal(Name, Text(request.Body, "user_name"), "user_name");
        Equal(token, Text(request.Body, "token"), "must use session token, not auth or gateway token");
        Equal("com.of.lolo", Text(request.Body, "package_name"), "package");
        Equal(1, request.Body.GetProperty("platform").GetInt32(), "platform");
        Equal(5, request.Body.EnumerateObject().Count(), "check fields");
    }

    private static async Task LoginFlow(LocalCheckLoginCrypto mode)
    {
        var path = NewPath(); Seed(path, endpoint: "");
        using var handler = new Fake(mode, r =>
        {
            if (r.Index == 1)
            {
                Equal("v1/user/loginByName", r.Route, "password route");
                Equal(Name, Text(r.Body, "username"), "username");
                Equal(Password, Text(r.Body, "password"), "password");
                Equal(Name, Text(r.Body, "deviceId"), "device ID");
                Equal("lolo", Text(r.Body, "productCode"), "product");
                Equal("lolo", Text(r.Body, "channelCode"), "channel");
                Equal("zh", Text(r.Body, "clientLang"), "language");
                Equal(1, r.Body.GetProperty("platform").GetInt32(), "platform");
                Equal(7, r.Body.EnumerateObject().Count(), "login fields");
                Identity(Account(path)); // credentials must exist before any transmission
                return Login();
            }
            CheckRequest(r);
            Equal("auth-new", Text(Account(path), "auth_token"), "auth committed before check");
            return Check();
        });
        using var http = new HttpClient(handler);
        var result = await new LocalLoginClient(Endpoint.TrimEnd('/'), path, http, mode).LoginAsync();
        Equal(2, handler.Count, "request count");
        Equal(Uid, result.Uid, "returned uid"); Equal(Name, result.UserName, "returned name");
        Equal("gateway-only", result.GatewayToken, "returned gateway token");
        Assert(!result.ToString()!.Contains("gateway-only"), "ToString leaks gateway token");
        var stored = Account(path); Identity(stored);
        Equal(Endpoint, Text(stored, "endpoint"), "normalized endpoint binding");
        Equal(Uid, Text(stored, "uid"), "persisted uid");
        Assert(!File.ReadAllText(path).Contains("session-new") && !File.ReadAllText(path).Contains("gateway-only"), "only auth token should persist");
    }

    private static async Task Rotation(LocalCheckLoginCrypto mode)
    {
        var path = NewPath(); Seed(path, "auth-old", uid: Uid);
        using var handler = new Fake(mode, r =>
        {
            if (r.Index == 1 || r.Index == 3)
            {
                Equal("v1/user/autoLogin", r.Route, "must not fall back to password login");
                Equal(r.Index == 1 ? "auth-old" : "auth-rotated", Text(r.Body, "auth_token"), "resume latest rotation");
                Equal(1, r.Body.EnumerateObject().Count(), "auto fields");
                return Login(r.Index == 1 ? "auth-rotated" : "auth-final", r.Index == 1 ? "session-rotated" : "session-final");
            }
            CheckRequest(r, r.Index == 2 ? "session-rotated" : "session-final");
            Equal(r.Index == 2 ? "auth-rotated" : "auth-final", Text(Account(path), "auth_token"), "rotation on disk before check handler executes");
            if (r.Index == 2) throw new HttpRequestException("fixture check failure");
            return Check();
        });
        using var http = new HttpClient(handler);
        await Fails(() => new LocalLoginClient(Endpoint, path, http, mode).LoginAsync());
        Equal(2, handler.Count, "no fallback after check failure"); Identity(Account(path));
        await new LocalLoginClient(Endpoint, path, http, mode).LoginAsync();
        Equal(4, handler.Count, "second instance resumes"); Equal("auth-final", Text(Account(path), "auth_token"), "final auth saved");
    }

    private static async Task RejectResult(string result, bool atCheck)
    {
        var path = NewPath(); Seed(path, "auth-old", uid: Uid);
        using var handler = new Fake(LocalCheckLoginCrypto.AesWithSign, r =>
        {
            if (atCheck && r.Index == 1) return Login();
            // Include otherwise valid data: rejection must be based on result's exact boolean type/value.
            var data = atCheck ? new { uid = Uid, user_name = Name, user_token = "gateway-only" } as object
                : new { authToken = "auth-new", userData = new { uid = Uid, username = Name, token = "session-new" } };
            return Reply("{" + (result == "missing" ? "" : "\"result\":" + result + ",") + "\"data\":" + JsonSerializer.Serialize(data) + "}");
        });
        using var http = new HttpClient(handler);
        await Fails(() => new LocalLoginClient(Endpoint, path, http).LoginAsync());
        Equal(atCheck ? 2 : 1, handler.Count, "rejection terminates flow"); Identity(Account(path));
        Equal(atCheck ? "auth-new" : "auth-old", Text(Account(path), "auth_token"), "retain correct auth token");
    }

    private static async Task FailureRetainsAccount(string failure)
    {
        var path = NewPath(); Seed(path, "auth-old", uid: Uid);
        using var handler = new Fake(LocalCheckLoginCrypto.AesWithSign, r =>
        {
            Equal("v1/user/autoLogin", r.Route, "failure may not create/fall back to another account");
            Equal("auth-old", Text(r.Body, "auth_token"), "failed restore retains old auth");
            return failure switch
            {
                "network" => throw new HttpRequestException("offline"),
                "http" => Reply("{}", HttpStatusCode.ServiceUnavailable),
                "json" => Reply("invalid-json"),
                "data" => Reply("{\"result\":true,\"data\":[]}"),
                "identity" => Login(name: "other-account"),
                _ => Reply(JsonSerializer.Serialize(new { result = true, data = new { authToken = "bad", userData = new { uid = Uid, username = Name } } }))
            };
        });
        using var http = new HttpClient(handler);
        for (var i = 0; i < 2; i++)
        {
            await Fails(() => new LocalLoginClient(Endpoint, path, http).LoginAsync());
            Identity(Account(path)); Equal(Uid, Text(Account(path), "uid"), "uid retained");
            Equal("auth-old", Text(Account(path), "auth_token"), "auth retained");
        }
        Equal(2, handler.Count, "one request each attempt, no fallback");
    }

    private static async Task FirstUseFailure()
    {
        var path = NewPath(); string? name = null, password = null;
        using var handler = new Fake(LocalCheckLoginCrypto.AesWithSign, r =>
        {
            Equal("v1/user/loginByName", r.Route, "first-use route");
            var account = Account(path);
            name ??= Text(r.Body, "username"); password ??= Text(r.Body, "password");
            Assert(name.StartsWith("local_") && name.Length == 30 && password.Length == 48, "generated credentials format");
            Identity(account, name, password);
            Equal(name, Text(r.Body, "username"), "same username retried"); Equal(password, Text(r.Body, "password"), "same password retried");
            throw new HttpRequestException("offline");
        });
        using var http = new HttpClient(handler);
        for (var i = 0; i < 2; i++) await Fails(() => new LocalLoginClient(Endpoint, path, http).LoginAsync());
        Equal(2, handler.Count, "retry count");
    }

    private static async Task EndpointIsolation()
    {
        var path = NewPath(); Seed(path, "auth-old", uid: Uid);
        var original = File.ReadAllText(path);
        using var handler = new Fake(LocalCheckLoginCrypto.AesWithSign, _ => throw new Exception("cross-endpoint request leaked"));
        using var http = new HttpClient(handler);
        foreach (var other in new[] { "http://127.0.0.1:5001/sdk/", "https://127.0.0.1:5000/sdk/", "http://127.0.0.1:5000/other/" })
            await Fails(() => new LocalLoginClient(other, path, http).LoginAsync());
        Equal(0, handler.Count, "endpoint mismatch blocked before HTTP"); Equal(original, File.ReadAllText(path), "mismatched endpoint leaves file untouched");
        // Distinct account files are independent even when their credentials are identical.
        var second = NewPath(); const string otherEndpoint = "http://127.0.0.1:5001/sdk/"; Seed(second, endpoint: otherEndpoint);
        using var otherHandler = new Fake(LocalCheckLoginCrypto.AesWithSign, r => r.Index == 1 ? Login() : Check(), otherEndpoint);
        using var otherHttp = new HttpClient(otherHandler);
        await new LocalLoginClient(otherEndpoint, second, otherHttp).LoginAsync();
        Equal(original, File.ReadAllText(path), "other endpoint cannot overwrite first account");
    }

    private static async Task InvalidAccounts()
    {
        using var handler = new Fake(LocalCheckLoginCrypto.AesWithSign, _ => throw new Exception("invalid account sent HTTP"));
        using var http = new HttpClient(handler);
        foreach (var content in new[] { "{", "null", "{}", "{\"username\":\"x\"}" })
        {
            var path = NewPath(); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, content);
            await Fails(() => new LocalLoginClient(Endpoint, path, http).LoginAsync());
            Equal(content, File.ReadAllText(path), "invalid account preserved");
        }
        foreach (var auth in new[] { "auth-old", "" })
        {
            var path = NewPath(); Seed(path, auth, endpoint: "", uid: auth.Length == 0 ? Uid : "");
            await Fails(() => new LocalLoginClient(Endpoint, path, http).LoginAsync());
        }
        Equal(0, handler.Count, "no HTTP for invalid state");
    }

    private static async Task CancellationRotation()
    {
        var path = NewPath(); Seed(path, "auth-old", uid: Uid); using var cts = new CancellationTokenSource();
        using var handler = new Fake(LocalCheckLoginCrypto.AesWithSign, r =>
        {
            if (r.Index == 1) return Login("auth-cancel");
            CheckRequest(r);
            Equal("auth-cancel", Text(Account(path), "auth_token"), "commit precedes cancelled check");
            cts.Cancel();
            throw new OperationCanceledException(cts.Token);
        });
        using var http = new HttpClient(handler);
        try { await new LocalLoginClient(Endpoint, path, http).LoginAsync(cts.Token); throw new Exception("expected cancellation"); }
        catch (OperationCanceledException) { }
        Equal(2, handler.Count, "cancellation does not trigger fallback");
        Identity(Account(path));
        Equal("auth-cancel", Text(Account(path), "auth_token"), "cancellation retains rotated auth");
    }

    private sealed record Request(int Index, string Route, JsonElement Body);
    private sealed class Fake : HttpMessageHandler
    {
        private readonly LocalCheckLoginCrypto mode; private readonly Func<Request, HttpResponseMessage> respond; private readonly Uri endpoint;
        public int Count { get; private set; }
        public Fake(LocalCheckLoginCrypto mode, Func<Request, HttpResponseMessage> respond, string endpoint = Endpoint)
            => (this.mode, this.respond, this.endpoint) = (mode, respond, new Uri(endpoint));
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Count++;
            Equal(HttpMethod.Post, request.Method, "HTTP method");
            Assert(request.RequestUri!.AbsoluteUri.StartsWith(endpoint.AbsoluteUri, StringComparison.Ordinal), "request endpoint/path");
            Equal("application/x-www-form-urlencoded", request.Content!.Headers.ContentType!.MediaType, "form content type");
            var route = endpoint.MakeRelativeUri(request.RequestUri).ToString();
            var wire = await request.Content.ReadAsStringAsync();
            var form = wire.Split('&').Select(p => p.Split('=', 2)).ToDictionary(p => Decode(p[0]), p => Decode(p[1]));
            bool check = route == "v2/users/checkLogin", des = check && mode == LocalCheckLoginCrypto.DesWithoutSign;
            Equal(des ? 1 : check ? 2 : 1, form.Count, "exact form fields");
            string encoded = form[des ? "json_data" : "data"];
            Assert(des || !encoded.Contains('='), "AES base64 must be unpadded");
            Assert(!des || encoded.Length % 4 == 0, "DES base64 uses standard padding");
            var ciphertext = Convert.FromBase64String(encoded.PadRight((encoded.Length + 3) / 4 * 4, '='));
            // Decode with explicit ECB block operations and manually verify/remove PKCS#7.
            // Do not call or reflect into the production encryption implementation.
            using SymmetricAlgorithm algorithm = des ? DES.Create() : Aes.Create();
            algorithm.Key = Encoding.ASCII.GetBytes(des ? "78143304" : "0b2a18e45d7df321");
            algorithm.Mode = CipherMode.ECB; algorithm.Padding = PaddingMode.None;
            using var decryptor = algorithm.CreateDecryptor();
            var padded = decryptor.TransformFinalBlock(ciphertext, 0, ciphertext.Length);
            int padding = padded[^1]; Assert(padding >= 1 && padding <= algorithm.BlockSize / 8, "PKCS7 length");
            Assert(padded.TakeLast(padding).All(b => b == padding), "PKCS7 bytes");
            var body = JsonDocument.Parse(padded.AsMemory(0, padded.Length - padding)).RootElement.Clone();
            if (check && !des)
            {
                // Lolo alg.SignData: sort keys, key=value&, append shared key, lowercase MD5.
                var canonical = string.Join("", body.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal).Select(p => p.Name + "=" + (p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() : p.Value.GetRawText()) + "&")) + "0b2a18e45d7df321";
                var expected = Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
                Equal(expected, form["sign"], "Lolo checkLogin signature");
            }
            return respond(new Request(Count, route, body));
        }
        private static string Decode(string value) => Uri.UnescapeDataString(value.Replace("+", " "));
    }
}
