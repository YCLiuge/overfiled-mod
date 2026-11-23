using HarmonyLib;
using Il2CppInutan;
using Il2CppPb;
using Il2CppProtocol;
using MelonLoader;
using Microsoft.VisualBasic;
using System.Collections;
using UnityEngine;
using mod.st;
namespace mod.tool
{
    internal class Test
    {
        public static Test i = new();
        Test() { if (i == null) i = this; }

        public static void test(string msg) 
        {
            var msgs = msg.Split(' ');


            JellyScaleAnimator.Instance.SetTarget(TipMsgCore.i.mainplayer.transform);

            MelonCoroutines.Start(i.IETest(msgs));
            // MelonCoroutines.Start(Fk_fanction.PutDown_fk_temp(5040010, tokensys.i.mainplayer.transform.position, new()));

        }
        
        IEnumerator IETest(string[] strings)
        {
            yield return null;
            TipMsgCore.i.OK();
        }
    }
    //[HarmonyPatch(typeof(HomeSystem), "CreateAndPutDown")]
    //class Patch_CreateAndPutDown
    //{
    //    // 在方法执行前记录参数
    //    [HarmonyPrefix]
    //    static void Prefix(FurnitureDetailsInfo data, long playerId)
    //    {
    //        if (data == null)
    //        {
    //            MelonLogger.Warning("CreateAndPutDown: data 为 null");
    //            return;
    //        }

    //        MelonLogger.Msg($"[CreateAndPutDown] 玩家 {playerId} 尝试放置家具：");
    //        MelonLogger.Msg($"  家具ID: {data.FurnitureItemId}");
    //        MelonLogger.Msg($"  实例ID: {data.FurnitureId}");
    //        MelonLogger.Msg($"  位置: {data.Pos}");
    //        MelonLogger.Msg($"  旋转: {data.Rotation}");
    //        MelonLogger.Msg($"  图层: {data.LayerNum}");
    //    }

    //    //// 在方法执行后记录（可选）
    //    //[HarmonyPostfix]
    //    //static void Postfix()
    //    //{
    //    //    MelonLogger.Msg("CreateAndPutDown 执行完毕（网络请求已发送）");
    //    //}
    //}
}
