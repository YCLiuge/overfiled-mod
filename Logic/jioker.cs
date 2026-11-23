using HarmonyLib;
using Il2Cpp;
using Il2CppInutan;
using Il2CppPb;
using MelonLoader;
using System.Collections.Generic;
using UnityEngine;
namespace Logic
{
    public static class Obj
    {
        static GameObject light;
        static GameObject ttl;
        static GameObject ttl_main;
        static List<UnityEngine.Vector3> ttl_bsPositions;
        static GameObject huajia;

        //-------------------------------------------场景/---------------------普通物体/家具店画架
        public static GameObject Get_light()
        {
            if (light == null) light = GameObject.Find("-------------------------------------------后期");
            return light;
        }
        public static GameObject Get_ttl() 
        {
            if (ttl == null) ttl = GameObject.Find("[UIMainRoot]/UI_Root_Title(Clone)/Canvas/StateRoot/TitleParent");
            return ttl;
        }
        public static GameObject Get_ttl_main()
        {
            if (ttl_main == null) ttl_main = GameObject.Find("[UIMainRoot]/UI_Root_Main(Clone)/Canvas/Rect");
            return ttl_main;
        }
        public static List<UnityEngine.Vector3> Get_ttl_bsPositions()
        {
            if (ttl_bsPositions == null)
            {
                ttl_bsPositions = new List<UnityEngine.Vector3>();
                if (ttl_main == null) ttl_main = GameObject.Find("[UIMainRoot]/UI_Root_Main(Clone)/Canvas/Rect");
                if (ttl_main != null)
                {
                    var b = ttl_main.transform;
                    for (int i = 0; i < b.childCount; i++) 
                    {
                        ttl_bsPositions.Add(b.GetChild(i).transform.position);
                    }
                }
            }
            return ttl_bsPositions;
        }
        public static GameObject Get_huajia_clone() 
        {
            if (huajia == null) huajia = GameObject.Find("-------------------------------------------场景/---------------------普通物体/家具店画架");

            return GameObject.Instantiate(huajia);
        }

    }
    [HarmonyPatch(typeof(SpellSystem), "TryCastActiveSpell")]
    class SpellTool
    {
        [HarmonyPrefix]
        static bool Prefix(Unit caster, int spellID, int levelIndex, Unit target, UnityEngine.Vector3 direction, SpellCustomParameter parameter, bool ignoreLockState, bool ignoreGroundState, ref SpellData spell)
        {
            
            if(SpellSystem.Instance.IsUltraSpell(caster, spellID))
            {
                PlayerSystem.Instance.ChangeMainPlayerUltraPoint(200);
            }
            else
            {
                PlayerSystem.Instance.ChangeMainPlayerActionPoint(100);
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(PlayerSystem), "OnChannelJumpCallback")]
    public static class Patch_ChannelJump
    {
        // Prefix 在原方法执行前触发
        public static void Prefix(ChangeSceneChannelRsp rsq)
        {
            if (rsq == null) return;

            // 1. 打印核心状态（StatusCode 是个枚举，直接转字符串看）
            MelonLogger.Msg($"[频道回调] Status: {rsq.Status}, 频道ID: {rsq.ChannelId}");
            //bundle load assets_development_arts_model_character_character_25_skin.bundle assets/development/arts/model/character/character_25/skin/character_25@skin_body.fbx
            // 2. 判断是否成功（通常 OK 或 0 代表成功，具体以你打印出的结果为准）
            // 如果你能反编译看 StatusCode 枚举的定义，可以直接写 rsq.Status == StatusCode.OK
            if (rsq.Status.ToString() != "Ok" && (int)rsq.Status != 0)
            {
                MelonLogger.Warning($"[频道] 进频道失败！错误码: {rsq.Status}");

                // 3. 看看是否有冷却限制
                if (rsq.PasswordAllowTime > 0)
                {
                    MelonLogger.Warning($"[频道] 触发密码冷却，下次允许尝试的时间/倒计时: {rsq.PasswordAllowTime}");
                }
            }
            else
            {
                MelonLogger.Msg($"[频道] 成功进入频道 {rsq.ChannelId}!");
            }
        }
    }

    [HarmonyPatch(typeof(PlayerSystem), "ChangeMainPlayerActionPoint")]
    public static class Patch_ChangeActionPoint
    {
        public static void Prefix(ref int value)
        {

        }
    }

    [HarmonyPatch(typeof(PlayerSystem), "ChangeMainPlayerUltraPoint")]
    public static class Patch_ChangeUltraPoint
    {
        public static void Prefix(ref int value)
        {

        }
    }

    //[HarmonyPatch(typeof(CameraSystem), "SetCameraFeatureUnit")]
    //public static class Patch_SetCameraFeatureUnit
    //{
    //    // 参数列表必须和原方法完全一致，类型、顺序、个数都不能错
    //    public static void Prefix(
    //        GameObject targetUnit,
    //        float p2, float p3, bool p4,
    //        float p5, float p6, float p7, float p8, float p9, float p10, float p11)
    //    {
    //        // 防止目标为空导致报错
    //        string unitName = targetUnit != null ? targetUnit.name : "null";

    //        // 打印所有参数（你可以根据游戏实际内容，把 f2~f11 替换成具体的含义，比如距离、高度、FOV、偏移量等）
    //        MelonLogger.Msg($"[CameraMonitor] 设置相机特征单位:");
    //        MelonLogger.Msg($"  -> 目标: {unitName}");
    //        MelonLogger.Msg($"  -> 参数: [{p2:F2}, {p3:F2}], Bool: {p4}");
    //        MelonLogger.Msg($"  -> 浮点组: [{p5:F2}, {p6:F2}, {p7:F2}, {p8:F2}, {p9:F2}, {p10:F2}, {p11:F2}]");
    //    }
    //}
}
