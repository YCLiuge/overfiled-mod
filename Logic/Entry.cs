
using MelonLoader;
using ModLoader;
using System;
using System.Reflection;
using Logic;
namespace Logic_v2   // ★ 命名空间带版本号，避免类型冲突
{
    public class Entry : ILogic
    {
        public string Name => "ex_mod";
        public string Version =>
        System.Reflection.Assembly.GetExecutingAssembly()
        .GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>()
        ?.InformationalVersion ?? "0.0.0";
        // ★ 每次热重载都新建一个 Harmony 实例，ID 带 Guid 保证唯一
        private HarmonyLib.Harmony _harmony;

        public void OnLoad()
        {
            MelonLogger.Msg($"[{Name}] OnLoad v{Version}");

            // ★ 用唯一 ID，避免和旧 ALC 里的旧补丁冲突
            string harmonyId = $"com.you.logic_main.{Guid.NewGuid():N}";
            _harmony = new HarmonyLib.Harmony(harmonyId);

            try
            {
                // 方式 A：自动扫描本程序集里所有 [HarmonyPatch] 类
                _harmony.PatchAll(Assembly.GetExecutingAssembly());
                MelonLogger.Msg($"[{Name}] ✓ PatchAll 完成");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[{Name}] PatchAll 失败: {ex.Message}\n{ex.StackTrace}");
            }

            // 方式 B：手动指定（当自动扫描不生效时用）
            // PatchManually();

            MelonLogger.Msg($"[{Name}] OnLoad 完成");
            MainCore.i.start();
        }

        // ============================================================
        //  OnUpdate：每帧逻辑
        // ============================================================
        public void OnUpdate()
        {
            // Keep HTTP dispatch ahead of manager updates that may throw.
            AIControlServer.Tick();
            // 每帧要跑的逻辑放这
            MainCore.i.update();
        }

        // ============================================================
        //  OnUnload：撤销补丁（★ 必须做）
        // ============================================================
        public void OnUnload()
        {
            AIControlServer.Stop();
            MelonLogger.Msg($"[{Name}] OnUnload 开始");

            try
            {
                // ★ 撤销本 Harmony 实例的所有补丁
                _harmony?.UnpatchSelf();
                MelonLogger.Msg($"[{Name}] ✓ UnpatchSelf 完成");
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[{Name}] UnpatchSelf 异常: {ex.Message}");
            }
            finally
            {
                _harmony = null;
            }

            MelonLogger.Msg($"[{Name}] OnUnload 完成");
            MainCore.i.OnUnload();
        }
    
    }
   
}