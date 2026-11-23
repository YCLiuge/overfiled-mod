using mod;
using HarmonyLib;
using Il2Cpp;
using Il2CppInutan;
using Il2CppKinematicCharacterController;
using MelonLoader;
using mod.tool;
using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using static Il2CppInutan.HomeSystem;

namespace HomeEditorModule.Core
{

    internal class FreeSetHomeItem
    {
        public static FreeSetHomeItem i = new();
        FreeSetHomeItem() { if (i == null) i = this; }

        //-------------------------------------------场景/---------------------主建筑/主城_喷泉_B
        GameObject dimian;
        GameObject dimian_clone;
        GameObject wg;
        KinematicCharacterMotor plyer;
        Vector3 plahigh = new Vector3(0, 0.35f, 0);
        public static Vector3 plehighex = new Vector3(0, 0, 0);
        public float moveSpeed = 5f;          // 移动速度
        Vector3 vector3 = Vector3.zero;
        // public float Y = 0;
        public bool canfly = false;
        float timer = 0;
        public void Start()
        {
            dimian = GameObject.Find("-------------------------------------------场景/---------------------主建筑/主城_喷泉_B");
            wg = GameObject.Find("放置网格/PlacementGrid_9999(Clone)/Grid_1");
            plyer = TipMsgCore.i.mainplayer;
            if (dimian == null || wg == null || plyer == null || dimian.transform.childCount == 0)
                throw new InvalidOperationException("家园地面、放置网格或玩家未就绪");
            EditorRuntime.Remember(dimian); EditorRuntime.Remember(wg); EditorRuntime.Remember(plyer.gameObject);
            EditorRuntime.Remember(dimian.transform.GetChild(0).gameObject);
            dimian_clone = GameObject.Instantiate(dimian); EditorRuntime.Own(dimian_clone);
            dimian.transform.GetChild(0).gameObject.SetActive(false);
            dimian.transform.localScale = new(20, 1, 20); wg.transform.localScale = new(20, 1, 20);
        }
        public void Updata(string name, string msg)
        {
            if (name != TipMsgCore.i.mainplayername) msg = "";
            if (plyer == null) plyer = TipMsgCore.i.mainplayer;
            if (timer > 0) timer -= Time.deltaTime;
            // FK owns character movement; the shared flight path also consumes arrow keys.
            bool fk = AutoHomeItem.Stute == Stute_AutoHomeItem.Fking;
            if (fk) canfly = false;
            if (!fk && Input.GetKey(KeyCode.Alpha3) && timer <= 0)
            {

                timer = 0.5f;
                canfly = !canfly;
                stc.Log("飞行模式:" + canfly);
                EditorBridge.Y = plyer.transform.position.y;
            }

            if (canfly)
            {
                Move();
            }
            else
            {
                DimMove();
            }
            Setinit();
            AutoHomeItem.i.Update(msg);
        }
        public void Setinit()
        {
            dimian.transform.position = plyer.transform.position + plahigh + plehighex;
            wg.transform.position = plyer.transform.position + plahigh + plehighex;
        }
        public void Recover()
        {
            plehighex = Vector3.zero; canfly = false; timer = 0;
            dimian = null; wg = null; plyer = null; dimian_clone = null;
        }

        void DimMove()
        {
            float yAxis = 0f;
            if (Input.GetKey(KeyCode.Alpha1))
                yAxis = 1f;
            else if (Input.GetKey(KeyCode.Alpha2))
                yAxis = -1f;
            plehighex.y += yAxis * moveSpeed * Time.deltaTime;

        }
        void Move()
        {
            //    // 获取水平/垂直输入（A/D 或 左/右 对应横向，W/S 或 上/下 对应纵向）
            float horizontal = Input.GetAxis("Horizontal");
            float vertical = Input.GetAxis("Vertical");

            // ----- Y 轴控制（数字键 1 向上，2 向下）-----
            float yAxis = 0f;
            if (Input.GetKey(KeyCode.Alpha1))
                yAxis = 1f;
            else if (Input.GetKey(KeyCode.Alpha2))
                yAxis = -1f;
            
            // ----- 构建局部坐标系下的移动方向 -----
            // 前后方向：玩家的 forward 方向（红色轴）
            Vector3 forwardMove = Camera.main.transform.forward * vertical;
            forwardMove = new(forwardMove.x, 0, forwardMove.z);
            // 左右方向：玩家的 right 方向（蓝色轴）
            Vector3 rightMove = Camera.main.transform.right * horizontal;
            rightMove = new(rightMove.x, 0, rightMove.z);
            // 上下方向：根据选项选择局部上方向（绿色轴）或世界 Y 轴
            Vector3 upMove;

            upMove = plyer.transform.up * yAxis;


            // 合成最终方向并归一化（保证斜向速度恒定）
            Vector3 direction = (forwardMove + rightMove + upMove).normalized;

            if (direction != Vector3.zero)
            {
                float exspeed = 1;
                if (Input.GetKey(KeyCode.LeftControl)) exspeed = 0.1f;
                Vector3 movement = direction * moveSpeed * Time.deltaTime * exspeed;
                EditorBridge.Y += movement.y;
                // 按照你的要求，使用 plyer.SetPosition
                plyer.SetPosition(plyer.transform.position + movement);
            }
            plyer.SetPosition(new Vector3(plyer.transform.position.x, EditorBridge.Y, plyer.transform.position.z));
        }
        public static void SetModelTransparencyFull(GameObject target, float alpha)
        {
            Renderer renderer = target.GetComponent<Renderer>();
            if (renderer == null) return;

            Material mat = renderer.material;

            // 1. 切换渲染模式为 Transparent
            mat.SetFloat("_Mode", 3); // 3 = Transparent, 2 = Fade
            mat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.EnableKeyword("_ALPHABLEND_ON");
            mat.renderQueue = (int)RenderQueue.Transparent;

            // 2. 设置透明度
            Color color = mat.color;
            color.a = Mathf.Clamp01(alpha);
            mat.color = color;
        }
       // public static GameObject CarryedItem;
        IEnumerator Settrue(GameObject gameObject)
        {
            //FreeSetHomeItem.i.Recover();
            yield return new WaitForSeconds(1f);
            gameObject.SetActive(true);

        }
        static IEnumerator creatfk(int id)
        {
            yield return new WaitForSeconds(0.2f);
            IHomeSystem.sys.CreateAndPickUp(id);
        }
        [HarmonyPatch(typeof(UIHomeSystem), "OnClickPutdown")]
        class comUIHomeSystem_OnClickPutdown
        {
            [HarmonyPrefix]
            static void Prefix()
            {
                if (!EditorRuntime.Ready) return;
                if (IHomeSystem.CarryingItem != null)
                {
                    
                    EditorBridge.itemtemp = IHomeSystem.CarryingItem;
                    //FreeSetHomeItem.i.Setinit();
                    EditorRuntime.RememberVisibility(IHomeSystem.CarryingItem.gameObject);
                    IHomeSystem.CarryingItem.gameObject.SetActive(false);
                    var CarryedItem = IHomeSystem.CarryingItem.gameObject;
                    EditorRuntime.Start(FreeSetHomeItem.i.Settrue(CarryedItem)); // 注意括号，调用方法获取 IEnumerator
                    if (Fksys.use)
                    {
                        Fksys.use = false;
                        DetectInfo det;
                        det = new DetectInfo() { pos = TipMsgCore.i.mainplayer.transform.position, rot = TipMsgCore.i.mainplayer.transform.rotation, gridId = IHomeSystem.sys.detectInfo.gridId };
                        IHomeSystem.sys.detectInfo = det;
                        EditorRuntime.Start(creatfk(IHomeSystem.CarryingItem.tableId));
                    }
                }
            }
        }

        //[HarmonyPatch(typeof(HomeSystem), "GetNowHomeItemPointMaxCount")]
        //class Patch_GetNowHomeItemPointMaxCount
        //{
        //    // 方法一：使用 Postfix 修改返回值
        //    [HarmonyPostfix]
        //    static void Postfix(ref int __result)
        //    {
                
        //        __result = 200000; 
        //        MelonLogger.Msg($"GetNowHomeItemPointMaxCount 被覆盖，新值: {__result}");
        //    }
        //}
        //[HarmonyPatch(typeof(HomeSystem), "GetNowHomeItemsPointCount")]
        //class Patch_GetNowHomeItemsPointCount
        //{
        //    // 使用 Postfix 修改返回值
        //    [HarmonyPostfix]
        //    static void Postfix(ref int __result)
        //    {
        //        __result = 0;
        //        MelonLogger.Msg($"GetNowHomeItemsPointCount 被覆盖，新值: {__result}");
        //    }
        //}
        //[HarmonyPatch(typeof(HomeSystem), "GetNowPlacingItemPointCount")]
        //class Patch_GetNowPlacingItemPointCount
        //{
        //    [HarmonyPostfix]
        //    static void Postfix(ref int __result)
        //    {
        //        // 强制返回 0，表示临时放置不占点数
        //        __result = 0;
        //        MelonLogger.Msg($"GetNowPlacingItemPointCount 被覆盖，新值: {__result}");
        //    }
        //}
    }
}
