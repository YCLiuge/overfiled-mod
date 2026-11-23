using Il2Cpp;
using Il2CppInutan;
using MelonLoader;
using mod;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using HarmonyLib;

namespace Logic.管理器
{
    internal class AotuFishing
    {
        public static bool on = false;
        static float timer1 = 0;
        public static void update()
        {
            if (FishingSystem.Instance == null) return;
            if (on)
            {
                if (Input.GetKeyDown(KeyCode.P)) 
                { 
                    on = false;
                    FishingSystem.Instance.SendFishingRequest(true);
                    return; 
                }
                if (FishingSystem.Instance.CanPerformAction(FishingState.Idle) && on && timer1 <= 0)
                {
                    timer1 = 2;
                    FishingSystem.Instance.SendFishingRequest(false, 1006009, 5021005, 1001);
                }
                if (timer1 > 0) timer1 -= Time.deltaTime;
            }
        }
        public static void startFishing()
        {
            on = true;
        }
    }


    [HarmonyPatch(typeof(FishingSystem), "SendFishingRequest")]
    public static class Patch_SendFishingRequest_Monitor
    {
        // Prefix 在原方法执行前触发
        public static void Prefix(bool isCancel, int fishingRodId, int baitId, int fishingPoolId)
        {
            string action = isCancel ? "【取消/收竿】" : "【发起/抛竿】";
            MelonLogger.Msg($"[监视] 发送钓鱼请求 {action} | 鱼竿ID: {fishingRodId} | 鱼饵ID: {baitId} | 鱼池ID: {fishingPoolId}");
            if (isCancel) AotuFishing.on = false;
        }
    }
}
