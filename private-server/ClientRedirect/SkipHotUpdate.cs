using System.Reflection;
using HarmonyLib;
using MelonLoader;

namespace OverField.PrivateServer;

/// <summary>Optional cached-resource path. Install only when SkipHotUpdate is enabled.</summary>
internal static class SkipHotUpdate
{
    private const BindingFlags Members = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    private static PropertyInfo _package = null!, _initialized = null!, _status = null!, _packageMode = null!, _callback = null!, _origin = null!, _downloadCount = null!;
    private static MethodInfo _version = null!, _end = null!, _downloader = null!;

    internal static void Install(HarmonyLib.Harmony harmony)
    {
        // Host initialization already queries the sandbox version, loads its actual cached
        // manifest and initializes the cache. Do not force OfflinePlayMode: it selects the
        // older StreamingAssets manifest and can bypass SceneHotUpdateController entirely.
        var resources = Resolve("Il2Cpp.ResManager");
        var package = Resolve("Il2CppYooAsset.ResourcePackage");
        var hot = Resolve("Il2Cpp.HotUpdateSystem");
        var action = Resolve("Il2CppSystem.Action");
        var start = Method(hot, "StartHotUpdate", new[] { action });
        _package = Property(resources, "package");
        _initialized = Property(resources, "_Init");
        _status = Property(package, "InitializeStatus");
        _packageMode = Property(package, "_playMode");
        _callback = Property(hot, "_endCallBack");
        _origin = Property(hot, "_originVersion");
        _version = Method(package, "GetPackageVersion", Type.EmptyTypes);
        _end = Method(hot, "OnHotUpdateEnd", Type.EmptyTypes);
        _downloader = Method(package, "CreateResourceDownloader", new[] { typeof(int), typeof(int), typeof(int) });
        _downloadCount = Property(_downloader.ReturnType, "TotalDownloadCount");
        if (start.ReturnType != typeof(void) || !_callback.CanWrite || !_origin.CanWrite ||
            _callback.PropertyType != action || _origin.PropertyType != typeof(string) ||
            _version.ReturnType != typeof(string) || _end.ReturnType != typeof(void) ||
            !_package.GetMethod!.IsStatic || !_initialized.GetMethod!.IsStatic ||
            _initialized.PropertyType != typeof(bool) || _downloadCount.PropertyType != typeof(int) ||
            !_status.PropertyType.IsEnum || !Enum.IsDefined(_status.PropertyType, "Succeed") ||
            Convert.ToInt32(Enum.Parse(_status.PropertyType, "Succeed")) != 2 ||
            !_packageMode.PropertyType.IsEnum || !Enum.IsDefined(_packageMode.PropertyType, "HostPlayMode") ||
            Convert.ToInt32(Enum.Parse(_packageMode.PropertyType, "HostPlayMode")) != 2)
            throw new InvalidOperationException("Cached host resource contract changed; skip not installed");
        try
        {
            harmony.Patch(start, prefix: new HarmonyMethod(typeof(SkipHotUpdate), nameof(StartLocal)));
        }
        catch
        {
            harmony.Unpatch(start, HarmonyPatchType.All, harmony.Id);
            throw;
        }
        MelonLogger.Msg("[PrivateServer] SkipHotUpdate installed: preserve native HostPlayMode cached manifest initialization; require zero missing downloads before native completion.");
    }

    private static bool StartLocal(object __instance, object __0)
    {
        try
        {
            var package = _package.GetValue(null);
            var initialized = (bool)(_initialized.GetValue(null) ?? false);
            var status = package == null ? 0 : Convert.ToInt32(_status.GetValue(package));
            var mode = package == null ? -1 : Convert.ToInt32(_packageMode.GetValue(package));
            var version = package == null ? null : _version.Invoke(package, null) as string;
            if (!LocalResourcePolicy.CanContinue(initialized, status, mode, version) || __0 == null)
                throw new InvalidOperationException($"Local host package not ready (ResManager.Init={initialized}, status={status}, mode={mode}, version={version ?? "<none>"}). Login was not completed.");
            // This creates a plan against the real active manifest and initialized cache.
            // Do not call BeginDownload: missing bundles stop this path without network IO.
            var plan = _downloader.Invoke(package, new object[] { 1, 0, 60 })
                ?? throw new InvalidOperationException("Native resource downloader plan is null");
            var missing = (int)(_downloadCount.GetValue(plan) ?? -1);
            if (!LocalResourcePolicy.HasAllResources(missing))
                throw new InvalidOperationException($"Cached manifest {version} requires {missing} resource downloads. No official request/download started; install complete matching resources before retrying.");
            _origin.SetValue(__instance, version);
            _callback.SetValue(__instance, __0);
            MelonLogger.Msg($"[PrivateServer] Local host package initialized: version={version}, missingDownloads={missing}; entering native completion without official version/manifest/download requests.");
            // Preserve OnHotFixCS, the native scene callback, shader warmup and later init.
            // Login's dispatch/currentVersion comparison is deliberately left unchanged.
            _end.Invoke(__instance, null);
        }
        catch (Exception ex)
        {
            MelonLogger.Error("[PrivateServer] SkipHotUpdate stopped: " + (ex.InnerException ?? ex).Message);
        }
        return false;
    }

    private static Type Resolve(string name) => AppDomain.CurrentDomain.GetAssemblies()
        .Select(a => a.GetType(name, false)).FirstOrDefault(t => t != null)
        ?? throw new TypeLoadException(name);
    private static PropertyInfo Property(Type type, string name) => type.GetProperty(name, Members)
        ?? throw new MissingMemberException(type.FullName, name);
    private static MethodInfo Method(Type type, string name, Type[] args) => type.GetMethod(name, Members, null, args, null)
        ?? throw new MissingMethodException(type.FullName, name);
}
