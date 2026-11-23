using DeepSeekClientMod;
using Il2Cpp;
using Il2CppInutan;
using Il2CppKinematicCharacterController;
using MelonLoader;
using mod.st;
using ModLoader;
using System.Collections;
using UnityEngine;
using static Il2CppInutan.HomeSystem;
using NetChater = mod.st.NetChater;
namespace mod.tool
{
    internal class A_maintool
    {
        static bool ff = false;//
        static bool rr = false;//
        static Quaternion playerquaternion;
        static float ff_speed = 0.5f;
        public static float Y = 0;//player高度控制
        public static bool useReturn = true;
        public static void updata()
        {
            if(Input.anyKey) keydawncommod();
            if (ff)
            {
                if (Input.GetKeyDown(KeyCode.Escape)) ff = false;
                Y += ff_speed * Time.deltaTime;
                var a = TipMsgCore.i.mainplayer.transform;
                TipMsgCore.i.mainplayer.SetPosition(new(a.position.x, Y, a.position.z));
            }
            if (rr) 
            {
                if (Input.GetKeyDown(KeyCode.Escape)) rr = false;
                TipMsgCore.i.mainplayer.SetRotation(playerquaternion);
                
            }
            if(useReturn)TipMsgCore.i.mainplayer.gameObject.GetComponent<Unit>().UnitEntitys.MoveEntity.StandGroundPosition = TipMsgCore.i.mainplayer.transform.position;
            DroneFlyController.Instance.OnUpdate();
            OceanCreator.Instance.OnUpdate();
            BoatShapeController.Instance.OnUpdate();
        }
        public static List<Transform> FindAllDeep(Transform parent, string name)
        {
            var result = new List<Transform>();
            if (parent == null) return result;

            if (parent.name == name) result.Add(parent);

            for (int i = 0; i < parent.childCount; i++)
                result.AddRange(FindAllDeep(parent.GetChild(i), name));

            return result;
        }
   
        public static void updata(string message)
        {
            string[] msg = message.Split(' ');
            switch (msg[0])
            {

                case "dcmx":
                    if (msg.Length == 1)
                    {
                        TipMsgCore.i.mainplayer.SetPosition(new());
                        TipMsgCore.i.mainplayer.SetRotation(new());
                        ModelExporter.ExportToOBJ(TipMsgCore.i.mainplayer.gameObject.transform.GetChild(0).gameObject, "mx", ConstMsg.ModelExporter_path, 0, true);
                    }
                    if (msg.Length == 2)
                    {
                        if (TipMsgCore.i.players.TryGetValue(msg[1], out var player))
                        {
                            player.root.GetComponent<KinematicCharacterMotor>().SetPosition(new());
                            player.root.GetComponent<KinematicCharacterMotor>().SetRotation(new());
                            ModelExporter.ExportToOBJ(TipMsgCore.i.mainplayer.gameObject.transform.GetChild(0).gameObject, "mx_" + msg[1], ConstMsg.ModelExporter_path, 0, true);
                        }
                        else { TipMsgCore.i.ERR(); }
                    }
                    break;
                case "tp":
                    if (msg.Length == 4)
                    {
                        if (float.TryParse(msg[1], out float x) && float.TryParse(msg[2], out float y) && float.TryParse(msg[3], out float z))
                        {
                            Y = y;
                            TipMsgCore.i.mainplayer.SetPosition(new Vector3(x, y, z));
                        }
                        else TipMsgCore.i.ERR();

                    }
                    else if (msg.Length == 2)
                    {
                        if (int.TryParse(msg[1], out var b))
                        {
                            if (b >= 0 && b < TipMsgCore.i.players.Count)
                            {
                                int i = 0;
                                foreach (var c in TipMsgCore.i.players.Values)
                                {

                                    if (i == b)
                                    {
                                        TipMsgCore.i.mainplayer.SetPosition(c.root.position);
                                        break;
                                    }
                                    else
                                    {
                                        i++;
                                    }
                                }
                            }
                        }
                        else if (TipMsgCore.i.players.TryGetValue(msg[1], out var a))
                        {
                            TipMsgCore.i.mainplayer.SetPosition(a.root.position);
                        }
                        else TipMsgCore.i.ERR();

                    }
                    else TipMsgCore.i.ERR();
                    break;
                case "f":
                    if (msg.Length == 1)
                    {
                        ff = !ff;
                        Y = TipMsgCore.i.mainplayer.transform.position.y;
                    }
                    if (msg.Length == 2)
                    {
                        ff = float.TryParse(msg[1], out float a);
                        if (ff) { ff_speed = a; }
                        Y = TipMsgCore.i.mainplayer.transform.position.y;
                    }
                    break;
                case "r":
                    if (rr) rr = false;
                    if (msg.Length == 4)
                    {
                        if (float.TryParse(msg[1], out float x) && float.TryParse(msg[2], out float y) && float.TryParse(msg[3], out float z))
                        {
                            playerquaternion = Quaternion.Euler(x, y, z);
                            rr = true;
                        }
                    }
                    break;
                case "ai":
                    if (msg.Length == 2) { }
                    else if (msg.Length == 3)
                    {
                        switch (msg[1])
                        {
                            case "set":
                                DeepSeekClient.Instance.ClearHistory();
                                DeepSeekClient.Instance.SetSystemMessage(msg[2]);
                                break;

                        }
                    }
                    else if (msg.Length == 4) { }

                    break;
                case "put":
                    if (msg.Length == 5)
                    {
                        float[] posrot = new float[4];
                        for (int i = 0; i < 4; i++)
                        {
                            if (float.TryParse(msg[i + 1], out float a))
                            {
                                posrot[i] = a;
                            }
                        }
                        DetectInfo det;
                        det = new DetectInfo() { pos = new(posrot[0], posrot[1], posrot[2]), rot = Quaternion.Euler(0, posrot[3], 0), gridId = IHomeSystem.sys.detectInfo.gridId };
                        IHomeSystem.sys.detectInfo = det;
                        if (IHomeSystem.sys.carryingItem != null) IHomeSystem.sys.PutDownCarrying();
                    }
                    break;
                case "m":
                    m = true;

                    break;
                case "gp":
                    if (msg.Length == 2)
                    {
                        if (msg[1] == "cl_list")
                        {
                            foreach (var i in IHomeSystem.sys.allFurnitures.Values)
                            {
                                tempitems.Add(i);
                            }
                            IHomeSystem.sys.allFurnitures.Clear();
                            TipMsgCore.i.OK();
                        }
                        if (msg[1] == "cl_all")
                        {
                            for (var i = 0; i < tempitems.Count; i++)
                            {

                                if (tempitems[i] != null) MonoBehaviour.Destroy(tempitems[i].gameObject);
                            }
                            tempitems.Clear();
                            TipMsgCore.i.OK();
                        }
                        if (msg[1] == "reput")
                        {
                            MelonCoroutines.Start(reput());
                        }
                        if (msg[1] == "stop")
                        {
                            gpstop = true;
                        }
                    }
                    break;
                case "look":
                    if (msg.Length == 2)
                    {
                        if (int.TryParse(msg[1], out int a))
                        {
                            if (a == 1)
                            {
                                CameraSystem.Instance.SetCameraFixOrFlolow(eCameraMode.FirstCamera);
                                CameraSystem.Instance._FollowMoveSmooth = 30;
                                CameraSystem.Instance._MainCameraRootController.MainCameraCharacterFader.enabled = false;
                            }
                            else
                            {
                                CameraSystem.Instance._FollowMoveSmooth = 5;
                                CameraSystem.Instance._MainCameraRootController.MainCameraCharacterFader.enabled = true;
                            }
                            if (a == 2)
                            {
                                CameraSystem.Instance.SetCameraFixOrFlolow(eCameraMode.Cross);
                            }
                            if (a == 3) CameraSystem.Instance.SetCameraFixOrFlolow(eCameraMode.Fixed);
                        }
                    }
                    if (msg.Length == 3)
                    {
                        if (msg[1] == "fov")
                        {
                            if (float.TryParse(msg[2], out float b))
                            {
                                CameraSystem.Instance.SetFOV(b);
                            }
                        }
                    }
                    break;
                case "rt":
                    if (msg.Length == 2)
                    {
                        if (int.TryParse(msg[1], out var rt))
                        {
                            useReturn = rt == 1 ? true : false;
                        }
                    }
                    break;
                case "chat":
                    if (msg.Length == 2)
                    {
                        if (msg[1][0] == '!')
                        {
                            string numberPart = msg[1].TrimStart('!');
                            if (int.TryParse(numberPart, out int v))
                            {
                                TipMsgCore.i.sendchat("", v, eChatChannelType.Current);
                            }
                        }
                        else
                        {
                            TipMsgCore.i.sendchat(msg[1], 0, eChatChannelType.Current);
                        }
                    }
                    if (msg.Length == 3)
                    {
                        if (msg[1][0] == '!')
                        {
                            string numberPart = msg[1].TrimStart('!');
                            if (int.TryParse(numberPart, out int v))
                            {
                                if (int.TryParse(msg[2], out int e))
                                {
                                    TipMsgCore.i.sendchat("", v, (eChatChannelType)e);
                                }
                            }
                        }
                        else
                        {
                            if (int.TryParse(msg[2], out int e))
                            {
                                TipMsgCore.i.sendchat(msg[1], 0, (eChatChannelType)e);
                            }
                        }
                    }
                    break;
                case "by":
                    if (msg.Length == 1)
                    {
                        if (JellyScaleAnimator.Instance.GetCount() != TipMsgCore.i.players.Count)
                        {
                            JellyScaleAnimator.Instance.ClearTargets();
                            JellyScaleAnimator.Instance.AddTarget(TipMsgCore.i.mainplayer.transform);

                            foreach (var p in TipMsgCore.i.players.Values)
                            {
                                var b = p.root;
                                if (b == null) continue;
                                if (b.name == "[Player]")
                                {
                                    if (!JellyScaleAnimator.Instance.HasTarget(b))JellyScaleAnimator.Instance.AddTarget(b);
                                }
                                else
                                {
                                    var cs = FindAllDeep(b,"[Player]");
                                    foreach(var c in cs)
                                    {
                                        if(!JellyScaleAnimator.Instance.HasTarget(c)) JellyScaleAnimator.Instance.AddTarget(c);
                                    }
                                    
                                }
                            }
                        }
                        if (!JellyScaleAnimator.Instance.IsPlaying)
                        {
                            JellyScaleAnimator.Instance.PlayLoop();
                        }
                        else
                        {
                            JellyScaleAnimator.Instance.Stop();
                        }
                    }
                    break;
                case "wave":
                    var _ocean=OceanCreator.Instance;
                    if (_ocean.IsActive) _ocean.DestroyOcean();
                    else
                    {
                        var player = TipMsgCore.i.mainplayer.transform;
                        Vector3 pos = player != null ? player.position : Vector3.zero;
                        _ocean.CreateOcean(pos);
                    }
                    break;
                case "boat":
                    if (msg.Length == 2 && msg[1]=="init")
                    {
                        AutoHomeItem.i.Copyitems(Vector3.zero);
                    }
                    if (msg.Length == 1)
                    {
                        if (BoatShapeController.boat == null)
                        {
                            BoatShapeController.boat = new GameObject("Boat");
                            var b = BoatShapeController.boat;
                            b.transform.rotation = TipMsgCore.i.mainplayer.transform.rotation;
                            var items = AutoHomeItem.i.items;
                            int i = 0;
                            foreach (var item in items)
                            {
                                var info = new Il2CppPb.FurnitureDetailsInfo()
                                {
                                    FurnitureItemId = (uint)item.id,
                                    FurnitureId = i++,
                                    Pos = new Il2CppPb.Vector3(),
                                    Rotation = new Il2CppPb.Vector3()
                                };

                                IHomeSystem.sys.CreateAndPutDown(info, 0);
                            }
                            var center = AutoHomeItem.GetCenterAverage(items);
                            b.transform.position = center;
                            List<HomeItem_Furniture> longs = new();
                            foreach (var item in IHomeSystem.sys.allFurnitures)
                            {
                                if (item.Value.playerId == 0)
                                {
                                    item.Value.SetPosRot(items[(int)item.Value.id].Position, (items[(int)item.Value.id].Rotation).eulerAngles.y);
                                    item.Value.transform.SetParent(b.transform, true);
                                    tempitems.Add(item.Value);
                                    longs.Add(item.Value);
                                }
                            }
                            foreach (var item in longs)
                            {
                                IHomeSystem.sys.UnRegisterFurniture(item);
                            }
                            
                            BoatShapeController.Instance.Activate(b.transform);

                        }
                        else
                        {
                            BoatShapeController.Instance.Deactivate();
                            NetPlayer.DestroySmoothly(BoatShapeController.boat);
                            BoatShapeController.boat = null;
                        }
                    }
                        break;
                case "net":
                    NetChater.Instance.Command(msg.Skip(1).Where(x => !string.IsNullOrWhiteSpace(x)).ToArray());
                    break;
                case "#":
                    NetChater.Instance.Send(message.TrimStart('#'));
                    break;
                case"update":
                    _ = LogicUpdater.Instance.CheckAndUpdateAllAsync();
                    TipMsgCore.i.OK();
                    break;
            }

        }
        static List<HomeItem> tempitems = new();
        static bool gpstop = false;
        static IEnumerator reput()
        {
            if (IHomeSystem.sys == null)
            {
                MelonLogger.Error("IHomeSystem.sys 为 null");
                yield break;
            }

            if (tempitems == null || tempitems.Count == 0)
            {
                MelonLogger.Warning("tempitems 为空");
                TipMsgCore.i.OK();
                yield break;
            }

            int success = 0;
            int skipped = 0;

            foreach (var item in tempitems)
            {
                if (gpstop)
                {
                    gpstop = false;
                    TipMsgCore.i.OK();
                    yield break;
                }
                if (item == null || item.transform == null || item.gameObject == null)
                {
                    MelonLogger.Warning("跳过已销毁的家具");
                    skipped++;
                    continue;
                }

                // 保存位置和旋转
                Vector3 pos = item.transform.position;
                Quaternion rot = item.transform.rotation;
                long itemId = item.id;

                // 使用固定等待，不依赖字典 Count
                yield return new WaitForSeconds(0.6f);

                // 重新判空
                if (IHomeSystem.sys == null || item == null || item.gameObject == null)
                    break;

                // 拾取
                IHomeSystem.sys.PickUp(item);
                yield return new WaitForSeconds(0.6f);

                // 重新判空
                if (IHomeSystem.sys == null)
                    yield break;

                // 设置检测信息
                DetectInfo det = new DetectInfo()
                {
                    pos = pos,
                    rot = rot,
                    gridId = IHomeSystem.sys.detectInfo.gridId
                };
                IHomeSystem.sys.detectInfo = det;

                // 放置
                if (IHomeSystem.sys.carryingItem != null)
                    IHomeSystem.sys.PutDownCarrying();

                yield return new WaitForSeconds(0.6f);
                stc.Log($"{itemId} {pos.x} {pos.z}");
                success++;
            }

            MelonLogger.Msg($"reput 完成：成功 {success}，跳过 {skipped}");
            TipMsgCore.i.OK();
        }
        static bool m = false;
        public static HomeItem itemtemp;
        static void keydawncommod()
        {
            if (m && itemtemp != null)
            {
                //if (CameraSystem.Instance.FollowTarget == tokensys.i.mainplayer.transform)
                //{ CameraSystem.Instance.SetCameraFollowUnit(itemtemp.gameObject); }

                float moveSpeed = 8f;
                float verticalSpeed = 5f;
                Transform tr = itemtemp.transform;

                // 获取主相机
                Camera cam = Camera.main;
                if (cam == null)
                {
                    // 如果没有主相机，则使用世界坐标轴（备选）
                    // 这里直接返回或使用物体自身方向，但按需求此处不处理
                    return;
                }

                // 输入
                float horizontal = Input.GetAxis("Horizontal"); // A / D
                float vertical = Input.GetAxis("Vertical");     // W / S
                float up = 0f;
                if (Input.GetKey(KeyCode.LeftControl)) up = -1f;
                if (Input.GetKey(KeyCode.LeftShift)) up = 1f;
                if (Input.GetKeyDown(KeyCode.Escape))
                {
                    m = false;
                    CameraSystem.Instance.SetCameraFollowUnit(TipMsgCore.i.mainplayer.gameObject);
                }
                if (Input.GetKeyDown(KeyCode.Tab))
                {
                    MelonCoroutines.Start(HomeTool.PickUpAndPutDown(itemtemp, itemtemp.transform.position,cam.transform.eulerAngles.y));
                }
                if (Input.GetKeyDown(KeyCode.CapsLock))
                {
                    if (CameraSystem.Instance.FollowTarget == TipMsgCore.i.mainplayer.transform)
                    {
                        CameraSystem.Instance.SetCameraFollowUnit(itemtemp.gameObject);
                    }
                    else
                    {
                        CameraSystem.Instance.SetCameraFollowUnit(TipMsgCore.i.mainplayer.gameObject);
                    }
                }
                // 计算移动方向（基于相机方向）
                Vector3 forward = cam.transform.forward;
                Vector3 right = cam.transform.right;
                // 在XZ平面上投影，保持移动水平时不影响Y轴
                forward.y = 0;
                right.y = 0;
                forward.Normalize();
                right.Normalize();

                Vector3 moveDirection = forward * vertical + right * horizontal + Vector3.up * up;
                moveDirection.Normalize();

                // 分解速度
                Vector3 horizontalMove = new Vector3(moveDirection.x, 0, moveDirection.z) * moveSpeed;
                Vector3 verticalMove = Vector3.up * moveDirection.y * verticalSpeed;

                Vector3 totalMove = (horizontalMove + verticalMove) * Time.deltaTime;

                // 移动 Transform
                tr.position += totalMove;
                tr.eulerAngles = new(0, cam.transform.eulerAngles.y, 0);
            }
        }
    }
}
