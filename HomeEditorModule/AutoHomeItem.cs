using mod;
using Il2Cpp;
using Il2CppInutan;
using MelonLoader;
using mod.tool;
using mod.st;
using System.Collections;
using UnityEngine;
using static Il2CppInutan.HomeSystem;
namespace HomeEditorModule.Core
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

        public List<Itempot> items { get => mod.AutoHomeItem.i.items; set => mod.AutoHomeItem.i.items = value; }

        string path = "mod储存数据/";//保存路径
        public GameObject fk; // 辅助框物体
        private LineRenderer _lineRenderer;
        private Vector3 _fixedCorner;//成员变量存储固定角
        private Vector3 _movingCorner;//成员变量存储移动角
        private bool _isDrawing;//辅助框状态
        private object _drawCoroutine;
         public void Copyitems(Vector3 fixedCorner,bool part = false)
        {
            items.Clear();
            foreach (var item in IHomeSystem.sys.allFurnitures)
            {

                if (part)
                {
                    if (IsPointInRect(item.value.gameObject.transform.position))
                    {
                        items.Add(new(item.value.gameObject, fixedCorner));
                    }
                }
                else
                {
                    items.Add(new(item.value.gameObject));
                }
            }
            TipMsgCore.i.Send("已复制:" + items.Count);
        }
        public void Copyitems_part()
        {
            if (fk == null)
            {
                fk = new GameObject("辅助框");
                // 添加 LineRenderer（原生组件，Il2Cpp 支持）
                _lineRenderer = fk.AddComponent<LineRenderer>();
                // 配置 LineRenderer
                var material = new Material(Shader.Find("Sprites/Default"));
                EditorRuntime.Own(material); _lineRenderer.sharedMaterial = material;
                _lineRenderer.startWidth = 0.1f;
                _lineRenderer.endWidth = 0.1f;
                _lineRenderer.useWorldSpace = true;
                _lineRenderer.positionCount = 5; // 闭合矩形
                _lineRenderer.enabled = false; // 默认隐藏
                fk.SetActive(false);
            }

            // 切换显示状态
            if (fk.activeSelf)
            {
                // 如果已显示，则隐藏并停止协程
                fk.SetActive(false);
                if (_drawCoroutine != null)
                {
                    EditorRuntime.Stop(_drawCoroutine);
                    _drawCoroutine = null;
                }
                _isDrawing = false;
            }
            else
            {
                // 显示并启动协程
                fk.SetActive(true);
                _lineRenderer.enabled = true;
                // 记录固定角为玩家当前位置
                var player = TipMsgCore.i.mainplayer.transform;
                if (player != null)
                {
                    _fixedCorner = player.position;
                    // 启动协程
                    if (_drawCoroutine != null)
                        EditorRuntime.Stop(_drawCoroutine);
                    _isDrawing = true;
                    _drawCoroutine = EditorRuntime.Start(UpdateRectCoroutine());
                    
                }
                else
                {
                    stc.Log("玩家不存在，无法启动辅助框");
                    fk.SetActive(false);
                }
            }
        }

        IEnumerator UpdateRectCoroutine()
        {
            stc.Log("更新矩形协程已启动");
            var player = TipMsgCore.i.mainplayer?.transform;
            if (player == null)
            {
                stc.Log("玩家不存在，协程退出");
                yield break;
            }

            while (_isDrawing)
            {
               

                _movingCorner = player.position;
                float yOffset = player.position.y + 1;
                Vector3 p1 = new Vector3(_fixedCorner.x, yOffset, _fixedCorner.z);
                Vector3 p2 = new Vector3(_movingCorner.x, yOffset, _fixedCorner.z);
                Vector3 p3 = new Vector3(_movingCorner.x, yOffset, _movingCorner.z);
                Vector3 p4 = new Vector3(_fixedCorner.x, yOffset, _movingCorner.z);

                try
                {
                    _lineRenderer.SetPositions(new Vector3[] { p1, p2, p3, p4, p1 });
                }
                catch (System.Exception ex)
                {
                    stc.Log("SetPositions 异常: " + ex.Message);
                }
                if (Input.GetKeyDown(KeyCode.Alpha0))
                {
                    StopDrawing();
                    Copyitems(_fixedCorner, true);
                    stc.Log("按键0退出");
                    yield break;
                }
               // stc.Log(string.Format("固定角: {0:F2}, {1:F2} | 移动角: {2:F2}, {3:F2}",
                    //_fixedCorner.x, _fixedCorner.z, movingCorner.x, movingCorner.z));

                yield return null;
            }
            stc.Log("协程正常退出（_isDrawing = false）");
        }

        private void StopDrawing()
        {
            _isDrawing = false;
            if (_drawCoroutine != null)
            {
                EditorRuntime.Stop(_drawCoroutine);
                _drawCoroutine = null;
            }
            if (fk != null)
            {
                _lineRenderer.enabled = false;
                fk.SetActive(false);
            }
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


        // Preview meshes have no game furniture components, IDs or server registration.
        public IEnumerator showcp(IEnumerator cp=null)
        {
            GameObject pivot = new GameObject("HomeEditorPreview"); EditorRuntime.Own(pivot);
            pivot.transform.position = GetCenterAverage(items);
            var previews = new List<(Itempot item, Transform transform)>();
            foreach (var data in items)
            {
                GameObject visual = new GameObject("Preview." + data.id); visual.transform.SetParent(pivot.transform, true);
                visual.transform.position=data.Position; visual.transform.rotation=data.Rotation;
                foreach(var furniture in IHomeSystem.sys.allFurnitures.Values)
                {
                    var source=furniture.gameObject.GetComponent<HomeItem_Furniture>();
                    if(source != null && source.tableId == data.id) { CopyMesh(source.transform,visual.transform,true); break; }
                }
                previews.Add((data,visual.transform));
            }
            ObjectController controller = new(); EditorRuntime.Controller=controller;
            controller.Activate(pivot.transform);
            bool confirmed=false;
            try {
                while(EditorRuntime.Ready) {
                    yield return null; controller.OnUpdate();
                    if(Input.GetKeyDown(KeyCode.Escape)) break;
                    if(Input.GetKeyDown(KeyCode.Tab)) { confirmed=true; break; }
                }
                if(confirmed) foreach(var preview in previews) { preview.item.Position=preview.transform.position; preview.item.Rotation=preview.transform.rotation; }
            } finally { controller.Deactivate(); EditorRuntime.Controller=null; EditorRuntime.ReleasePivot(pivot); }
            if(confirmed && cp != null) EditorRuntime.Start(cp);
            else Stute=Stute_AutoHomeItem.None;
        }
        private static void CopyMesh(Transform source, Transform parent, bool root=false)
        {
            var go=new GameObject(source.name); go.transform.SetParent(parent,false);
            go.transform.localPosition=root ? Vector3.zero : source.localPosition; go.transform.localRotation=root ? Quaternion.identity : source.localRotation; go.transform.localScale=root ? source.lossyScale : source.localScale;
            var mesh=source.GetComponent<MeshFilter>(); var renderer=source.GetComponent<MeshRenderer>();
            if(mesh != null && renderer != null) { go.AddComponent<MeshFilter>().sharedMesh=mesh.sharedMesh; go.AddComponent<MeshRenderer>().sharedMaterials=renderer.sharedMaterials; }
            for(int i=0;i<source.childCount;i++) CopyMesh(source.GetChild(i),go.transform);
        }
        public void Reset() { cplocker = false; StopDrawing(); if (fk != null) UnityEngine.Object.Destroy(fk); fk = null; Stute = Stute_AutoHomeItem.None; }
        bool cplocker = false;
        public IEnumerator Pasteitems(List<Itempot> items_s, bool fk = false, bool qzcp = false)
        {
            if (cplocker) yield break;
            cplocker = true;
            try {
            if (IHomeSystem.OwnGarden && items_s.Count != 0)
            {
                //HomeTool.Confirm移除全部装饰(true);
                yield return new WaitForSeconds(0.1f);
                //todo
                //BagsSystem.GetItemCount(int);
                Dictionary<int, int> idCount = new Dictionary<int, int>();
                List<int> qs = new List<int>();
                foreach (var item in items_s)
                {
                    int id = item.id;
                    if (idCount.ContainsKey(id))
                        idCount[id]++;
                    else
                        idCount[id] = 1;
                }

                bool cancp = true;
                foreach (var kvp in idCount)
                {
                    if (kvp.Value > (BagsSystem.Instance.GetItemCount(kvp.Key) - IHomeSystem.sys.GetHomeItemCount(kvp.Key)))
                    {
                        stc.Log("缺少物品:" + IExcelSystem.Item.GetItemString(kvp.Key).text_[0] + ",数量:" + (kvp.Value - (BagsSystem.Instance.GetItemCount(kvp.Key) - IHomeSystem.sys.GetHomeItemCount(kvp.Key))));
                        qs.Add(kvp.Key);
                        cancp = false;
                    }
                }
                if (!cancp && !qzcp)
                {
                    TipMsgCore.i.Send("家具数量不足");
                    Stute = Stute_AutoHomeItem.None;
                    cplocker = false;
                    yield break;
                }

                Vector3 pl = TipMsgCore.i.mainplayer.transform.position;
                foreach (var item in items_s)
                {
                    if (Stute == Stute_AutoHomeItem.None || !EditorRuntime.Ready)
                    {
                        TipMsgCore.i.Send("退出");
                        cplocker = false;
                        yield break;
                    }
                    if (qzcp)
                    {
                        bool kg = false;
                        foreach (var id in qs)
                        {
                            if (id == item.id) { kg = true; break; }
                        }
                        if (kg) continue;
                    }
                    int num = IHomeSystem.sys.allFurnitures.Keys.Count;
                    while (num == IHomeSystem.sys.allFurnitures.Keys.Count)
                    {
                        if (Stute == Stute_AutoHomeItem.None || !EditorRuntime.Ready)
                        {
                            TipMsgCore.i.Send("退出");
                            cplocker = false;
                            yield break;
                        }
                        yield return new WaitForSeconds(0.6f);
                        IHomeSystem.sys.CreateAndPickUp(item.id);
                        yield return new WaitForSeconds(0.6f);
                        DetectInfo det = new DetectInfo() { pos = item.Position, rot = item.Rotation, gridId = IHomeSystem.sys.detectInfo.gridId };
                        IHomeSystem.sys.detectInfo = det;
                        if (IHomeSystem.sys.carryingItem != null) IHomeSystem.sys.PutDownCarrying();
                        stc.Log(item.id + " " + det.pos.x + " " + det.pos.z);
                    }
                }

                if (IHomeSystem.sys.allFurnitures.Keys.Count != items_s.Count) TipMsgCore.i.Send("cp完成,校验出错");
                else TipMsgCore.i.Send("cp完成");
                Stute = Stute_AutoHomeItem.None;
                if (fk) Stute = Stute_AutoHomeItem.Fking;
            }
            } finally { cplocker = false; }
        }
        public bool IsPointInRect(Vector3 point)
        {
            float minX = Mathf.Min(_fixedCorner.x, _movingCorner.x);
            float maxX = Mathf.Max(_fixedCorner.x, _movingCorner.x);
            float minZ = Mathf.Min(_fixedCorner.z, _movingCorner.z);
            float maxZ = Mathf.Max(_fixedCorner.z, _movingCorner.z);
            return point.x >= minX && point.x <= maxX && point.z >= minZ && point.z <= maxZ;
        }
        public void Update(string message)
        {
            string[] msg = message.Split(' ');
            switch (Stute)
            {
                case Stute_AutoHomeItem.None:
                    switch (msg[0])
                    {
                        case "-cc":
                            if (msg.Length == 1) Copyitems(Vector3.zero);
                            else if (msg.Length == 2 && msg[1] == "part")
                            {
                                Copyitems_part();
                                TipMsgCore.i.OK();
                            } break;
                        case "-set":
                            _fixedCorner = TipMsgCore.i.mainplayer.transform.position;
                            break;                           
                        case "-cp":
                            if (msg.Length == 2 && msg[1]=="t") EditorRuntime.Start(showcp(Pasteitems(items, false, true)));
                            else EditorRuntime.Start(showcp(Pasteitems(items)));
                            Stute = Stute_AutoHomeItem.Pasting;
                            break;
                        case "-sv":
                            if (msg.Length == 2)
                            {
                                ItempotStorage.SaveItempots(path  + msg[1] + ".json", items);
                                TipMsgCore.i.OK();
                            }
                            else if (msg.Length == 3)
                            {
                                if (msg[1] == "Y")
                                {
                                    ItempotStorage.SaveFloat(path + "Y" + msg[2] + ".json", FreeSetHomeItem.plehighex.y);
                                    TipMsgCore.i.OK();
                                }
                            }
                            else TipMsgCore.i.Send("参数出错:-sv [Y] file");
                            break;
                        case "-rd":
                            if (msg.Length == 2)
                            {
                                var loaded = ItempotStorage.LoadItempots(path + msg[1] + ".json");
                                if (loaded != null) { items.Clear(); items.AddRange(loaded); }
                                if (loaded != null) TipMsgCore.i.OK();
                                else TipMsgCore.i.ERR();
                            }
                            else if (msg.Length == 3)
                            {
                                if (msg[1] == "Y")
                                {
                                    FreeSetHomeItem.plehighex.y = ItempotStorage.LoadFloat(path + "Y" + msg[2] + ".json");
                                    TipMsgCore.i.OK();
                                }
                            }
                            else TipMsgCore.i.Send("参数出错:-rd [Y] file");
                            break;
                        case "-ck":
                            stc.Log(path);
                            var a = ItempotStorage.GetFileNamesInDirectory(path);
                            foreach(var b in a)
                            {
                                stc.Log(b);
                            }
                            break;
                        case "-fl":
                            if (msg.Length != 2) { TipMsgCore.i.Send("参数错误:-fl 名字"); break; }
                            if (!TipMsgCore.i.players.TryGetValue(msg[1], out var p)) { TipMsgCore.i.Send("场景查无此人"); break; }
                            Followsys.i.Set(p);
                            Stute = Stute_AutoHomeItem.fling;
                            break;
                        case "-fk":
                            Fksys.i.start();
                            Stute = Stute_AutoHomeItem.Fking;
                            break;
                    }
                    break;
                case Stute_AutoHomeItem.Pasting:
                    //Pasteitems();
                    break;
                case Stute_AutoHomeItem.fling:
                    Followsys.i.Updata();
                    break;
                case Stute_AutoHomeItem.Fking:
                    Fksys.i.updata(message);
                    break;

                    
            }
            if (msg[0] == "-end")
            {
                Stute = Stute_AutoHomeItem.None;

            }
        }



    }

}
