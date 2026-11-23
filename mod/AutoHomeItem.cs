using Il2Cpp;
using Il2CppInutan;
using MelonLoader;
using mod.tool;
using mod.st;
using System.Collections;
using UnityEngine;
using static Il2CppInutan.HomeSystem;
namespace mod
{
    public enum Stute_AutoHomeItem
    {
        None,
        Pasting,
        fling,
        Fking,
    }
    public class AutoHomeItem
    {
        public static AutoHomeItem i = new();
        public static Stute_AutoHomeItem Stute;
        AutoHomeItem() { if (i == null) i = this; }

        public List<Itempot> items = new();
        // Shared boat clipboard only; editing runtime lives in HomeEditorModule.

         public void Copyitems(Vector3 fixedCorner,bool part = false)
        {
            items.Clear();
            foreach (var item in IHomeSystem.sys.allFurnitures)
            {

                if (part) throw new NotSupportedException("矩形复制请使用 HomeEditorModule 的 -cc");
                items.Add(new(item.value.gameObject));
            }
            TipMsgCore.i.Send("已复制:" + items.Count);
        }
        public static Vector3 GetCenterAverage(List<Itempot> objects)
        {
            if (objects == null || objects.Count == 0) return Vector3.zero;

            Vector3 sum = Vector3.zero;
            int count = 0;
            foreach (var t in objects)
            {
                if (t == null) continue;
                sum += t.Position;
                count++;
            }
            return count > 0 ? sum / count : Vector3.zero;
        }
        public static Vector3 GetCenterAverage(List<Net_BoatData> objects)
        {
            if (objects == null || objects.Count == 0) return Vector3.zero;

            Vector3 sum = Vector3.zero;
            int count = 0;
            foreach (var t in objects)
            {
                if (t == null) continue;
                sum += t.pos;
                count++;
            }
            return count > 0 ? sum / count : Vector3.zero;
        }


    }
    public class Itempot
    {
       
        public Vector3 Position;
        public Quaternion Rotation;
        public int id;
        public string Name;
        public Itempot() { }
        public Itempot(Vector3 position, Quaternion rotation, int id, string name)
        {
            Position = position;
            Rotation = rotation;
            this.id = id;
            Name = name;
        }
        public Itempot(GameObject gameObject)
        {
           
            Position = gameObject.transform.position;
            Rotation = gameObject.transform.rotation;
            id = gameObject.GetComponent<HomeItem_Furniture>().tableId;
            Name = IExcelSystem.Item.GetItemString(id).text_[0];
        }
        public Itempot(GameObject gameObject,Vector3 p)
        {
           
            Position = gameObject.transform.position - p;
            Rotation = gameObject.transform.rotation;
            id = gameObject.GetComponent<HomeItem_Furniture>().tableId;
            Name = IExcelSystem.Item.GetItemString(id).text_[0];
        }
    }
}
