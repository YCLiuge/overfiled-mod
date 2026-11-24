namespace OverField.PrivateServer;

public static class RedirectPolicy
{
    private static readonly HashSet<string> Hosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "dsp-prod-of.inutan.com", "sdkapi-sz.inutan.com", "sdkapi-of.inutan.com"
    };

    public static Uri ValidateTarget(string target)
    {
        if (!Uri.TryCreate(target, UriKind.Absolute, out var uri) ||
            (uri.Scheme != "http" && uri.Scheme != "https") ||
            !string.IsNullOrEmpty(uri.UserInfo) || uri.AbsolutePath != "/" ||
            !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw new ArgumentException("BaseUrl must be an HTTP(S) origin, e.g. http://127.0.0.1:8080");
        return uri;
    }

    public static bool TryRewrite(string? original, Uri target, out string rewritten)
    {
        rewritten = original ?? "";
        if (!Uri.TryCreate(original, UriKind.Absolute, out var uri) ||
            (uri.Scheme != "http" && uri.Scheme != "https") || !Hosts.Contains(uri.Host) ||
            !string.IsNullOrEmpty(uri.UserInfo)) return false;
        // Keep the original escaped path and query; do not log SDK tokens or request bodies.
        rewritten = target.GetLeftPart(UriPartial.Authority) + uri.PathAndQuery + uri.Fragment;
        return rewritten != original;
    }
}
