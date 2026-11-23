using Il2CppInutan;
using UnityEngine;
using static Il2CppInutan.HomeSystem;
using System.Collections;
namespace mod.tool
{
    internal class HomeTool
    {

        public static void Confirm移除全部装饰(bool agree)
        {
            var uiHome = UIHomeSystem.Instance;
            if (uiHome == null) return;
            if (!(IHomeSystem.sys.ownGarden && IHomeSystem.sys.isGarden)) return;
            uiHome.Method_Private_Void_Boolean_PDM_0(true);

        }
        public static void Confirm应用方案(bool agree,int num=0)
        {
            if (num > 1) return;
            var uiHome = UIHomeSystem.Instance;
            if (uiHome == null) return;
            if (uiHome.schemeList.Count == 0) return;
            if (!(IHomeSystem.sys.ownGarden && IHomeSystem.sys.isGarden)) return;
            uiHome.OnClickApplySchemeItem(num);
            uiHome.Method_Private_Void_Boolean_PDM_1(true);

        }
        public static void Confirm保存方案(bool agree,int num=0)
        {
            if (num > 1) return;
            var uiHome = UIHomeSystem.Instance;
            if (uiHome == null) return;
            if (uiHome.schemeList.Count == 0) return;
            if (!(IHomeSystem.sys.ownGarden && IHomeSystem.sys.isGarden)) return;
            uiHome.OnClickSaveSchemeItem(num);
            uiHome.Method_Private_Void_Boolean_PDM_2(true);
           
        }
        public static IEnumerator PickUpAndPutDown(HomeItem item, Vector3 Position, float ry = 0)
        {
            if (IHomeSystem.sys == null || item == null) yield break;
            IHomeSystem.sys.PickUp(item);
            yield return new WaitForSeconds(0.8f);
            var det = new DetectInfo() { pos = Position, rot = Quaternion.Euler(0, ry, 0), gridId = IHomeSystem.sys.detectInfo.gridId };
            IHomeSystem.sys.detectInfo = det;
            IHomeSystem.sys.PutDownCarrying();
        }
    }
}
