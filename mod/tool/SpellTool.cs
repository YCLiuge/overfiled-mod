using HarmonyLib;
using Il2Cpp;
using MelonLoader;
using UnityEngine;


namespace mod.tool
{
    [HarmonyPatch(typeof(SpellSystem), "TryCastActiveSpell")]
    class SpellTool
    {
        [HarmonyPrefix]
        static bool Prefix(Unit caster, int spellID, int levelIndex, Unit target, Vector3 direction, SpellCustomParameter parameter, bool ignoreLockState, bool ignoreGroundState, ref SpellData spell)
        {
           // MelonLogger.Msg($"尝试释放技能 ID={spellID}，施法者={caster?.name}");
            // 可以在这里修改参数或阻止释放（返回 false）
            
            SpellSystem.Instance.SetSpellNowCD(caster, spellID, 0f);
            return true;
        }
    }
    [HarmonyPatch(typeof(SpellSystem), "OnSpellUpdateCD")]
    class Patch_OnSpellUpdateCD
    {
        [HarmonyPrefix]
        static void Prefix(Unit caster, int spellID, float extCD, bool isResetTime)
        {
            MelonLogger.Msg($"技能 {spellID} 冷却更新，额外CD={extCD}, 重置={isResetTime}");
        }
    }
}
