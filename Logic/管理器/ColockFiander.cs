using Il2Cpp;
using Il2CppInutan;
using mod;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Il2CppProtocol;
using UnityEngine.EventSystems;

namespace Logic.管理器
{
    //-------------------------------------------采集(Clone)/采集组/
    internal class ColockFiander
    {
        public static ColockFiander Instance = new ColockFiander();
        //public 
        public IEnumerator GetCJW()
        {
            GameObject root = GameObject.Find("-------------------------------------------采集(Clone)/采集组/随机采集");

            if (root != null)
            {

                for (int i = 0; i < root.transform.childCount; i++)
                {
                    var child = root.transform.GetChild(i);
                    List<Transform> list = new List<Transform>();
                    for (int temp = 0; temp < child.transform.childCount; temp++)
                    {
                        list.Add(child.transform.GetChild(temp));
                    }
                    for (int j = 0; j < list.Count; j++)
                    {
                        var bb = list[j].GetComponent<GatherItemBase>();
                        bb?.OnPlayerClick();
                        yield return new WaitForSeconds(0.3f);
                        bb?.OnCollect(TipMsgCore.i.mainplayer.GetComponent<Unit>());
                    }
                }
            }
        }
        public IEnumerator GetCJWx()
        {
            var root = GameObject.Find("-------------------------------------------采集(Clone)");
            if (root != null)
            {
                for (int i = 0; i < root.transform.childCount; i++)
                {
                    if (root.transform.GetChild(i).gameObject.activeSelf)
                    {
                        var a = root.transform.GetChild(i);
                        var b = a.Find("固定采集");
                        if (b != null)
                        {
                            for (int z = 0; z < b.transform.childCount; z++)
                            {
                                var child = b.transform.GetChild(z);
                                List<Transform> list = new List<Transform>();
                                for (int temp = 0; temp < child.transform.childCount; temp++)
                                {
                                    list.Add(child.transform.GetChild(temp));
                                }
                                for (int j = 0; j < list.Count; j++)
                                {
                                    var bb = list[j].GetComponent<GatherItemBase>();
                                    bb?.OnPlayerClick();
                                    yield return new WaitForSeconds(0.3f);
                                    bb?.OnCollect(TipMsgCore.i.mainplayer.GetComponent<Unit>());
                                }
                            }
                        }
                    }
                }
            }
        }
        public IEnumerator GetBX()
        {
            GameObject root = GameObject.Find("-------------------------------------------宝箱(Clone)");
            if (root == null) yield break;
            for (int i = 0; i < root.transform.childCount; i++)
            {
                var a = root.transform.GetChild(i);

                if (true)
                {
                    var c = a.GetComponent<CollectionTreasureController>();
                    c?.OnCollected(TipMsgCore.i.mainplayer.GetComponent<Unit>());
                    yield return new WaitForSeconds(0.3f);
                    UIPickupSystem.Instance?.OnPickupAll(null);
                    yield return new WaitForSeconds(0.1f);
                    BagsSystem.Instance.BattleBagMoveToBag();
                }
            }
        }
        public IEnumerator GetMoon()
        {
            GameObject root = GameObject.Find("-------------------------------------------月亮(Clone)/---------------------------------------小月亮");
            if (root == null) yield break;
            for (int i = 0; i < root.transform.childCount; i++)
            {
                TipMsgCore.i.mainplayer.SetPosition(root.transform.GetChild(i).position);
                yield return new WaitForSeconds(0.1f);
            }
        }
        //[UIMainRoot]/UI_Root_PlayerMenu(Clone)/Canvas/Rect/玩家界面/内容/玩家名片/菜单按钮/传送身边/按钮
        public void tp()
        {
            if (Input.GetKeyDown(KeyCode.P))
            {
                var p = GameObject.Find("[UIMainRoot]/UI_Root_PlayerMenu(Clone)/Canvas/Rect/玩家界面/内容/玩家名片/菜单按钮/传送身边/按钮");
                if (p == null) return;
                p.GetComponent<UGUI_ButtonEvent>()?.OnPointerClick(null);
            }
        }
        public IEnumerator Sm()
        {
            //foreach (var a in UIChatSystem.Instance._Emotions) {

            //    UIChatSystem.Instance.SendMessageData("", a.EmotionConfig.ID,eChatChannelType.Channel);
            //    if (Input.GetKeyDown(KeyCode.P)) yield break;
                yield return new WaitForSeconds(1f); 
            //}
        }
    }
}
