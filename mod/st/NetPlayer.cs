using Chat.Client;
using MelonLoader;
using System.Collections;
using UnityEngine;

namespace mod.st
{
    public class NetPlayer
    {
        public static NetPlayer Instance = new();
        public static GameObject root;
        public Dictionary<string,GameObject> players = new ();
        public Dictionary<string,List<GameObject>> FollowplayersList = new ();
        public void reset()
        {
            MonoBehaviour.Destroy (root);
            root = new GameObject("NetPlayerRoot");
            Behaviour.DontDestroyOnLoad(root);
            players.Clear ();
            FollowplayersList.Clear ();
        }
        public void start()
        {
            //注册事件
            NetChater.Instance.OnChannelEvent += req;
            NetChater.Instance.OnMessageReceived += BoatShapeController.Req;

            root = new GameObject("NetPlayerRoot");
            Behaviour.DontDestroyOnLoad(root);
        }
        public void updata()
        {
            if (FollowplayersList.Count != 0)
            {
                foreach (var Followlistg in FollowplayersList)
                {
                    if (TipMsgCore.i.players.TryGetValue(Followlistg.Key, out var p))
                    {
                        foreach (var g in Followlistg.Value)
                        {
                            if (g != null)
                            {
                                g.transform.position = p.transform.position + Vector3.down;
                                g.transform.rotation = Quaternion.Euler(0, p.transform.rotation.y, 0);
                            }
                        }
                    }
                }
            }
        }
        static List<GameObject> Destroylist = new();
        public static void DestroySmoothly(GameObject root)
        {
            if (root == null) return;

            // 1. 立刻禁用，视觉上"消失"
            root.SetActive(false);
            // 2. 分帧销毁
            MelonCoroutines.Start(DestroyHierarchySmooth(root, 1));
        }
        public static IEnumerator DestroyHierarchySmooth(GameObject root,int batchSize = 20)
        {
            if (root == null) yield break;
            if (Destroylist.Contains(root)) yield break;
            Destroylist.Add(root);
            // ---------- 2. 扁平化收集所有节点（含根对象） ----------
            List<Transform> allNodes = new List<Transform>();
            CollectAllRecursive(root.transform, allNodes);

            int total = allNodes.Count;
            MelonLogger.Msg($"[SmoothDestroy] 准备销毁 {total} 个对象（含子级）");

            // ---------- 3. 从最深节点开始销毁 ----------
            // 按层级深度排序：深度大的先销毁（叶子先删）
            allNodes.Sort((a, b) => GetDepth(b).CompareTo(GetDepth(a)));

            int destroyed = 0;
            foreach (var t in allNodes)
            {
                // 可能被前面父级销毁时连带销毁了，判空跳过
                if (t == null || t.gameObject == null) continue;

                GameObject.Destroy(t.gameObject);
                destroyed++;

                // 每 batchSize 个让出一帧
                if (destroyed % batchSize == 0)
                    yield return null;
            }
            Destroylist.Remove(root);
            MelonLogger.Msg($"[SmoothDestroy] 已分帧销毁 {destroyed}/{total} 个对象");
        }

        /// <summary>递归收集所有子节点（含自身）</summary>
        private static void CollectAllRecursive(Transform node, List<Transform> result)
        {
            if (node == null) return;
            result.Add(node);

            for (int i = 0; i < node.childCount; i++)
                CollectAllRecursive(node.GetChild(i), result);
        }

        /// <summary>获取节点在层级中的深度</summary>
        private static int GetDepth(Transform t)
        {
            int depth = 0;
            while (t.parent != null)
            {
                depth++;
                t = t.parent;
            }
            return depth;
        }
        void req(ChannelEventInfo info)
        {
            if (info == null) return;
            if (info.Type == "join")
            {
                var a = root.transform.Find(info.Nick);
                if ( a == null) 
                {
                    var g = new GameObject(info.Nick);
                    g.transform.SetParent(root.transform);
                    players.Add(g.name, g);
                }
                else
                {
                    players.Add(a.name, a.gameObject);
                }
                FollowplayersList.TryAdd(info.Nick, new());
            }
            if (info.Type == "leave")
            {
                var a = players[info.Nick];
                players.Remove(info.Nick);
                DestroySmoothly(a.gameObject);
                FollowplayersList.Remove(info.Nick);
            }
        }
    }
}
