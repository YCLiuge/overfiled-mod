using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using MelonLoader;
using Newtonsoft.Json;
using UnityEngine;

namespace ModLoader
{
    /// <summary>
    /// 多模块远程更新器（自定义 TCP 协议 + 本地缓存）
    /// </summary>
    public class LogicUpdater
    {
        public static LogicUpdater Instance { get; } = new LogicUpdater();
        // ================================================================
        //  ★ 连接配置
        // ================================================================
        public string[] ServerEndpoints = new[]
        {
            "127.0.0.1:10086",       // 本地优先（开发/测试）
            "frp-off.com:50505",     // 远程兜底
        };

        // ================================================================
        //  行为配置
        // ================================================================
        public bool EnableAutoCheck = false;
        public float AutoCheckInterval = 300f;   // 5 分钟
        public int ConnectTimeoutMs = 5000;
        public int TransferTimeoutMs = 30000;
        public int MaxDownloadBytes = 128 * 1024 * 1024;
        private readonly SemaphoreSlim _updateGate = new SemaphoreSlim(1, 1);
        private readonly ConcurrentQueue<Action> _mainThreadActions = new ConcurrentQueue<Action>();
        public bool VerifyHash = true;
        public bool LogVerbose = true;
        public bool EnableCache = true;

        // ================================================================
        //  状态
        // ================================================================
        public bool IsChecking { get; private set; }
        public DateTime LastCheckTime { get; private set; }
        public string LastError { get; private set; }

        private readonly Dictionary<string, string> _localVersions
            = new Dictionary<string, string>();
        private readonly Dictionary<string, string> _loadedHashes = new Dictionary<string, string>();
        private readonly Dictionary<string, int> _localPriorities
            = new Dictionary<string, int>();

        private float _checkTimer;

        // ================================================================
        //  缓存目录
        // ================================================================
        private string _cacheDir;

        public string CacheDir
        {
            get
            {
                if (_cacheDir == null)
                {
                    try
                    {
                        string modsDir = Path.GetDirectoryName(
                            Assembly.GetExecutingAssembly().Location);
                        _cacheDir = Path.Combine(modsDir, "cache");
                    }
                    catch
                    {
                        _cacheDir = Path.Combine(
                            AppDomain.CurrentDomain.BaseDirectory, "Mods", "cache");
                    }
                }
                return _cacheDir;
            }
        }

        public string ManifestCachePath
            => Path.Combine(CacheDir, "manifest.json");

        public string GetModuleCachePath(string moduleId)
        {
            ValidateModuleId(moduleId);
            return Path.Combine(CacheDir, $"{moduleId}.dll");
        }

        private static void ValidateModuleId(string moduleId)
        {
            if (string.IsNullOrWhiteSpace(moduleId) || moduleId.Length > 128)
                throw new InvalidDataException("模块 ID 为空或过长");
            foreach (char c in moduleId)
                if (!(c >= 'a' && c <= 'z') && !(c >= 'A' && c <= 'Z')
                    && !(c >= '0' && c <= '9') && c != '_' && c != '-' && c != '.')
                    throw new InvalidDataException($"非法模块 ID: {moduleId}");
            if (moduleId == "." || moduleId == ".." || moduleId.EndsWith("."))
                throw new InvalidDataException($"非法模块 ID: {moduleId}");
        }
        /// <summary>
        /// 从 URL 里提取文件名
        ///   "http://frp-off.com:50505/Mods/Logic.dll"  → "Logic.dll"
        ///   "Logic.dll"                                 → "Logic.dll"
        ///   "Mods/sub/Foo.dll"                          → "sub/Foo.dll"（保留子目录）
        ///   null / ""                                   → null
        /// </summary>
        private static string ExtractFileName(string url)
        {
            if (string.IsNullOrEmpty(url)) return null;

            try
            {
                // 先按 URI 处理（标准 http:// 开头）
                if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
                {
                    // AbsolutePath 形如 "/Mods/Logic.dll"
                    string path = uri.AbsolutePath;
                    // 去掉开头的 "/"
                    if (path.StartsWith("/")) path = path.Substring(1);
                    return string.IsNullOrEmpty(path) ? null : path;
                }
            }
            catch { /* 不是合法 URI，走下面的字符串兜底 */ }

            // 不是完整 URI，就当作相对路径/文件名
            // 去掉开头的 "/"
            string s = url.Trim();
            if (s.StartsWith("/")) s = s.Substring(1);
            return string.IsNullOrEmpty(s) ? null : s;
        }
        private static string StripBom(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            return s[0] == '\uFEFF' ? s.Substring(1) : s;
        }
        // ================================================================
        //  初始化
        // ================================================================
        public void Initialize()
        {
            LoadLocalCache();

            if (EnableAutoCheck)
            {
                _ = CheckAndUpdateAllAsync();
            }
        }

        public void Tick()
        {
            // 网络线程仅排队，所有模块生命周期与状态提交均由游戏主线程执行。
            while (_mainThreadActions.TryDequeue(out var action)) action();
            if (!EnableAutoCheck) return;

            _checkTimer += Time.deltaTime;
            if (_checkTimer >= AutoCheckInterval)
            {
                _checkTimer = 0f;
                _ = CheckAndUpdateAllAsync();
            }
        }

        // ================================================================
        //  本地缓存加载
        // ================================================================
        public void LoadLocalCache()
        {
            if (!EnableCache) return;

            try
            {
                if (!Directory.Exists(CacheDir))
                {
                    if (LogVerbose)
                        MelonLogger.Msg($"[Updater] 缓存目录不存在: {CacheDir}");
                    return;
                }

                if (!File.Exists(ManifestCachePath))
                {
                    if (LogVerbose)
                        MelonLogger.Msg($"[Updater] 缓存清单不存在: {ManifestCachePath}");
                    return;
                }

                string json = File.ReadAllText(ManifestCachePath);
                var manifest = JsonConvert.DeserializeObject<ModuleManifest>(json);

                if (manifest?.Modules == null || manifest.Modules.Count == 0)
                {
                    MelonLogger.Warning("[Updater] 缓存清单为空");
                    return;
                }

                int loaded = 0;

                foreach (var m in manifest.Modules)
                {
                    if (m == null || !m.Enabled) continue;
                    try { ValidateModuleId(m.Id); }
                    catch (InvalidDataException ex)
                    {
                        MelonLogger.Warning($"[Updater] 忽略缓存条目: {ex.Message}");
                        continue;
                    }

                    string dllPath = GetModuleCachePath(m.Id);
                    if (!File.Exists(dllPath))
                    {
                        MelonLogger.Warning($"[Updater] {m.Id}: 缓存 DLL 不存在 {dllPath}");
                        continue;
                    }

                    if (LogicLoader.Instance.GetModule(m.Id) != null)
                        continue;

                    try
                    {
                        byte[] bytes = File.ReadAllBytes(dllPath);
                        if (VerifyHash && !string.IsNullOrEmpty(m.Hash) && !VerifySHA256(bytes, m.Hash))
                            throw new InvalidDataException("缓存 SHA256 校验失败");
                        bool ok = LogicLoader.Instance.LoadModuleFromBytes(
                            m.Id, bytes, m.Priority, dllPath);

                        if (ok)
                        {
                            _loadedHashes[m.Id] = Convert.ToHexString(SHA256.HashData(bytes));
                            _localVersions[m.Id] = m.Version ?? "0.0.0";
                            _localPriorities[m.Id] = m.Priority;
                            loaded++;
                            MelonLogger.Msg(
                                $"[Updater] ✓ 本地缓存加载: {m.Id} v{m.Version} (p={m.Priority})");
                        }
                    }
                    catch (Exception ex)
                    {
                        MelonLogger.Warning($"[Updater] 加载缓存 {m.Id} 失败: {ex.Message}");
                    }
                }

                MelonLogger.Msg(
                    $"[Updater] 本地缓存加载完成: {loaded}/{manifest.Modules.Count} 个模块");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[Updater] LoadLocalCache 异常: {ex.Message}");
            }
        }

        // ================================================================
        //  公开方法
        // ================================================================

        public async Task CheckAndUpdateAllAsync()
        {
            if (!await _updateGate.WaitAsync(0)) return;
            IsChecking = true;
            LastError = null;

            try
            {
                MelonLogger.Msg($"[Updater] 开始检查更新，候选服务器 {ServerEndpoints?.Length ?? 0} 个");

                // ★ 通过 TCP 拉取 version.json
                byte[] jsonBytes = await FetchTcpAsync("version.json");
                string json = StripBom(Encoding.UTF8.GetString(jsonBytes));
                var manifest = JsonConvert.DeserializeObject<ModuleManifest>(json);

                if (manifest?.Modules == null || manifest.Modules.Count == 0)
                {
                    MelonLogger.Warning("[Updater] 远程清单为空");
                    return;
                }

                MelonLogger.Msg($"[Updater] 远程清单包含 {manifest.Modules.Count} 个模块");

                int updated = 0, skipped = 0, failed = 0;

                foreach (var remote in manifest.Modules)
                {
                    try
                    {
                        bool result = await ProcessModuleAsync(remote);
                        if (result) updated++; else skipped++;
                    }
                    catch (Exception ex)
                    {
                        LastError = ex.Message;
                        MelonLogger.Error($"[Updater] 模块 {remote?.Id} 处理失败: {ex.Message}");
                        failed++;
                    }
                }

                LastCheckTime = DateTime.Now;
                MelonLogger.Msg($"[Updater] 检查完成: 更新={updated}, 跳过={skipped}, 失败={failed}");
            }
            catch (SocketException ex)
            {
                LastError = ex.Message;
                MelonLogger.Warning($"[Updater] 网络错误: {ex.Message}（已使用本地缓存）");
            }
            catch (TimeoutException)
            {
                LastError = "连接超时";
                MelonLogger.Warning("[Updater] 连接超时（已使用本地缓存）");
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                MelonLogger.Warning($"[Updater] 更新异常: {ex.Message}\n{ex.StackTrace}");
            }
            finally
            {
                IsChecking = false;
                _updateGate.Release();
            }
        }

        public async Task<bool> CheckSingleModuleAsync(string moduleId)
        {
            if (!await _updateGate.WaitAsync(0)) return false;
            IsChecking = true;
            LastError = null;
            try
            {
                byte[] jsonBytes = await FetchTcpAsync("version.json");
                string json = StripBom(Encoding.UTF8.GetString(jsonBytes));
                var manifest = JsonConvert.DeserializeObject<ModuleManifest>(json);

                if (manifest?.Modules == null) return false;

                foreach (var remote in manifest.Modules)
                {
                    if (remote.Id == moduleId)
                        return await ProcessModuleAsync(remote);
                }

                MelonLogger.Warning($"[Updater] 远程清单里没有模块 {moduleId}");
                return false;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                MelonLogger.Error($"[Updater] 检查 {moduleId} 失败: {ex.Message}");
                return false;
            }
            finally
            {
                LastCheckTime = DateTime.Now;
                IsChecking = false;
                _updateGate.Release();
            }
        }

        public void SetLocalVersion(string moduleId, string version)
        {
            _localVersions[moduleId] = version;
        }

        // ================================================================
        //  内部：单模块处理
        // ================================================================

        private async Task<bool> ProcessModuleAsync(RemoteModuleInfo remote)
        {
            if (remote == null) throw new InvalidDataException("清单包含空模块");
            ValidateModuleId(remote.Id);
            if (!remote.Enabled)
            {
                if (LogVerbose)
                    MelonLogger.Msg($"[Updater] {remote.Id}: 远程已禁用，跳过");
                return false;
            }

            var stateCompletion = new TaskCompletionSource<(string Version, string Hash, bool Loaded)>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _mainThreadActions.Enqueue(() =>
            {
                try { stateCompletion.TrySetResult((GetLocalVersion(remote.Id), GetLocalHash(remote.Id),
                    LogicLoader.Instance.HasModule(remote.Id))); }
                catch (Exception ex) { stateCompletion.TrySetException(ex); }
            });
            var state = await stateCompletion.Task;
            string localVer = state.Version;
            string localHash = state.Hash;

            bool versionNewer = IsNewer(remote.Version, localVer);
            bool hashChanged = !string.IsNullOrEmpty(remote.Hash)
                             && !string.Equals(remote.Hash, localHash,
                                               StringComparison.OrdinalIgnoreCase);

            if (!versionNewer && !hashChanged && state.Loaded)
            {
                if (LogVerbose)
                    MelonLogger.Msg($"[Updater] {remote.Id}: 已是最新 ({localVer})");
                return false;
            }

            if (versionNewer)
                MelonLogger.Msg($"[Updater] {remote.Id}: {localVer} → {remote.Version}，开始下载");
            else
                MelonLogger.Msg($"[Updater] {remote.Id}: 版本相同但内容变化（hash 不一致），开始下载");

            // ★ 从 url 提取文件名，回退到 {id}.dll
            string fileName = ExtractFileName(remote.Url);
            if (string.IsNullOrEmpty(fileName))
                fileName = $"{remote.Id}.dll";

            if (LogVerbose)
                MelonLogger.Msg($"[Updater] {remote.Id}: TCP 请求 {fileName}");

            byte[] dllBytes;
            try
            {
                dllBytes = await FetchTcpAsync(fileName);
            }
            catch (FileNotFoundException)
            {
                throw new FileNotFoundException($"{remote.Id}: 服务器没有 {fileName}");
            }
            catch (Exception ex)
            {
                throw new IOException($"{remote.Id}: 下载失败 {ex.Message}", ex);
            }

            if (dllBytes == null || dllBytes.Length == 0)
            {
                throw new InvalidDataException($"{remote.Id}: 下载内容为空");
            }

            if (VerifyHash && !string.IsNullOrEmpty(remote.Hash))
            {
                if (!VerifySHA256(dllBytes, remote.Hash))
                {
                    throw new InvalidDataException($"{remote.Id}: SHA256 校验失败，拒绝加载");
                }
                if (LogVerbose)
                    MelonLogger.Msg($"[Updater] {remote.Id}: SHA256 校验通过");
            }

            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _mainThreadActions.Enqueue(() =>
            {
                try
                {
                    if (!LogicLoader.Instance.LoadModuleFromBytes(
                        remote.Id, dllBytes, remote.Priority, $"remote:{remote.Version}"))
                        throw new InvalidOperationException($"{remote.Id}: 模块加载失败，未提交缓存和版本");
                    _loadedHashes[remote.Id] = Convert.ToHexString(SHA256.HashData(dllBytes));
                    _localVersions[remote.Id] = remote.Version;
                    _localPriorities[remote.Id] = remote.Priority;
                    if (EnableCache) SaveToCache(remote, dllBytes);
                    MelonLogger.Msg($"[Updater] ✓ {remote.Id} 热更新成功 → {remote.Version}");
                    completion.TrySetResult(true);
                }
                catch (Exception ex) { completion.TrySetException(ex); }
            });
            return await completion.Task;
        }

        // ================================================================
        //  内部：缓存
        // ================================================================

        private bool SaveToCache(RemoteModuleInfo remote, byte[] dllBytes)
        {
            try
            {
                Directory.CreateDirectory(CacheDir);

                string dllPath = GetModuleCachePath(remote.Id);
                string tmpDll = dllPath + ".tmp";

                File.WriteAllBytes(tmpDll, dllBytes);
                File.Move(tmpDll, dllPath, overwrite: true);

                ModuleManifest cacheManifest = null;
                if (File.Exists(ManifestCachePath))
                {
                    try
                    {
                        string oldJson = File.ReadAllText(ManifestCachePath);
                        cacheManifest = JsonConvert.DeserializeObject<ModuleManifest>(oldJson);
                    }
                    catch { }
                }

                if (cacheManifest == null)
                    cacheManifest = new ModuleManifest { Modules = new List<RemoteModuleInfo>() };
                if (cacheManifest.Modules == null)
                    cacheManifest.Modules = new List<RemoteModuleInfo>();

                var existing = cacheManifest.Modules.Find(x => x.Id == remote.Id);
                if (existing != null)
                {
                    existing.Version = remote.Version;
                    existing.Hash = remote.Hash;
                    existing.Priority = remote.Priority;
                    existing.Enabled = remote.Enabled;
                }
                else
                {
                    cacheManifest.Modules.Add(new RemoteModuleInfo
                    {
                        Id = remote.Id,
                        Version = remote.Version,
                        Hash = remote.Hash,
                        Priority = remote.Priority,
                        Enabled = remote.Enabled
                    });
                }

                string json = JsonConvert.SerializeObject(cacheManifest, Formatting.Indented);
                string tmpManifest = ManifestCachePath + ".tmp";
                File.WriteAllText(tmpManifest, json);
                File.Move(tmpManifest, ManifestCachePath, overwrite: true);

                if (LogVerbose)
                    MelonLogger.Msg($"[Updater] {remote.Id}: 已缓存 → {dllPath}");

                return true;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Updater] SaveToCache({remote.Id}) 异常: {ex.Message}");
                return false;
            }
        }


        // ================================================================
        //  ★ TCP 客户端核心
        // ================================================================

        /// <summary>
        /// 通过 TCP 拉取一个文件。会按 ServerEndpoints 顺序尝试，第一个成功的就返回。
        /// </summary>
        private async Task<byte[]> FetchTcpAsync(string fileName)
        {
            if (ServerEndpoints == null || ServerEndpoints.Length == 0)
                throw new InvalidOperationException("ServerEndpoints 未配置");

            Exception lastEx = null;

            for (int i = 0; i < ServerEndpoints.Length; i++)
            {
                string endpoint = ServerEndpoints[i];
                if (string.IsNullOrWhiteSpace(endpoint)) continue;

                string host;
                int port;

                if (!TryParseEndpoint(endpoint, out host, out port))
                {
                    MelonLogger.Warning($"[Updater] 跳过非法地址: {endpoint}");
                    continue;
                }

                try
                {
                    if (LogVerbose)
                        MelonLogger.Msg($"[Updater] 尝试 {host}:{port} → {fileName}");

                    return await FetchTcpFromAsync(host, port, fileName);
                }
                catch (Exception ex)
                {
                    lastEx = ex;
                    MelonLogger.Warning($"[Updater] {host}:{port} 失败: {ex.Message}");
                    // 继续尝试下一个
                }
            }

            // 全部失败
            throw new Exception(
                $"所有服务器都连不上（尝试了 {ServerEndpoints.Length} 个）",
                lastEx);
        }
        /// <summary>解析 "host:port" 格式</summary>
        private static bool TryParseEndpoint(string s, out string host, out int port)
        {
            host = null;
            port = 0;

            if (string.IsNullOrWhiteSpace(s)) return false;

            int idx = s.LastIndexOf(':');
            if (idx <= 0 || idx >= s.Length - 1) return false;

            host = s.Substring(0, idx).Trim();
            string portStr = s.Substring(idx + 1).Trim();

            if (string.IsNullOrEmpty(host)) return false;
            if (!int.TryParse(portStr, out port)) return false;
            if (port <= 0 || port > 65535) return false;

            return true;
        }
        /// <summary>
        /// 单次连接：连到指定的 host:port，发送请求，读取响应
        /// </summary>
        private async Task<byte[]> FetchTcpFromAsync(string host, int port, string fileName)
        {
            using (var client = new TcpClient())
            {
                using var connectTimeout = new CancellationTokenSource(ConnectTimeoutMs);
                await client.ConnectAsync(host.Trim('[', ']'), port, connectTimeout.Token);
                // 总传输期限涵盖发送、响应头及响应体，防止慢速连接无限占用更新器。
                using var transferTimeout = new CancellationTokenSource(TransferTimeoutMs);
                using (var stream = client.GetStream())
                {
                    // 1. 构造请求：[4 字节长度 大端][文件名 UTF8]
                    byte[] nameBytes = Encoding.UTF8.GetBytes(fileName);
                    if (nameBytes.Length == 0 || nameBytes.Length > 4096)
                        throw new InvalidDataException("请求路径长度非法");
                    byte[] req = new byte[4 + nameBytes.Length];
                    req[0] = (byte)((nameBytes.Length >> 24) & 0xFF);
                    req[1] = (byte)((nameBytes.Length >> 16) & 0xFF);
                    req[2] = (byte)((nameBytes.Length >> 8) & 0xFF);
                    req[3] = (byte)(nameBytes.Length & 0xFF);
                    Buffer.BlockCopy(nameBytes, 0, req, 4, nameBytes.Length);

                    await stream.WriteAsync(req, 0, req.Length, transferTimeout.Token);

                    // 2. 读响应头：[1 字节 状态][4 字节 长度 大端]
                    byte[] hdr = new byte[5];
                    await ReadExactAsync(stream, hdr, 5, transferTimeout.Token);

                    byte status = hdr[0];
                    int len = (hdr[1] << 24) | (hdr[2] << 16) | (hdr[3] << 8) | hdr[4];

                    int limit = status == 0x00
                        ? (fileName == "version.json" ? Math.Min(MaxDownloadBytes, 1024 * 1024) : MaxDownloadBytes)
                        : 4096;
                    if (len < 0 || len > limit || status > 0x02 || (status == 0x01 && len != 0))
                        throw new InvalidDataException($"服务器响应非法: status={status}, length={len}");

                    // 3. 成功
                    if (status == 0x00)
                    {
                        byte[] data = new byte[len];
                        await ReadExactAsync(stream, data, len, transferTimeout.Token);
                        return data;
                    }

                    // 4. 文件不存在
                    if (status == 0x01)
                        throw new FileNotFoundException($"服务器没有文件: {fileName}");

                    // 5. 其他错误
                    byte[] msgBuf = new byte[len];
                    await ReadExactAsync(stream, msgBuf, len, transferTimeout.Token);
                    throw new Exception($"服务器返回错误: {Encoding.UTF8.GetString(msgBuf)}");
                }
            }
        }
        private static async Task ReadExactAsync(NetworkStream s, byte[] buf, int count, CancellationToken token)
        {
            int total = 0;
            while (total < count)
            {
                int n = await s.ReadAsync(buf, total, count - total, token);
                if (n <= 0)
                    throw new IOException("连接被服务器关闭");
                total += n;
            }
        }

        // ================================================================
        //  工具
        // ================================================================

        private string GetLocalVersion(string moduleId)
        {
            if (_localVersions.TryGetValue(moduleId, out var loadedVersion)
                && LogicLoader.Instance.HasModule(moduleId))
                return loadedVersion;

            var logic = LogicLoader.Instance.GetModule(moduleId);
            if (logic != null && !string.IsNullOrEmpty(logic.Version))
                return logic.Version;

            return "0.0.0";
        }

        private string GetLocalHash(string moduleId)
        {
            if (_loadedHashes.TryGetValue(moduleId, out var loadedHash)
                && LogicLoader.Instance.HasModule(moduleId)) return loadedHash;
            if (!EnableCache) return null;

            string path = GetModuleCachePath(moduleId);
            if (!File.Exists(path)) return null;

            try
            {
                using (var sha = SHA256.Create())
                using (var fs = File.OpenRead(path))
                {
                    byte[] hash = sha.ComputeHash(fs);
                    return BitConverter.ToString(hash)
                        .Replace("-", "").ToLowerInvariant();
                }
            }
            catch { return null; }
        }

        private bool IsNewer(string remote, string local)
        {
            if (string.IsNullOrEmpty(remote)) return false;
            if (string.IsNullOrEmpty(local)) return true;

            try { return new Version(remote) > new Version(local); }
            catch { return remote != local; }
        }

        private bool VerifySHA256(byte[] data, string expectedHash)
        {
            try
            {
                using (var sha = SHA256.Create())
                {
                    byte[] hash = sha.ComputeHash(data);
                    string actual = BitConverter.ToString(hash)
                        .Replace("-", "").ToLowerInvariant();
                    return actual == expectedHash.ToLowerInvariant().Replace("-", "");
                }
            }
            catch { return false; }
        }
    }

    // ================================================================
    //  数据模型
    // ================================================================

    public class ModuleManifest
    {
        [JsonProperty("modules")]
        public List<RemoteModuleInfo> Modules { get; set; }
    }

    public class RemoteModuleInfo
    {
        [JsonProperty("id")] public string Id { get; set; }
        [JsonProperty("version")] public string Version { get; set; }
        [JsonProperty("url")] public string Url { get; set; }   // 提取路径，通过配置的 TCP 服务器下载
        [JsonProperty("hash")] public string Hash { get; set; }
        [JsonProperty("priority")] public int Priority { get; set; } = 100;
        [JsonProperty("enabled")] public bool Enabled { get; set; } = true;
        [JsonProperty("notes")] public string Notes { get; set; }
    }
}