using Il2Cpp;
using Il2CppGoogle.Protobuf.WellKnownTypes;
using Il2CppInutan;
using Il2CppKinematicCharacterController;
using Logic.管理器;
using MelonLoader;
using mod;
using ModLoader;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;
using static Il2Cpp.IExcelSystem;

namespace Logic
{
    internal class A_MainTips
    {
        public static A_MainTips i = new();
        A_MainTips() { if (i == null) i = this; }
        public void start()
        {
            AIControlServer.Start(8080);
            //CoordinateDisplaySystem.Init();
        }
        public void update()
        {
            // 驱动单例的更新
            ImageViewerManager.Tick();
            WorldImageViewer.Tick();
            ColockFiander.Instance.tp();
            AotuFishing.update();
            CameraOrbitSystem.Tick();
            CameraOrbitSystem.LateTick();
            if (PortalSystem.IsActive) PortalSystem.Tick();
            //// 按 F11 开启/关闭传送门系统
            //if (Input.GetKeyDown(KeyCode.F11))
            //{
            //    if (!PortalSystem.IsActive) PortalSystem.Init();
            //    else PortalSystem.Stop();
            //}
            // 驱动碰撞箱更新
            ColliderDebugger.Tick();
            bool a = Input.GetKey(KeyCode.LeftAlt);
            if (Input.GetKeyDown(KeyCode.Alpha9) && a)
            {

                if (!CommandPanelSystem.IsActive) CommandPanelSystem.StartSystem();
                else CommandPanelSystem.StopSystem();
            }
            CommandPanelSystem.Tick();
            CoordinateDisplaySystem.Tick();
        }
        public void Unload()
        {
            AIControlServer.Stop();
            try { CameraOrbitSystem.Shutdown(); }
            finally { CoordinateDisplaySystem.Destroy(); }
        }
        public void main_message(string message)
        {
            var msg = message.Split(' ');
            switch (msg[0])
            {
                case "up":
                    _ = LogicUpdater.Instance.CheckAndUpdateAllAsync();
                    break;
                case "pic":
                    if (msg.Length == 2)
                    {
                        ImageViewerManager.ShowImageFromFile("mod储存数据/" + msg[1]);
                    }
                    break;
                case "picw":
                    if (msg.Length == 2)
                        WorldImageViewer.SpawnImageFromFile("mod储存数据/" + msg[1]);
                    break;
                case "get":
                    if (msg.Length == 2)
                    {
                        if (msg[1] == "cjw") MelonCoroutines.Start(ColockFiander.Instance.GetCJW());
                        if (msg[1] == "cjwx") MelonCoroutines.Start(ColockFiander.Instance.GetCJWx());
                        if (msg[1] == "bx") MelonCoroutines.Start(ColockFiander.Instance.GetBX());
                        if (msg[1] == "moon") MelonCoroutines.Start(ColockFiander.Instance.GetMoon());
                    }
                    break;
                case "sm":
                    MelonCoroutines.Start(ColockFiander.Instance.Sm());
                    break;
                case "fish":
                    AotuFishing.startFishing();
                    break;
                case "mj":
                    CameraOrbitSystem.ExecuteCommand(message);
                    break;
                case "tb":
                    if (msg.Length == 2)
                    {

                        if (int.TryParse(msg[1], out int id))
                        {
                            string path = UnitEmotionSystem.Instance.GetEmotionActionPath(id);
                            if (path == "Assets/ResBundle/.prefab")
                            {
                                stc.Log($"警告：ID {id} 动作路径无效");
                                break;
                            }
                            foreach (var playe in TipMsgCore.i.players)
                            {
                                var g = playe.Value.root.GetComponent<Unit>();
                                if (g == null) continue;
                                UnitEmotionSystem.Instance.PlayEmotionAction(g, id, false);

                            }
                        }
                    }
                    break;
                case "tbs":
                    if (msg.Length == 3)
                    {
                        if (int.TryParse(msg[1], out int id))
                        {
                            string path = UnitEmotionSystem.Instance.GetEmotionActionPath(id);
                            if (path == "Assets/ResBundle/.prefab")
                            {
                                stc.Log($"警告：ID {id} 动作路径无效");
                                break;
                            }
                            if (msg[2] == "t") UnitEmotionSystem.Instance.PlayEmotionAction(TipMsgCore.i.mainplayer.GetComponent<Unit>(), id, true);
                            else UnitEmotionSystem.Instance.PlayEmotionAction(TipMsgCore.i.mainplayer.GetComponent<Unit>(), id, false);
                        }

                    }
                    break;
                case "api":
                    AIControlServer.Start(8080);
                    break;
                case "box":

                    ColliderDebugger.IsActive = !ColliderDebugger.IsActive;
                    MelonLogger.Msg($"[Debug] 碰撞箱显示: {(ColliderDebugger.IsActive ? "开启" : "关闭")}");
                    if (!ColliderDebugger.IsActive) ColliderDebugger.ClearAll();

                    ColliderDebugger.UseRaycastMode = !ColliderDebugger.UseRaycastMode;
                    MelonLogger.Msg($"[Debug] 准星探测模式: {(ColliderDebugger.UseRaycastMode ? "开启" : "关闭")}");


                    break;
                case "light":
                    if (Obj.Get_light() != null) Obj.Get_light().SetActive(!Obj.Get_light().activeSelf);
                    break;
                case "ttlf":
                    if (msg.Length == 1)
                    {

                        if (Obj.Get_ttl() != null) Obj.Get_ttl().SetActive(!Obj.Get_ttl().activeSelf);
                    }
                    if (msg.Length == 2)
                    {
                        if (Obj.Get_ttl_main() != null)
                        {
                            var b = Obj.Get_ttl_main().transform;
                            foreach (var c in msg[1])
                            {
                                int index = c - '0';
                                if (index < b.childCount && index >= 0 && index < 6)
                                {
                                    b.GetChild(index).gameObject.SetActive(!b.GetChild(index).gameObject.activeSelf);
                                }
                            }
                        }
                    }
                    break;
                case "ttlm":
                    Obj.Get_ttl_bsPositions();
                    if (msg.Length == 1) 
                    {
                        for (int i = 0; i < Obj.Get_ttl_main().transform.childCount; i++) 
                        {
                            Obj.Get_ttl_main().transform.GetChild(i).position = Obj.Get_ttl_bsPositions()[i];
                        }
                    }
                    if (msg.Length == 2)
                    {
                        if (Obj.Get_ttl_main() != null)
                        {
                            var b = Obj.Get_ttl_main().transform;
                            foreach (var c in msg[1])
                            {
                                if (!char.IsDigit(c)) continue; // 过滤非数字

                                int index = c - '0';
                                // 移除 index < 6 的限制，依靠 childCount 挡住越界
                                if (index < b.childCount && index >= 0)
                                {
                                    var m = b.GetChild(index);

                                    // 用 CanvasGroup 代替坐标位移，这是正规做法
                                    var cg = m.GetComponent<CanvasGroup>();
                                    if (cg == null) cg = m.gameObject.AddComponent<CanvasGroup>();

                                    // 直接切换透明度，不管交互
                                    cg.alpha = cg.alpha > 0 ? 0 : 1;
                                }
                            }
                        }
                    }
                    if (msg.Length == 4) 
                    {
                        if (int.TryParse(msg[1],out var n1)&& int.TryParse(msg[2], out var n2)&& int.TryParse(msg[3], out var n3))
                        {
                            if (n1 < Obj.Get_ttl_main().transform.childCount && n1 >= 0)
                            {
                                Obj.Get_ttl_main().transform.GetChild(n1).position = new Vector3(n1, n2);
                            }
                        }
                    }
                    break;
                // paint / dg are owned by the independently reloadable DrawGuessModule.
            }
        }

    }
}
