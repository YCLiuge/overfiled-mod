using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OverField.PrivateServer;

public enum LocalCheckLoginCrypto { AesWithSign, DesWithoutSign }

// Deliberately not a record: generated ToString must never print tokens.
public sealed class LocalLoginResult
{
    public string Uid { get; }
    public string UserName { get; }
    public string GatewayToken { get; }
    internal LocalLoginResult(string uid, string userName, string gatewayToken)
        => (Uid, UserName, GatewayToken) = (uid, userName, gatewayToken);
}

/// <summary>Local SDK login. The separate account JSON contains plaintext credentials;
/// keep it private. A rejected/failed restore never creates another account.</summary>
public sealed class LocalLoginClient
{
    private const string AesKey = "0b2a18e45d7df321";
    private static readonly HttpClient DefaultHttp = new() { Timeout = TimeSpan.FromSeconds(15) };
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> AccountLocks = new(StringComparer.OrdinalIgnoreCase);
    private static readonly JsonSerializerOptions AccountJson = new() { WriteIndented = true };
    private readonly Uri endpoint;
    private readonly string accountPath;
    private readonly HttpClient http;
    private readonly LocalCheckLoginCrypto crypto;

    /// <param name="endpoint">SDK HTTP(S) base URL, e.g. http://127.0.0.1:5000.</param>
    /// <param name="accountFilePath">Dedicated account JSON; prefill username/password to use an existing account.</param>
    /// <param name="httpClient">Optional caller-owned client, also usable with a fake HttpMessageHandler.</param>
    public LocalLoginClient(string endpoint, string accountFilePath, HttpClient? httpClient = null,
        LocalCheckLoginCrypto checkLoginCrypto = LocalCheckLoginCrypto.AesWithSign)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) ||
            (uri.Scheme != "http" && uri.Scheme != "https") || uri.UserInfo.Length != 0 ||
            uri.Query.Length != 0 || uri.Fragment.Length != 0)
            throw new ArgumentException("SDK endpoint must be an absolute HTTP(S) base URL.");
        if (!Enum.IsDefined(typeof(LocalCheckLoginCrypto), checkLoginCrypto))
            throw new ArgumentException("Unsupported checkLogin encryption mode.");
        this.endpoint = new Uri(uri.AbsoluteUri.TrimEnd('/') + "/");
        accountPath = Path.GetFullPath(accountFilePath);
        http = httpClient ?? DefaultHttp;
        crypto = checkLoginCrypto;
    }

    public async Task<LocalLoginResult> LoginAsync(CancellationToken cancellationToken = default)
    {
        var gate = AccountLocks.GetOrAdd(accountPath, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var account = await LoadAccountAsync(cancellationToken).ConfigureAwait(false);
            Dictionary<string, object> request;
            string route;
            if (!string.IsNullOrEmpty(account.AuthToken))
            {
                route = "v1/user/autoLogin";
                request = new() { ["auth_token"] = account.AuthToken };
            }
            else
            {
                route = "v1/user/loginByName";
                request = new()
                {
                    ["username"] = account.Username, ["password"] = account.Password,
                    ["platform"] = 1, ["productCode"] = "lolo", ["channelCode"] = "lolo",
                    ["clientLang"] = "zh", ["deviceId"] = account.Username
                };
            }
            var login = await PostAsync(route, request, false, cancellationToken).ConfigureAwait(false);
            var auth = RequiredString(login, "authToken");
            if (!login.TryGetProperty("userData", out var user)) throw InvalidResponse();
            var uid = RequiredString(user, "uid");
            var name = RequiredString(user, "username");
            var session = RequiredString(user, "token");
            if ((!string.IsNullOrEmpty(account.Uid) && account.Uid != uid) || account.Username != name)
                throw new InvalidOperationException("SDK returned a different account identity.");
            account.AuthToken = auth;
            account.Uid = uid;
            // Rotation invalidates the old auth token: commit BEFORE checkLogin or any cancellation point.
            await SaveAccountAsync(account, CancellationToken.None).ConfigureAwait(false);
            var check = await PostAsync("v2/users/checkLogin", new()
            {
                ["uid"] = uid, ["user_name"] = name, ["token"] = session,
                ["package_name"] = "com.of.lolo", ["platform"] = 1
            }, true, cancellationToken).ConfigureAwait(false);
            if (RequiredString(check, "uid") != uid || RequiredString(check, "user_name") != name)
                throw new InvalidOperationException("SDK returned a different account identity.");
            return new LocalLoginResult(uid, name, RequiredString(check, "user_token"));
        }
        finally { gate.Release(); }
    }

    private async Task<Account> LoadAccountAsync(CancellationToken ct)
    {
        Account account;
        if (File.Exists(accountPath))
        {
            try
            {
                account = JsonSerializer.Deserialize<Account>(await File.ReadAllTextAsync(accountPath, ct).ConfigureAwait(false))
                    ?? throw InvalidResponse();
            }
            catch (JsonException) { throw new InvalidOperationException("Local account JSON is invalid."); }
            if (string.IsNullOrWhiteSpace(account.Username) || string.IsNullOrWhiteSpace(account.Password))
                throw new InvalidOperationException("Local account requires both username and password.");
            if (string.IsNullOrEmpty(account.Endpoint))
            {
                if (!string.IsNullOrEmpty(account.AuthToken) || !string.IsNullOrEmpty(account.Uid))
                    throw new InvalidOperationException("Existing login state requires an endpoint binding.");
                account.Endpoint = endpoint.AbsoluteUri;
            }
            else if (account.Endpoint != endpoint.AbsoluteUri)
                throw new InvalidOperationException("Local account belongs to a different SDK endpoint. Use a separate account file.");
        }
        else
        {
            account = new Account
            {
                Endpoint = endpoint.AbsoluteUri,
                Username = "local_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(12)).ToLowerInvariant(),
                Password = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant()
            };
        }
        // Persist first-use credentials before sending: transport failures cannot change identity on retry.
        await SaveAccountAsync(account, ct).ConfigureAwait(false);
        return account;
    }

    private async Task SaveAccountAsync(Account account, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(accountPath)!);
        var temporary = accountPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(account, AccountJson), ct).ConfigureAwait(false);
            File.Move(temporary, accountPath, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private async Task<JsonElement> PostAsync(string route, Dictionary<string, object> body, bool check, CancellationToken ct)
    {
        var plain = JsonSerializer.SerializeToUtf8Bytes(body);
        var form = new Dictionary<string, string>();
        bool des = check && crypto == LocalCheckLoginCrypto.DesWithoutSign;
        using (SymmetricAlgorithm algorithm = des ? DES.Create() : Aes.Create())
        {
            algorithm.Key = Encoding.ASCII.GetBytes(des ? "78143304" : AesKey);
            algorithm.Mode = CipherMode.ECB;
            algorithm.Padding = PaddingMode.PKCS7;
            using var encryptor = algorithm.CreateEncryptor();
            var encoded = Convert.ToBase64String(encryptor.TransformFinalBlock(plain, 0, plain.Length));
            form[des ? "json_data" : "data"] = des ? encoded : encoded.TrimEnd('=');
        }
        if (check && !des)
        {
            var canonical = string.Concat(body.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => pair.Key + "=" + Convert.ToString(pair.Value, CultureInfo.InvariantCulture) + "&")) + AesKey;
            form["sign"] = Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
        }
        try
        {
            using var content = new FormUrlEncodedContent(form);
            using var response = await http.PostAsync(new Uri(endpoint, route), content, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) throw new InvalidOperationException("SDK HTTP request failed.");
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("result", out var result) ||
                result.ValueKind != JsonValueKind.True)
                throw new InvalidOperationException("SDK authentication was rejected.");
            if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object) throw InvalidResponse();
            return data.Clone();
        }
        catch (HttpRequestException) { throw new InvalidOperationException("SDK network request failed; the local account was retained."); }
        catch (JsonException) { throw InvalidResponse(); }
    }

    private static InvalidOperationException InvalidResponse() => new("SDK response is invalid.");
    private static string RequiredString(JsonElement parent, string property)
    {
        if (parent.ValueKind != JsonValueKind.Object || !parent.TryGetProperty(property, out var value) ||
            value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString())) throw InvalidResponse();
        return value.GetString()!;
    }

    private sealed class Account
    {
        [JsonPropertyName("endpoint")] public string Endpoint { get; set; } = "";
        [JsonPropertyName("username")] public string Username { get; set; } = "";
        [JsonPropertyName("password")] public string Password { get; set; } = "";
        [JsonPropertyName("auth_token")] public string AuthToken { get; set; } = "";
        [JsonPropertyName("uid")] public string Uid { get; set; } = "";
    }
}
