using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using MelonLoader;

namespace ModLoader
{
    /// <summary>
    /// 每个模块一个独立 ALC，支持卸载
    /// </summary>
    internal class ModLoadContext : AssemblyLoadContext
    {
        public ModLoadContext(string name) : base(name, isCollectible: true) { }

        // 返回 null，让依赖解析回退到默认 ALC
        // 这样 Il2Cpp 的 wrapper、MelonLoader 等共享程序集都能被找到
        protected override Assembly Load(AssemblyName name)
        {
            return null;
        }
    }

    /// <summary>逻辑模块信息</summary>
    public class LogicModule
    {
        public string Id { get; set; }          // 模块标识（唯一）
        public ILogic Logic { get; set; }       // 模块实例
        public Assembly Assembly { get; set; }  // 模块程序集
        public AssemblyLoadContext Context { get; set; }  // ★ 模块专属 ALC
        public string SourcePath { get; set; }  // 来源（本地路径或 "remote:xxx"）
        public int Priority { get; set; }       // 优先级（数字小的先执行）
    }

    /// <summary>
    /// 多模块逻辑加载器（支持真正的热重载）
    /// </summary>
    public class LogicLoader
    {
        public static LogicLoader Instance { get; } = new LogicLoader();

        // ---------- 模块注册表 ----------
        private readonly Dictionary<string, LogicModule> _modules
            = new Dictionary<string, LogicModule>();

        // 仅在注册表变更时重建；Tick 持有本帧的不可变排序快照。
        private LogicModule[] _orderedModules = Array.Empty<LogicModule>();

        // 生命周期在主线程串行执行，拒绝 OnLoad/OnUnload/事件中的重入修改。
        private bool _changingModules;

        public int ModuleCount => _modules.Count;

        // ---------- 事件 ----------
        public event Action<string, ILogic> OnModuleLoaded;
        public event Action<string> OnModuleUnloaded;

        // ================================================================
        //  加载
        // ================================================================

        /// <summary>
        /// 从文件加载一个逻辑模块
        /// </summary>
        public bool LoadModuleFromFile(string moduleId, string path, int priority = 100)
        {
            if (string.IsNullOrEmpty(moduleId))
            {
                MelonLogger.Error("[LogicLoader] moduleId 为空");
                return false;
            }

            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                MelonLogger.Warning($"[LogicLoader] 文件不存在: {path}");
                return false;
            }

            try
            {
                byte[] bytes = File.ReadAllBytes(path);
                return LoadModuleFromBytes(moduleId, bytes, priority, path);
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[LogicLoader] 读取 {moduleId} 失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 从内存加载一个逻辑模块（仅主线程调用，使用独立 ALC）。
        /// 校验/构造失败保留旧实例；切换失败先清理新实例，再尽力重新初始化旧实例。
        /// ILogic 无事务契约，不能保证状态恢复或撤销模块未清理的外部副作用。
        /// </summary>
        public bool LoadModuleFromBytes(
            string moduleId, byte[] dllBytes,
            int priority = 100, string source = "memory")
        {
            if (string.IsNullOrEmpty(moduleId))
            {
                MelonLogger.Error("[LogicLoader] moduleId 为空");
                return false;
            }

            if (dllBytes == null || dllBytes.Length == 0)
            {
                MelonLogger.Error($"[LogicLoader] {moduleId} 数据为空");
                return false;
            }

            if (!BeginChange()) return false;

            ModLoadContext alc = null;
            ILogic logic = null;
            LogicModule previous = null;
            bool previousSuspended = false;
            bool committed = false;
            bool previousCleanupSucceeded = false;

            try
            {
                // 先完成程序集校验和构造；失败时旧模块继续运行。
                // 独立 ALC 可以容纳同名程序集，无需强制 GC。
                // 构造函数必须避免外部副作用：接口无法回滚静态初始化或构造失败。
                // ---------- 创建独立 ALC ----------
                string alcName = $"Mod_{moduleId}_{Guid.NewGuid():N}";
                alc = new ModLoadContext(alcName);

                // ---------- 从流加载程序集 ----------
                Assembly asm;
                try
                {
                    using (var ms = new MemoryStream(dllBytes))
                    {
                        asm = alc.LoadFromStream(ms);
                    }
                }
                catch (BadImageFormatException)
                {
                    MelonLogger.Error($"[LogicLoader] {moduleId} 不是有效程序集");
                    return false;
                }
                catch (Exception ex)
                {
                    MelonLogger.Error($"[LogicLoader] {moduleId} 程序集加载失败: {ex.Message}");
                    return false;
                }

                // ---------- 查找 ILogic 实现 ----------
                Type entryType = FindLogicType(asm);
                if (entryType == null)
                {
                    MelonLogger.Error($"[LogicLoader] {moduleId} 里没有 ILogic 实现");
                    return false;
                }

                if (entryType.IsAbstract)
                {
                    MelonLogger.Error($"[LogicLoader] {moduleId} 的 ILogic 实现是抽象类");
                    return false;
                }

                // ---------- 实例化 ----------
                try
                {
                    logic = (ILogic)Activator.CreateInstance(entryType);
                }
                catch (Exception ex)
                {
                    MelonLogger.Error($"[LogicLoader] {moduleId} 实例化失败: {ex.Message}");
                    return false;
                }

                if (logic == null)
                {
                    MelonLogger.Error($"[LogicLoader] {moduleId} 实例化为 null");
                    return false;
                }

                // 提前读取模块元数据，避免属性 getter 失败破坏已完成的切换。
                string name = logic.Name;
                string version = logic.Version;

                // 旧 ALC 保留到新模块成功初始化之后，才能尝试恢复同一个旧实例。
                if (_modules.TryGetValue(moduleId, out previous))
                {
                    _modules.Remove(moduleId);
                    RebuildOrder();
                    previousSuspended = true;
                    // 旧清理失败时直接中止，不激活新模块。
                    previous.Logic?.OnUnload();
                    previousCleanupSucceeded = true;
                }

                // 同一时间只允许一代模块的 OnLoad 副作用处于激活状态。
                logic.OnLoad();

                // ---------- 注册 ----------
                var module = new LogicModule
                {
                    Id = moduleId,
                    Logic = logic,
                    Assembly = asm,
                    Context = alc,
                    SourcePath = source,
                    Priority = priority
                };
                _modules[moduleId] = module;
                RebuildOrder();
                committed = true;

                if (previous != null)
                {
                    SafeUnload(previous.Context);
                    NotifyUnloaded(moduleId);
                }

                MelonLogger.Msg(
                    $"[LogicLoader] ✓ 模块已加载: {moduleId} " +
                    $"({name} v{version}) @ {source}");

                try { OnModuleLoaded?.Invoke(moduleId, logic); }
                catch (Exception ex)
                {
                    MelonLogger.Warning(
                        $"[LogicLoader] OnModuleLoaded 事件异常: {ex.Message}");
                }

                return true;
            }
            catch (Exception ex)
            {
                MelonLogger.Error(
                    $"[LogicLoader] {moduleId} 加载异常: {ex.Message}\n{ex.StackTrace}");
                return committed;
            }
            finally
            {
                try
                {
                    if (!committed)
                    {
                        // OnLoad 可能先产生副作用再抛异常，必须先清理候选实例。
                        bool cleaned = TryCleanup(moduleId, logic);
                        SafeUnload(alc);
                        if (previousSuspended)
                        {
                            // 清理失败时无法确认新副作用已撤销，不能再激活旧模块。
                            if (!previousCleanupSucceeded || !cleaned || !TryRestore(previous))
                            {
                                if (!previousCleanupSucceeded)
                                    MelonLogger.Error($"[LogicLoader] 旧模块 {moduleId} 清理失败，停止切换且不重复初始化旧实例");
                                if (!cleaned)
                                    MelonLogger.Error($"[LogicLoader] 新实例清理失败，旧模块 {moduleId} 保持停用以避免副作用叠加");
                                SafeUnload(previous.Context);
                                NotifyUnloaded(moduleId);
                            }
                        }
                    }
                }
                finally { _changingModules = false; }
            }
        }

        // ================================================================
        //  卸载
        // ================================================================

        public bool UnloadModule(string moduleId)
        {
            if (string.IsNullOrEmpty(moduleId) || !BeginChange()) return false;
            try { return UnloadModuleCore(moduleId); }
            finally { _changingModules = false; }
        }

        private bool UnloadModuleCore(string moduleId)
        {
            if (!_modules.TryGetValue(moduleId, out var module)) return false;

            // 先移出快照，避免清理过程重入 Tick 时执行正在卸载的实例。
            _modules.Remove(moduleId);
            RebuildOrder();
            TryCleanup(moduleId, module.Logic);
            SafeUnload(module.Context);
            MelonLogger.Msg($"[LogicLoader] ✗ 模块已卸载: {moduleId}");
            NotifyUnloaded(moduleId);
            return true;
        }

        public void UnloadAll()
        {
            if (!BeginChange()) return;
            try
            {
                var snapshot = _orderedModules;
                foreach (var module in snapshot)
                    UnloadModuleCore(module.Id);
            }
            finally { _changingModules = false; }

            // ALC 的实际回收交给运行时，不在主线程强制 GC 或等待终结器。
        }

        // ================================================================
        //  每帧驱动
        // ================================================================

        public void Tick()
        {
            if (_changingModules) return;
            var snapshot = _orderedModules;
            for (int i = 0; i < snapshot.Length; i++)
            {
                var module = snapshot[i];
                string id = module.Id;
                // 本帧被卸载或替换的旧实例不再执行；新增实例下一帧开始执行。
                if (!_modules.TryGetValue(id, out var current) ||
                    !ReferenceEquals(current, module) || module.Logic == null) continue;

                try
                {
                    module.Logic.OnUpdate();
                }
                catch (Exception ex)
                {
                    MelonLogger.Error(
                        $"[LogicLoader] 模块 {id}.OnUpdate 异常: {ex.Message}");
                    // 单个模块异常不影响其他模块
                }
            }
        }
        // ================================================================
        //  查询
        // ================================================================

        public ILogic GetModule(string moduleId)
        {
            return _modules.TryGetValue(moduleId, out var m) ? m.Logic : null;
        }

        public LogicModule GetModuleInfo(string moduleId)
        {
            return _modules.TryGetValue(moduleId, out var m) ? m : null;
        }

        public bool HasModule(string moduleId)
        {
            return _modules.ContainsKey(moduleId);
        }

        public IEnumerable<LogicModule> GetAllModules()
        {
            return _modules.Values;
        }

        // ================================================================
        //  内部
        // ================================================================

        private bool BeginChange()
        {
            if (_changingModules)
            {
                MelonLogger.Warning("[LogicLoader] 生命周期回调中不允许重入加载/卸载模块");
                return false;
            }
            _changingModules = true;
            return true;
        }

        private bool TryCleanup(string moduleId, ILogic logic)
        {
            try
            {
                logic?.OnUnload();
                return true;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning(
                    $"[LogicLoader] {moduleId}.OnUnload 异常，副作用可能残留: {ex.Message}");
                return false;
            }
        }

        // ILogic 没有状态保存/事务接口，只能尽力对同一个旧实例再次 OnLoad。
        // 这要求 OnUnload 能撤销部分初始化，旧实例也能重新初始化；无法恢复内存状态、
        // 外部 I/O、静态初始化、构造函数副作用或未解除的事件/补丁。ALC.Unload 不会撤销它们。
        private bool TryRestore(LogicModule previous)
        {
            try
            {
                previous.Logic.OnLoad();
                _modules[previous.Id] = previous;
                RebuildOrder();
                MelonLogger.Warning($"[LogicLoader] 已尽力恢复旧模块: {previous.Id}（不保证原运行状态）");
                return true;
            }
            catch (Exception ex)
            {
                _modules.Remove(previous.Id);
                RebuildOrder();
                MelonLogger.Error($"[LogicLoader] 旧模块 {previous.Id} 恢复失败: {ex.Message}");
                TryCleanup(previous.Id, previous.Logic);
                return false;
            }
        }

        private void NotifyUnloaded(string moduleId)
        {
            try { OnModuleUnloaded?.Invoke(moduleId); }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[LogicLoader] OnModuleUnloaded 事件异常: {ex.Message}");
            }
        }

        private void RebuildOrder()
        {
            var snapshot = new LogicModule[_modules.Count];
            _modules.Values.CopyTo(snapshot, 0);
            Array.Sort(snapshot, (a, b) =>
            {
                int priority = a.Priority.CompareTo(b.Priority);
                return priority != 0 ? priority : StringComparer.Ordinal.Compare(a.Id, b.Id);
            });
            _orderedModules = snapshot;
        }

        /// <summary>
        /// 在程序集里查找第一个 ILogic 实现
        /// </summary>
        private Type FindLogicType(Assembly asm)
        {
            Type[] types;
            try
            {
                types = asm.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                types = ex.Types;
                MelonLogger.Warning(
                    $"[LogicLoader] 部分类型加载失败（{ex.LoaderExceptions?.Length ?? 0} 个）");
            }

            if (types == null) return null;

            foreach (var t in types)
            {
                if (t == null) continue;
                if (t.IsAbstract) continue;
                if (t.IsInterface) continue;
                if (!t.IsClass) continue;
                if (typeof(ILogic).IsAssignableFrom(t)) return t;
            }
            return null;
        }

        /// <summary>
        /// 安全卸载 ALC（吞掉异常）
        /// </summary>
        private void SafeUnload(AssemblyLoadContext alc)
        {
            if (alc == null) return;
            try { alc.Unload(); }
            catch { /* 静默 */ }
        }
    }
}