using System.Reflection;
using HarmonyLib;
using MelonLoader;

namespace OverField.PrivateServer;

/// <summary>Hide only OutLog's presentation; preserve its subscriptions, buffers and disk logging.</summary>
internal static class ErrorOverlay
{
    private const BindingFlags Members = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    private static PropertyInfo _background = null!, _text = null!, _button = null!, _open = null!, _textObject = null!;
    private static MethodInfo _setActive = null!;
    private static bool _reported;

    internal static void Install(HarmonyLib.Harmony harmony)
    {
        var type = Resolve("Il2Cpp.OutLog");
        var gameObject = Resolve("UnityEngine.GameObject");
        _background = Property(type, "Background");
        _text = Property(type, "TMPText");
        _button = Property(type, "UguiBtn");
        _open = Property(type, "IsUIOpen");
        _textObject = Property(_text.PropertyType, "gameObject");
        _setActive = gameObject.GetMethod("SetActive", Members, null, new[] { typeof(bool) }, null)
            ?? throw new MissingMethodException(gameObject.FullName, "SetActive");
        var show = Method(type, "SetTextUIShow", new[] { typeof(bool) });
        var callback = Method(type, "_SetTextUIShow_b__29_0", new[] { typeof(bool) });
        var start = Method(type, "Start", Type.EmptyTypes);
        var update = Method(type, "Update", Type.EmptyTypes);
        var refresh = Method(type, "OnUpdateTextUI", Type.EmptyTypes);
        if (_background.PropertyType != gameObject || _button.PropertyType != gameObject ||
            _textObject.PropertyType != gameObject || _open.PropertyType != typeof(bool) || !_open.CanWrite)
            throw new InvalidOperationException("OutLog UI contract changed");
        var prefix = new HarmonyMethod(typeof(ErrorOverlay), nameof(KeepClosed));
        var postfix = new HarmonyMethod(typeof(ErrorOverlay), nameof(Hide));
        // Resolve every member before patching. No log handlers or lifecycle methods are skipped.
        foreach (var target in new[] { show, callback }) harmony.Patch(target, prefix: prefix, postfix: postfix);
        foreach (var target in new[] { start, update, refresh }) harmony.Patch(target, postfix: postfix);
        MelonLogger.Msg("[PrivateServer] Error overlay hidden: OutLog text/background/feedback UI only; native logging remains enabled.");
    }

    private static void KeepClosed(ref bool __0) => __0 = false;

    private static void Hide(object __instance)
    {
        try
        {
            _open.SetValue(__instance, false);
            Deactivate(_background.GetValue(__instance));
            Deactivate(_button.GetValue(__instance));
            var text = _text.GetValue(__instance);
            if (text != null) Deactivate(_textObject.GetValue(text));
        }
        catch (Exception ex)
        {
            if (_reported) return;
            _reported = true;
            MelonLogger.Warning("[PrivateServer] Could not hide OutLog UI: " + (ex.InnerException ?? ex).Message);
        }
    }

    private static void Deactivate(object? gameObject)
    {
        if (gameObject != null) _setActive.Invoke(gameObject, new object[] { false });
    }

    private static Type Resolve(string name) => AppDomain.CurrentDomain.GetAssemblies()
        .Select(a => a.GetType(name, false)).FirstOrDefault(t => t != null) ?? throw new TypeLoadException(name);
    private static PropertyInfo Property(Type type, string name) => type.GetProperty(name, Members)
        ?? throw new MissingMemberException(type.FullName, name);
    private static MethodInfo Method(Type type, string name, Type[] args) => type.GetMethod(name, Members, null, args, null)
        ?? throw new MissingMethodException(type.FullName, name);
}
