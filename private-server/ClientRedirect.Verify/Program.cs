using OverField.PrivateServer;

var target = RedirectPolicy.ValidateTarget("http://127.0.0.1:8080");
int assertions = 0;
void Check(bool value, string description) { assertions++; if (!value) throw new Exception(description); }
foreach (var host in new[] { "dsp-prod-of.inutan.com:18881", "sdkapi-sz.inutan.com", "sdkapi-of.inutan.com" })
{
    Check(RedirectPolicy.TryRewrite($"https://{host}/dispatch/region_info?token=a%2Fb&version=1.1.12", target, out var result), host);
    Check(result == "http://127.0.0.1:8080/dispatch/region_info?token=a%2Fb&version=1.1.12", "Preserve encoded query and route");
}
foreach (var input in new[] { "http://sdkapi-of.inutan.com.evil.test/v1/user", "http://evil.test/sdkapi-of.inutan.com", "http://127.0.0.1:8080/", "http://cdn3-of.inutan.com/Resources", "file://sdkapi-of.inutan.com/a", "not a url", "http://user:pass@sdkapi-of.inutan.com/a" })
    Check(!RedirectPolicy.TryRewrite(input, target, out _), "Unexpected redirect: " + input);
foreach (var input in new[] { "file:///test", "http://user:password@localhost:8080", "http://localhost:8080/path", "http://localhost/?q=1" })
{
    bool rejected = false;
    try { RedirectPolicy.ValidateTarget(input); } catch (ArgumentException) { rejected = true; }
    Check(rejected, "Invalid target accepted: " + input);
}
Check(RedirectPolicy.TryRewrite("HTTP://SDKAPI-OF.INUTAN.COM/v1/test", target, out _), "Case insensitive host");
foreach (var initialized in new[] { false, true })
foreach (var status in new[] { 0, 1, 2, 3 })
foreach (var mode in new[] { -1, 0, 1, 2, 3 })
foreach (var version in new string?[] { null, "", " ", "1.1.12" })
    Check(LocalResourcePolicy.CanContinue(initialized, status, mode, version) ==
        (initialized && status == 2 && mode == 2 && version == "1.1.12"),
        "Local resource completion must require genuine initialized host package and manifest");
foreach (var missing in new[] { int.MinValue, -1, 0, 1, 2, 100, int.MaxValue })
    Check(LocalResourcePolicy.HasAllResources(missing) == (missing == 0),
        "Missing or invalid resource counts must stop local completion");
Console.WriteLine($"PASS: {assertions} redirect/local-resource policy assertions. Runtime IL2CPP hooks and asset completeness require game verification.");
