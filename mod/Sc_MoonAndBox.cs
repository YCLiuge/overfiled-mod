using Il2CppBehaviorDesigner.Runtime.Tasks.Unity.UnityAnimator;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static Il2Cpp.Layers;

namespace mod
{
    internal class Sc_MoonAndBox
    {
        public static Sc_MoonAndBox i = new();
        Sc_MoonAndBox() { if (i == null) i = this; }
        //-------------------------------------------月亮(Clone)/---------------------------------------小月亮z
        //CollectionSmallMoonController.OnCollected()
        //-------------------------------------------月亮(Clone)/-------------------------------------固定月亮z
        //CollectionBigMoonController.OnCollected()
        //动作月亮_001 | SmallMoon 11103001/已收集
        //-------------------------------------------宝箱(Clone)/z
        //CollectionTreasureController.OnCollected(Unit)
        //UIPickupSystem.OnPickupAll(Object)
        //BagsSystem.BattleBagMoveToBag()
        public void updata(string name,string msg)
        {
            if (name != TipMsgCore.i.mainplayername) msg = "";
        }
    }
}
