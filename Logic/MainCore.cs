using Il2Cpp;
using Il2CppBehaviorDesigner.Runtime.Tasks;
using Il2CppBehaviorDesigner.Runtime.Tasks.Unity.UnityAnimator;
using Il2CppInutan;
using Il2CppKinematicCharacterController;
using Logic.管理器;
using mod;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
namespace Logic
{
    internal class MainCore
    {
        public static MainCore i = new();
        MainCore() { if (i == null) i = this; }
        public void start()
        {
            TipMsgCore.Tip_actions += tip_message;
            ExSpellPlayer.i.start();
            A_MainTips.i.start();
        }
        static int index = 0;
        string Message;
        string MsgName;
        public void update() 
        {
            ExSpellPlayer.i.update();
            A_MainTips.i.update();
            if (TipMsgCore.i.mainplayer == null)
            {
                TipMsgCore.i.mainplayer = PlayerSystem.Instance?.MainCharacterEntity?.Unit?.GetComponent<KinematicCharacterMotor>();
            }
            MsgName = "";
            Message = "";

        }
        public void OnUnload()
        {
            TipMsgCore.Tip_actions -= tip_message;
            A_MainTips.i.Unload();
        }
        public void tip_message(string name,string msg)
        {
            if (name == TipMsgCore.i.mainplayername)
            {
                mainplayermessage(msg);
            }
        }
        public void mainplayermessage(string msg)
        {
            ExSpellPlayer.i.main_message(msg);
            StringCaller.update(msg);
            BundleHelper.update(msg);

            A_MainTips.i.main_message(msg);
        }

    }
}
