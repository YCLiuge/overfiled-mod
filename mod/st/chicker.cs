using Il2CppInutan;
using Il2CppPb;
using MelonLoader;
using HarmonyLib;

namespace mod.st
{
    //[HarmonyPatch(typeof(HomeSystem), "CreateAndPutDown")]
    //class Patch_CreateAndPutDown
    //{
    //    [HarmonyPrefix]
    //    static void Prefix(FurnitureDetailsInfo data, long playerId)
    //    {
    //        if (data == null)
    //        {
    //            MelonLogger.Warning("[CreateAndPutDown] data 为 null");
    //            return;
    //        }

    //        MelonLogger.Msg("===== CreateAndPutDown 被调用 =====");
    //       // MelonLogger.Msg($"  实例: {(__instance != null ? __instance.GetType().Name : "null")}");
    //        MelonLogger.Msg($"  playerId: {playerId}");
    //        MelonLogger.Msg($"  家具配置ID (FurnitureItemId): {data.FurnitureItemId}");
    //        MelonLogger.Msg($"  家具实例ID (FurnitureId): {data.FurnitureId}");
    //        MelonLogger.Msg($"  位置 Pos: ({data.Pos.X}, {data.Pos.Y}, {data.Pos.Z})");
    //        MelonLogger.Msg($"  旋转 Rot: ({data.Rotation?.X}, {data.Rotation?.Y}, {data.Rotation?.Z})");
    //        MelonLogger.Msg($"  层级 LayerNum: {data.LayerNum}");
    //    }
    //}
}
