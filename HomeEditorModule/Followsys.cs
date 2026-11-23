using mod;

using Il2CppInutan;
using Il2CppPb;
using mod.tool;
using System.Collections;
using UnityEngine;
using static Il2CppInutan.HomeSystem;

namespace HomeEditorModule.Core
{
    internal class Followsys
    {
        public static Followsys i = new();
        Followsys() { if (i == null) i = this; }
        Transform follow;
        public HomeItem pt;
        public void Set(Transform t)
        {
            follow = t;
        }
        float timer = 0;
        public void Updata()
        {

            if (!follow)
            {
                AutoHomeItem.Stute = Stute_AutoHomeItem.None;
                TipMsgCore.i.Send("目标丢失"); return;
            }
            if (Input.GetKey(KeyCode.Alpha4)) AutoHomeItem.Stute = Stute_AutoHomeItem.None;
            Vector2 vector2_1 = new(TipMsgCore.i.mainplayer.transform.position.x, TipMsgCore.i.mainplayer.transform.position.z);
            Vector2 vector2_2 = new(follow.position.x, follow.position.z);
            timer -= Time.deltaTime;
            if ((vector2_1 - vector2_2).magnitude > 4f)
            {
                if (IHomeSystem.sys == null) return;

                TipMsgCore.i.mainplayer.SetPosition(new(follow.position.x, EditorBridge.Y, follow.position.z));
                //todo


                

                if (timer <= 0)
                {
                    timer = 0f;
                    if (pt == null)
                    {
                        IHomeSystem.sys.CreateAndPickUp(5043347);
                        pt = IHomeSystem.sys.carryingItem;
                    }
                    else
                    {
                        if (IHomeSystem.sys.carryingItem == null)
                            IHomeSystem.sys.PickUp(pt);
                        // IHomeSystem.sys.carryingItem = pt;
                    }
                    //EditorRuntime.Start(PutDown());
                    PutDown1();
                }
            }
        }
        IEnumerator PutDown()
        {
            
            yield return new WaitForSeconds(0.00001f);
            if (pt != null)
            {
                EditorRuntime.RememberVisibility(pt.gameObject);
                pt.gameObject.SetActive(false);
                pt.SetPosRot(TipMsgCore.i.mainplayer.transform.position,0);
                //-125158
                var det = new DetectInfo() { pos = TipMsgCore.i.mainplayer.transform.position, rot = TipMsgCore.i.mainplayer.transform.localRotation, gridId = IHomeSystem.sys.detectInfo.gridId };
                IHomeSystem.sys.detectInfo = det;

                if (IHomeSystem.sys.carryingItem != null)IHomeSystem.sys.PutDownCarrying();
                pt.gameObject.transform.position = TipMsgCore.i.mainplayer.transform.position;
            }

        }
       void PutDown1()
        {

            if (pt != null)
            {
                EditorRuntime.RememberVisibility(pt.gameObject);
                pt.gameObject.SetActive(false);
                pt.SetPosRot(TipMsgCore.i.mainplayer.transform.position, 0);
                //-125158
                var det = new DetectInfo() { pos = TipMsgCore.i.mainplayer.transform.position, gridId = IHomeSystem.sys.detectInfo.gridId };
                IHomeSystem.sys.detectInfo = det;
                //pt.gameObject.transform.position = tokensys.i.mainplayer.transform.position;
                if (IHomeSystem.sys.carryingItem != null) IHomeSystem.sys.PutDownCarrying();
                
            }

        }
        //[HarmonyPatch(typeof(HomeItem), "Initialize")]
        //class HomeItem_Putdown
        //{
        //    [HarmonyPrefix]
        //    static void Prefix(long id, long playerId, ref Vector3 position, float rotationY, CarryStat stat = CarryStat.None)
        //    {
        //        position = tokensys.i.mainplayer.transform.position;
        //    }


        //}
    }
}
