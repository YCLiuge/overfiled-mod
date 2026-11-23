using Il2Cpp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace mod
{
    class GamobjectCheker
    {
        public static GamobjectCheker i = new();
        GamobjectCheker() { if (i == null) i = this; }

        public List<GameObject> obs = new();
        string[] strs =
        {
                //"[UIMainRoot]/UI_Root_Home(Clone)",
                //"[UIMainRoot]/UI_Root_Main(Clone)/Canvas/Rect/右上模块/StateRoot/功能列表/家具按钮/按钮",
                //"[UIMainRoot]/UI_Root_Home(Clone)/Canvas/StateRoot/Rect/家具列表",
                //"[UIMainRoot]/UI_Root_Chat(Clone)/Canvas/Rect/Offset/底框/Canvas/Scroll View/Viewport/Content",
                //"[UIMainRoot]/UI_Root_Chat(Clone)/Canvas/Rect/Offset/底框/Canvas/Scroll View/Viewport/Content/Item(Clone)/聊天-左/玩家名字",
                //"[UIMainRoot]/UI_Root_Chat(Clone)/Canvas/Rect/Offset/底框/Canvas/Scroll View/Viewport/Content/Item(Clone)/聊天-左/文字范围/文字内容",
                //"[UIMainRoot]/UI_Root_Title(Clone)"
                "[UIMainRoot]/UI_Root_Title(Clone)/Canvas/StateRoot/TitleParent/Root"
        };
        public void Init()
        {
            obs.Clear();
            GameObject temp;

            foreach (string str in strs)
            {
                temp = GameObject.Find(str);
                if (temp != null)
                {
                    obs.Add(temp);
                }
                else
                {
                    stc.Log("未找到对象：" + str);
                }
            }
        }


    }
}
