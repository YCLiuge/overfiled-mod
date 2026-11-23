using Il2Cpp;
using Il2CppFIMSpace.Basics;
using Il2CppInterop.Runtime;
using MelonLoader;
using MelonLoader.Utils;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

/// <summary>
/// AssetBundle 通用读取工具（Il2Cpp 兼容）
/// </summary>Logic.BundleHelper.ListAssets
namespace Logic
{
    public static class BundleHelper
    {
        // ================================================================
        //  1. 查找已加载的 bundle
        // ================================================================

        /// <summary>
        /// 按关键字从「已加载的 bundle」里找第一个匹配项。
        /// 关键字大小写不敏感，支持模糊匹配。
        /// 例：FindLoadedBundle("专属技能_寅")
        /// </summary>
        public static AssetBundle FindLoadedBundle(string keyword)
        {
            var all = GetAllLoadedBundles();
            return FindInList(all, keyword);
        }

        /// <summary>
        /// 按关键字从「已加载的 bundle」里找所有匹配项。
        /// </summary>
        public static List<AssetBundle> FindLoadedBundles(string keyword)
        {
            var result = new List<AssetBundle>();
            var all = GetAllLoadedBundles();

            if (all.Count == 0) return result;

            string kw = keyword?.ToLowerInvariant() ?? "";

            foreach (var b in all)
            {
                if (string.IsNullOrEmpty(kw) ||
                    (b.name != null && b.name.ToLowerInvariant().Contains(kw)))
                {
                    result.Add(b);
                }
            }
            return result;
        }

        private static AssetBundle FindInList(List<AssetBundle> list, string keyword)
        {
            if (list.Count == 0) return null;

            // 没传关键字 → 返回第一个
            if (string.IsNullOrEmpty(keyword)) return list[0];

            string kw = keyword.ToLowerInvariant();

            // 优先精确匹配
            foreach (var b in list)
            {
                if (b.name != null &&
                    b.name.Equals(keyword, StringComparison.OrdinalIgnoreCase))
                    return b;
            }

            // 再做模糊匹配
            foreach (var b in list)
            {
                if (b.name != null &&
                    b.name.ToLowerInvariant().Contains(kw))
                    return b;
            }

            return null;
        }

        /// <summary>
        /// 拿到所有已加载的 bundle（用标准 List 封装，避免每次遍历都处理 Il2Cpp 枚举器）
        /// </summary>
        public static List<AssetBundle> GetAllLoadedBundles()
        {
            var result = new List<AssetBundle>();

            try
            {
                var enumerable = AssetBundle.GetAllLoadedAssetBundles();
                if (enumerable == null) return result;

                var enumerator = (IEnumerator)enumerable.GetEnumerator();
                while (enumerator.MoveNext())
                {
                    if (enumerator.Current is AssetBundle b && b != null)
                        result.Add(b);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[BundleHelper] GetAllLoadedBundles 异常: {ex.Message}");
            }

            return result;
        }

        // ================================================================
        //  2. 列出 bundle 内的资源
        // ================================================================

        /// <summary>
        /// 打印 bundle 内所有资源名，返回列表。
        /// </summary>
        public static string[] ListAssets(AssetBundle bundle, bool print = true)
        {
            if (bundle == null) return Array.Empty<string>();

            try
            {
                var names = bundle.GetAllAssetNames();
                if (names == null) return Array.Empty<string>();

                if (print)
                {
                    MelonLogger.Msg($"[BundleHelper] {bundle.name} 内资源数量: {names.Length}");
                    foreach (var n in names)
                        MelonLogger.Msg($"    {n}");
                }
                return names;
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[BundleHelper] ListAssets 异常: {ex.Message}");
                return Array.Empty<string>();
            }
        }

        /// <summary>
        /// 在 bundle 内按关键字找资源名（大小写不敏感）。
        /// </summary>
        public static string[] FindAssetNames(AssetBundle bundle, string keyword)
        {
            if (bundle == null) return Array.Empty<string>();

            var all = ListAssets(bundle, false);
            if (all.Length == 0) return Array.Empty<string>();

            if (string.IsNullOrEmpty(keyword)) return all;

            string kw = keyword.ToLowerInvariant();
            var result = new List<string>();

            foreach (var name in all)
            {
                if (name != null && name.ToLowerInvariant().Contains(kw))
                    result.Add(name);
            }
            return result.ToArray();
        }

        // ================================================================
        //  3. 通用加载（按任意类型）
        // ================================================================

        /// <summary>
        /// 通用加载：从已加载的 bundle 里读一个资源。
        /// 
        /// 例：
        ///   var clip = BundleHelper.LoadFromLoadedBundle&lt;AnimationClip&gt;("专属技能_寅", "yin");
        ///   var tex  = BundleHelper.LoadFromLoadedBundle&lt;Texture2D&gt;("UI", "icon_attack");
        ///   var go   = BundleHelper.LoadFromLoadedBundle&lt;GameObject&gt;("prefab", "boat");
        /// 
        /// 注意：Il2Cpp 下内部用 Il2CppType.Of&lt;T&gt;() 避开泛型 AOT 问题。
        /// </summary>
        public static T LoadFromLoadedBundle<T>(string bundleKeyword, string assetKeyword)
            where T : UnityEngine.Object
        {
            var bundle = FindLoadedBundle(bundleKeyword);
            if (bundle == null)
            {
                MelonLogger.Warning($"[BundleHelper] 没找到已加载的 bundle: {bundleKeyword}");
                return null;
            }

            return LoadFromBundle<T>(bundle, assetKeyword);
        }

        /// <summary>
        /// 通用加载：直接给 bundle 对象。
        /// </summary>
        public static T LoadFromBundle<T>(AssetBundle bundle, string assetKeyword)
            where T : UnityEngine.Object
        {
            if (bundle == null) return null;

            try
            {
                // 1. 找到资源名
                string[] matches = FindAssetNames(bundle, assetKeyword);

                if (matches.Length == 0)
                {
                    MelonLogger.Warning(
                        $"[BundleHelper] {bundle.name} 里没有匹配 '{assetKeyword}' 的资源");
                    // 把可用资源名打出来，方便排查
                    ListAssets(bundle, true);
                    return null;
                }

                if (matches.Length > 1)
                {
                    MelonLogger.Msg(
                        $"[BundleHelper] {bundle.name} 匹配到 {matches.Length} 个，取第一个: {matches[0]}");
                }

                string assetName = matches[0];

                // 2. 加载（Il2Cpp 下用 Il2CppType.Of<T>() 避开泛型 AOT）
                var type = Il2CppType.Of<T>();
                var loaded = bundle.LoadAsset(assetName, type);

                if (loaded == null)
                {
                    MelonLogger.Warning(
                        $"[BundleHelper] LoadAsset 返回 null: {bundle.name} / {assetName}");
                    return null;
                }

                // 3. 转成目标类型
                var result = loaded.TryCast<T>();
                if (result == null)
                {
                    MelonLogger.Warning(
                        $"[BundleHelper] 类型不匹配，实际类型不是 {typeof(T).Name}: {assetName}");
                    return null;
                }

                MelonLogger.Msg($"[BundleHelper] ✓ 加载 {typeof(T).Name}: {assetName}");
                return result;
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[BundleHelper] LoadFromBundle 异常: {ex.Message}\n{ex.StackTrace}");
                return null;
            }
        }

        // ================================================================
        //  4. 批量加载
        // ================================================================

        /// <summary>
        /// 从 bundle 里批量加载所有匹配的资源。
        /// </summary>
        public static List<T> LoadAllFromBundle<T>(AssetBundle bundle, string assetKeyword = null)
            where T : UnityEngine.Object
        {
            var result = new List<T>();
            if (bundle == null) return result;

            try
            {
                var names = FindAssetNames(bundle, assetKeyword);
                if (names.Length == 0) return result;

                var type = Il2CppType.Of<T>();

                foreach (var name in names)
                {
                    try
                    {
                        var loaded = bundle.LoadAsset(name, type);
                        var typed = loaded?.TryCast<T>();
                        if (typed != null)
                        {
                            result.Add(typed);
                            MelonLogger.Msg($"[BundleHelper] ✓ 加载 {typeof(T).Name}: {name}");
                        }
                    }
                    catch (Exception ex)
                    {
                        MelonLogger.Warning(
                            $"[BundleHelper] 加载 {name} 失败: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[BundleHelper] LoadAllFromBundle 异常: {ex.Message}");
            }

            return result;
        }
        /// <summary>
        /// /////////////////////////////////test
        /// </summary>

        // ================================================================
        //  5. 指令集（测试用）
        // ================================================================

        // 单个资源加载：类型名 -> 泛型调用
        private static readonly Dictionary<string, Func<AssetBundle, string, UnityEngine.Object>> s_loaders
            = new Dictionary<string, Func<AssetBundle, string, UnityEngine.Object>>(StringComparer.OrdinalIgnoreCase)
        {
    { "Object",                    (b, k) => LoadFromBundle<UnityEngine.Object>(b, k) },
    { "AnimationClip",             (b, k) => LoadFromBundle<AnimationClip>(b, k) },
    { "Texture2D",                 (b, k) => LoadFromBundle<Texture2D>(b, k) },
    { "Sprite",                    (b, k) => LoadFromBundle<Sprite>(b, k) },
    { "GameObject",                (b, k) => LoadFromBundle<GameObject>(b, k) },
    { "AudioClip",                 (b, k) => LoadFromBundle<AudioClip>(b, k) },
    { "Material",                  (b, k) => LoadFromBundle<Material>(b, k) },
    { "Shader",                    (b, k) => LoadFromBundle<Shader>(b, k) },
    { "TextAsset",                 (b, k) => LoadFromBundle<TextAsset>(b, k) },
    { "Font",                      (b, k) => LoadFromBundle<Font>(b, k) },
    { "Mesh",                      (b, k) => LoadFromBundle<Mesh>(b, k) },
    { "RuntimeAnimatorController", (b, k) => LoadFromBundle<RuntimeAnimatorController>(b, k) },
        };

        // 批量加载：类型名 -> 泛型调用（返回值用 IEnumerable<Object> 接，靠协变）
        private static readonly Dictionary<string, Func<AssetBundle, string, IEnumerable<UnityEngine.Object>>> s_bulkLoaders
            = new Dictionary<string, Func<AssetBundle, string, IEnumerable<UnityEngine.Object>>>(StringComparer.OrdinalIgnoreCase)
        {
    { "Object",                    (b, k) => LoadAllFromBundle<UnityEngine.Object>(b, k) },
    { "AnimationClip",             (b, k) => LoadAllFromBundle<AnimationClip>(b, k) },
    { "Texture2D",                 (b, k) => LoadAllFromBundle<Texture2D>(b, k) },
    { "Sprite",                    (b, k) => LoadAllFromBundle<Sprite>(b, k) },
    { "GameObject",                (b, k) => LoadAllFromBundle<GameObject>(b, k) },
    { "AudioClip",                 (b, k) => LoadAllFromBundle<AudioClip>(b, k) },
    { "Material",                  (b, k) => LoadAllFromBundle<Material>(b, k) },
    { "Shader",                    (b, k) => LoadAllFromBundle<Shader>(b, k) },
    { "TextAsset",                 (b, k) => LoadAllFromBundle<TextAsset>(b, k) },
    { "Font",                      (b, k) => LoadAllFromBundle<Font>(b, k) },
    { "Mesh",                      (b, k) => LoadAllFromBundle<Mesh>(b, k) },
    { "RuntimeAnimatorController", (b, k) => LoadAllFromBundle<RuntimeAnimatorController>(b, k) },
        };

        /// <summary>
        /// 指令入口：外部（聊天/控制台钩子）把每条消息传进来即可。
        /// 只有以 "bundle " 或 "/bundle " 开头的消息才会被当作指令。
        /// </summary>
        public static void update(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return;

            message = message.Trim();

            // 前缀识别（可自行改成 "!" 或其它）
            string rest;
            if (message.StartsWith("/bundle", StringComparison.OrdinalIgnoreCase))
                rest = message.Substring("/bundle".Length).Trim();
            else if (message.StartsWith("bundle", StringComparison.OrdinalIgnoreCase))
                rest = message.Substring("bundle".Length).Trim();
            else
                return; // 不是我们的指令，忽略

            var parts = SplitArgs(rest);
            if (parts.Count == 0) { PrintHelp(); return; }

            string cmd = parts[0].ToLowerInvariant();
            try
            {
                switch (cmd)
                {
                    case "help": PrintHelp(); break;
                    case "list": CmdList(); break;
                    case "find": CmdFind(Arg(parts, 1)); break;
                    case "assets": CmdAssets(Arg(parts, 1)); break;
                    case "findasset": CmdFindAsset(Arg(parts, 1), Arg(parts, 2)); break;
                    case "load": CmdLoad(Arg(parts, 1), Arg(parts, 2), Arg(parts, 3)); break;
                    case "loadall": CmdLoadAll(Arg(parts, 1), Arg(parts, 2), Arg(parts, 3)); break;
                    case "export": CmdExport(Arg(parts, 1), Arg(parts, 2), Arg(parts, 3)); break;
                    default:
                        MelonLogger.Warning($"[BundleHelper] 未知指令: {cmd}");
                        PrintHelp();
                        break;
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[BundleHelper] 指令执行异常: {ex.Message}\n{ex.StackTrace}");
            }
        }

        // ---------------- 指令实现 ----------------

        private static void PrintHelp()
        {
            MelonLogger.Msg(
                "[BundleHelper] 指令集：\n" +
                "  bundle list                                       列出所有已加载 bundle\n" +
                "  bundle find <bundleKeyword>                       按关键字模糊查找 bundle\n" +
                "  bundle assets <bundleKeyword>                     列出 bundle 内所有资源\n" +
                "  bundle findasset <bundleKeyword> <assetKeyword>   在 bundle 里按关键字找资源名\n" +
                "  bundle load <bundleKeyword> <assetKeyword> [type] 加载单个资源\n" +
                "  bundle loadall <bundleKeyword> [assetKeyword] [type] 批量加载\n" +
                "  bundle export <bundleKeyword> [assetKeyword] [outDir]\n" +
                "  bundle help                                       显示帮助\n" +
                "  支持 type: " + string.Join(" / ", s_loaders.Keys));
        }

        private static void CmdList()
        {
            var all = GetAllLoadedBundles();
            MelonLogger.Msg($"[BundleHelper] 已加载 bundle 数量: {all.Count}");
            for (int i = 0; i < all.Count; i++)
                MelonLogger.Msg($"  [{i}] {all[i].name}");
        }

        private static void CmdFind(string keyword)
        {
            var matches = FindLoadedBundles(keyword);
            MelonLogger.Msg($"[BundleHelper] 匹配 '{keyword}' 的 bundle: {matches.Count}");
            foreach (var b in matches) MelonLogger.Msg($"  {b.name}");
        }

        private static void CmdAssets(string bundleKeyword)
        {
            var b = FindLoadedBundle(bundleKeyword);
            if (b == null) { MelonLogger.Warning($"[BundleHelper] 没找到 bundle: {bundleKeyword}"); return; }
            ListAssets(b, true);
        }

        private static void CmdFindAsset(string bundleKeyword, string assetKeyword)
        {
            var b = FindLoadedBundle(bundleKeyword);
            if (b == null) { MelonLogger.Warning($"[BundleHelper] 没找到 bundle: {bundleKeyword}"); return; }
            var names = FindAssetNames(b, assetKeyword);
            MelonLogger.Msg($"[BundleHelper] 在 {b.name} 中匹配 '{assetKeyword}': {names.Length}");
            foreach (var n in names) MelonLogger.Msg($"  {n}");
        }

        private static void CmdLoad(string bundleKeyword, string assetKeyword, string typeName)
        {
            if (string.IsNullOrEmpty(bundleKeyword) || string.IsNullOrEmpty(assetKeyword))
            {
                MelonLogger.Warning("[BundleHelper] 用法: bundle load <bundleKeyword> <assetKeyword> [type]");
                return;
            }

            var b = FindLoadedBundle(bundleKeyword);
            if (b == null) { MelonLogger.Warning($"[BundleHelper] 没找到 bundle: {bundleKeyword}"); return; }

            typeName = string.IsNullOrEmpty(typeName) ? "Object" : typeName;
            if (!s_loaders.TryGetValue(typeName, out var loader))
            {
                MelonLogger.Warning($"[BundleHelper] 不支持的类型: {typeName}，可选: {string.Join(" / ", s_loaders.Keys)}");
                return;
            }

            var obj = loader(b, assetKeyword);
            if (obj == null)
                MelonLogger.Warning("[BundleHelper] 加载失败");
            else
                MelonLogger.Msg($"[BundleHelper] ✓ 加载成功 [{obj.GetType().Name}] {obj.name}");
        }

        private static void CmdLoadAll(string bundleKeyword, string assetKeyword, string typeName)
        {
            if (string.IsNullOrEmpty(bundleKeyword))
            {
                MelonLogger.Warning("[BundleHelper] 用法: bundle loadall <bundleKeyword> [assetKeyword] [type]");
                return;
            }

            var b = FindLoadedBundle(bundleKeyword);
            if (b == null) { MelonLogger.Warning($"[BundleHelper] 没找到 bundle: {bundleKeyword}"); return; }

            typeName = string.IsNullOrEmpty(typeName) ? "Object" : typeName;
            if (!s_bulkLoaders.TryGetValue(typeName, out var loader))
            {
                MelonLogger.Warning($"[BundleHelper] 不支持的类型: {typeName}，可选: {string.Join(" / ", s_bulkLoaders.Keys)}");
                return;
            }

            int count = 0;
            foreach (var o in loader(b, assetKeyword))
            {
                count++;
                MelonLogger.Msg($"  {o.name}");
            }
            MelonLogger.Msg($"[BundleHelper] 批量加载完成，共 {count} 个");
        }

        // ---------------- 解析辅助 ----------------

        private static string Arg(List<string> parts, int index)
            => index < parts.Count ? parts[index] : null;

        /// <summary>
        /// 拆分参数，支持 "..." 包裹含空格参数。
        /// 例：load "专属技能 寅" yin AnimationClip
        /// </summary>
        private static List<string> SplitArgs(string s)
        {
            var list = new List<string>();
            if (string.IsNullOrEmpty(s)) return list;

            var cur = new List<char>();
            bool inQuote = false;

            foreach (char c in s)
            {
                if (c == '"') { inQuote = !inQuote; continue; }

                if (char.IsWhiteSpace(c) && !inQuote)
                {
                    if (cur.Count > 0) { list.Add(new string(cur.ToArray())); cur.Clear(); }
                }
                else
                {
                    cur.Add(c);
                }
            }
            if (cur.Count > 0) list.Add(new string(cur.ToArray()));
            return list;
        }
        // ================================================================
        //  6. 导出
        // ================================================================

        public static string DefaultExportRoot
        {
            get
            {
                string root = "导出模型/";

                // 1) 空值兜底
                if (string.IsNullOrWhiteSpace(root))
                {
                    MelonLogger.Warning("[Export] ConstMsg.ModelExporter_path 为空，回退到 UserData");
                    root = Path.Combine(MelonEnvironment.UserDataDirectory, "BundleExport");
                }

                // 2) 相对路径按 MelonLoader 工作目录解析（也可换成游戏根目录，见下方说明）
                if (!Path.IsPathRooted(root))
                    root = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), root));

                // 3) 确保存在
                try { Directory.CreateDirectory(root); }
                catch (Exception ex)
                {
                    MelonLogger.Error($"[Export] 无法创建导出目录 {root}: {ex.Message}");
                }

                return root;
            }
        }

        private static void CmdExport(string bkw, string akw, string outDir)
        {
            if (string.IsNullOrEmpty(bkw))
            {
                MelonLogger.Warning("[BundleHelper] 用法: bundle export <bundleKeyword> [assetKeyword] [outDir]");
                return;
            }
            var b = FindLoadedBundle(bkw);
            if (b == null) { MelonLogger.Warning($"[BundleHelper] 没找到 bundle: {bkw}"); return; }
            ExportBundle(b, akw, outDir);
        }

        public static int ExportBundle(AssetBundle bundle, string assetKeyword, string outDir)
        {
            if (bundle == null) return 0;
            if (string.IsNullOrEmpty(outDir))
                outDir = Path.Combine(DefaultExportRoot, SafeName(bundle.name));
            Directory.CreateDirectory(outDir);

            var names = FindAssetNames(bundle, assetKeyword);
            MelonLogger.Msg($"[Export] {bundle.name} 导出 {names.Length} 个资源 → {outDir}");

            int ok = 0;
            for (int i = 0; i < names.Length; i++)
            {
                try
                {
                    if (ExportOne(bundle, names[i], outDir) != null) ok++;
                }
                catch (Exception ex)
                {
                    MelonLogger.Warning($"[Export] {names[i]} 失败: {ex.Message}");
                }
                if ((i + 1) % 20 == 0)
                    MelonLogger.Msg($"[Export] 进度 {i + 1}/{names.Length}");
            }
            MelonLogger.Msg($"[Export] 完成，成功 {ok}/{names.Length}");
            return ok;
        }

        private static UnityEngine.Object ExportOne(AssetBundle bundle, string assetName, string outDir)
        {
            var loaded = bundle.LoadAsset(assetName, Il2CppType.Of<UnityEngine.Object>());
            if (loaded == null) return null;

            // LoadAsset<Object> returns a managed Object wrapper; dispatch on the native IL2CPP type.
            string typeName = loaded.GetIl2CppType().Name;
            string sub = Path.Combine(outDir, SafeName(typeName));
            Directory.CreateDirectory(sub);
            string fileBase = Path.Combine(sub, SafeName(assetName));

            switch (typeName)
            {
                case "Texture2D": ExportTexture(loaded.TryCast<Texture2D>(), fileBase + ".png"); break;
                case "Sprite": ExportSprite(loaded.TryCast<Sprite>(), fileBase + ".png"); break;
                case "AudioClip": ExportAudio(loaded.TryCast<AudioClip>(), fileBase + ".wav"); break;
                case "TextAsset": ExportText(loaded.TryCast<TextAsset>(), fileBase); break;
                case "Mesh": ExportMesh(loaded.TryCast<Mesh>(), fileBase + ".obj"); break;
                case "Material": ExportMat(loaded.TryCast<Material>(), fileBase + ".mat.txt"); break;
                case "Shader": ExportShader(loaded.TryCast<Shader>(), fileBase + ".shader.txt"); break;
                case "AnimationClip": ExportClip(loaded.TryCast<AnimationClip>(), fileBase + ".anim.txt"); break;
                case "GameObject": ExportGO(loaded.TryCast<GameObject>(), fileBase + ".go.txt"); break;
                case "Font": ExportFont(loaded.TryCast<Font>(), fileBase + ".font.txt"); break;
                default: ExportDump(loaded, fileBase + ".txt"); break;
            }
            return loaded;
        }

        // ---------------- 各类型导出 ----------------

        private static void ExportTexture(Texture2D tex, string path)
        {
            if (tex == null) return;
            var readable = MakeReadable(tex);
            var png = ImageConversion.EncodeToPNG(readable);
            if (png == null) return;
            File.WriteAllBytes(path, png.ToArray());   // 若编译报错：用 System.Linq 的 ToArray
        }

        private static void ExportSprite(Sprite sp, string path)
        {
            if (sp == null || sp.texture == null) return;
            var src = MakeReadable(sp.texture);

            var r = sp.textureRect;   // 图集里的真实区域
            int x = Mathf.Clamp(Mathf.RoundToInt(r.x), 0, Math.Max(0, src.width - 1));
            int y = Mathf.Clamp(Mathf.RoundToInt(r.y), 0, Math.Max(0, src.height - 1));
            int w = Mathf.Clamp(Mathf.RoundToInt(r.width), 1, src.width - x);
            int h = Mathf.Clamp(Mathf.RoundToInt(r.height), 1, src.height - y);

            var cropped = new Texture2D(w, h, TextureFormat.ARGB32, false);
            cropped.SetPixels(src.GetPixels(x, y, w, h));
            cropped.Apply();

            var png = ImageConversion.EncodeToPNG(cropped);
            if (png != null) File.WriteAllBytes(path, png.ToArray());
            UnityEngine.Object.Destroy(cropped);
        }

        private static Texture2D MakeReadable(Texture2D src)
        {
            if (src.isReadable) return src;

            var rt = RenderTexture.GetTemporary(src.width, src.height, 0,
                RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Graphics.Blit(src, rt);

            var old = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(src.width, src.height, TextureFormat.ARGB32, false);
            tex.ReadPixels(new Rect(0, 0, src.width, src.height), 0, 0);
            tex.Apply();
            RenderTexture.active = old;
            RenderTexture.ReleaseTemporary(rt);
            return tex;
        }

        private static void ExportAudio(AudioClip clip, string path)
        {
            if (clip == null) return;
            int channels = clip.channels;
            int freq = clip.frequency;
            int samples = clip.samples;
            if (channels <= 0 || freq <= 0 || samples <= 0)
                throw new InvalidOperationException($"AudioClip 没有可导出的采样数据: {clip.name}");

            // GetData writes to the native array. Passing float[] creates a temporary
            // IL2CPP array whose updated contents are not copied back to the managed array.
            var nativeData = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<float>(checked(samples * channels));
            if (!clip.GetData(nativeData, 0))
                throw new InvalidOperationException($"GetData 失败，未生成 WAV: {clip.name} (loadType={clip.loadType}, loadState={clip.loadState})");
            var data = nativeData.ToArray();

            int dataSize = data.Length * 2; // 16bit PCM
            using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
            using var bw = new BinaryWriter(fs);

            bw.Write(Encoding.ASCII.GetBytes("RIFF"));
            bw.Write(36 + dataSize);
            bw.Write(Encoding.ASCII.GetBytes("WAVE"));
            bw.Write(Encoding.ASCII.GetBytes("fmt "));
            bw.Write(16);
            bw.Write((short)1);                        // PCM
            bw.Write((short)channels);
            bw.Write(freq);
            bw.Write(freq * channels * 2);             // byteRate
            bw.Write((short)(channels * 2));           // blockAlign
            bw.Write((short)16);                       // bitsPerSample
            bw.Write(Encoding.ASCII.GetBytes("data"));
            bw.Write(dataSize);

            foreach (var f in data)
                bw.Write((short)Mathf.Clamp(f * 32767f, -32768f, 32767f));
        }

        private static void ExportText(TextAsset ta, string fileBase)
        {
            if (ta == null) return;
            var bytes = ta.bytes;
            // 判断是不是纯文本：能正常读并且不含非法控制字符
            string text = ta.text;
            bool isText = !string.IsNullOrEmpty(text) &&
                          text.IndexOf('\0') < 0 && IsMostlyPrintable(text);

            if (isText)
                File.WriteAllText(fileBase + ".txt", text);
            else
                File.WriteAllBytes(fileBase + ".bytes", bytes.ToArray());
        }

        private static bool IsMostlyPrintable(string s)
        {
            int bad = 0, n = Math.Min(s.Length, 4096);
            for (int i = 0; i < n; i++)
            {
                char c = s[i];
                if (c < 32 && c != '\r' && c != '\n' && c != '\t') bad++;
            }
            return bad * 50 < n; // 非法字符 < 2%
        }

        private static void ExportMesh(Mesh m, string path)
        {
            if (m == null) return;
            var sb = new StringBuilder();
            sb.AppendLine($"# {m.name}  verts={m.vertexCount}  subMeshes={m.subMeshCount}");

            foreach (var v in m.vertices)
                sb.AppendLine($"v {v.x:F6} {v.y:F6} {v.z:F6}");
            foreach (var n in m.normals)
                sb.AppendLine($"vn {n.x:F6} {n.y:F6} {n.z:F6}");
            foreach (var u in m.uv)
                sb.AppendLine($"vt {u.x:F6} {u.y:F6}");

            var tris = m.triangles;
            for (int i = 0; i + 2 < tris.Length; i += 3)
                sb.AppendLine($"f {tris[i] + 1} {tris[i + 1] + 1} {tris[i + 2] + 1}");

            File.WriteAllText(path, sb.ToString());
        }

        private static void ExportMat(Material mat, string path)
        {
            if (mat == null) return;
            var sb = new StringBuilder();
            sb.AppendLine($"name: {mat.name}");
            sb.AppendLine($"shader: {mat.shader?.name}");
            sb.AppendLine($"renderQueue: {mat.renderQueue}");

            var sh = mat.shader;
            if (sh != null)
            {
                int n = sh.GetPropertyCount();
                sb.AppendLine($"properties ({n}):");
                for (int i = 0; i < n; i++)
                {
                    string pn = sh.GetPropertyName(i);
                    var pt = sh.GetPropertyType(i);
                    object val;
                    switch (pt)
                    {
                        case UnityEngine.Rendering.ShaderPropertyType.Color: val = mat.GetColor(pn); break;
                        case UnityEngine.Rendering.ShaderPropertyType.Vector: val = mat.GetVector(pn); break;
                        case UnityEngine.Rendering.ShaderPropertyType.Float:
                        case UnityEngine.Rendering.ShaderPropertyType.Range: val = mat.GetFloat(pn); break;
                        case UnityEngine.Rendering.ShaderPropertyType.Texture: val = mat.GetTexture(pn)?.name; break;
                        default: val = null; break;
                    }
                    sb.AppendLine($"  {pn} [{pt}] = {val}");
                }
            }
            File.WriteAllText(path, sb.ToString());
        }

        private static void ExportShader(Shader sh, string path)
        {
            if (sh == null) return;
            File.WriteAllText(path,
                $"name: {sh.name}\n" +
                $"isSupported: {sh.isSupported}\n" +
                $"renderQueue: {sh.renderQueue}\n" +
                $"passCount: {sh.passCount}\n" +
                $"# 运行时无法还原 shader 源码，仅导出元信息\n");
        }

        private static void ExportClip(AnimationClip clip, string path)
        {
            if (clip == null) return;
            var sb = new StringBuilder();
            sb.AppendLine($"name: {clip.name}");
            sb.AppendLine($"length: {clip.length}s");
            sb.AppendLine($"frameRate: {clip.frameRate}");
            sb.AppendLine($"wrapMode: {clip.wrapMode}");
            sb.AppendLine($"legacy: {clip.legacy}");
            sb.AppendLine($"localBounds: {clip.localBounds}");

            var ev = clip.events;
            if (ev != null && ev.Length > 0)
            {
                sb.AppendLine($"events ({ev.Length}):");
                foreach (var e in ev)
                    sb.AppendLine($"  {e.time}s -> {e.functionName}");
            }
            File.WriteAllText(path, sb.ToString());
        }

        private static void ExportGO(GameObject go, string path)
        {
            if (go == null) return;
            var sb = new StringBuilder();
            sb.AppendLine($"GameObject: {go.name}");
            sb.AppendLine($"active: {go.activeSelf}  layer: {LayerMask.LayerToName(go.layer)}  tag: {go.tag}");
            DumpTransform(go.transform, sb, 0);
            File.WriteAllText(path, sb.ToString());
        }

        private static void DumpTransform(Transform t, StringBuilder sb, int depth)
        {
            string ind = new string(' ', depth * 2);
            sb.AppendLine($"{ind}- {t.name}  localPos={t.localPosition}  localScale={t.localScale}");
            foreach (var c in t.GetComponents<Component>())
                sb.AppendLine($"{ind}    [{c?.GetType().Name}]");
            for (int i = 0; i < t.childCount; i++)
                DumpTransform(t.GetChild(i), sb, depth + 1);
        }

        private static void ExportFont(Font f, string path)
        {
            if (f == null) return;
            File.WriteAllText(path,
                $"name: {f.name}\n" +
                $"# 运行时无法还原 ttf/otf 原文件，仅导出元信息\n");
        }

        private static void ExportDump(UnityEngine.Object obj, string path)
        {
            File.WriteAllText(path,
                $"name: {obj.name}\n" +
                $"type: {obj.GetType().Name}\n" +
                $"# 未支持的类型，仅记录元信息\n");
        }

        private static string SafeName(string s)
        {
            if (string.IsNullOrEmpty(s)) return "unnamed";
            foreach (var c in Path.GetInvalidFileNameChars())
                s = s.Replace(c, '_');
            return s.Replace('/', '_').Replace('\\', '_');
        }

    }

}