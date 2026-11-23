//using Il2CppInutan;
//using mod;
//using System.Collections.Generic;
//using System.Linq;
//using UnityEngine;

//public class FurnitureGizmo
//{
//    // 当前选中的家具点集
//    public List<Itempot> Selection { get; private set; } = new List<Itempot>();

//    // 所有可选的家具池（通常绑定 AutoHomeItem.i.items）
//    private List<Itempot> _allItems;

//    public FurnitureGizmo(List<Itempot> allItems)
//    {
//        _allItems = allItems ?? new List<Itempot>();
//    }

//    // ==========================================
//    // 1. 选中逻辑 (Selection)
//    // ==========================================

//    public void ClearSelection()
//    {
//        Selection.Clear();
//    }

//    /// <summary>
//    /// 全选
//    /// </summary>
//    public void SelectAll()
//    {
//        Selection.Clear();
//        Selection.AddRange(_allItems);
//    }

//    /// <summary>
//    /// 点选：通过射线（准星/鼠标点击）选中家具
//    /// </summary>
//    /// <param name="ray">射线</param>
//    /// <param name="additive">是否加选（按住 Shift/Ctrl）</param>
//    /// <param name="posOffset">游戏世界坐标到 Itempot 坐标的偏移量（如果有）</param>
//    public bool SelectByRay(Ray ray, bool additive = false, Vector3 posOffset = default)
//    {
//        // 假设游戏里的家具在 Ignore Raycast 层之外，并且有碰撞体
//        if (Physics.Raycast(ray, out RaycastHit hit, 100f))
//        {
//            var furniture = hit.collider.GetComponentInParent<HomeItem_Furniture>();
//            if (furniture != null)
//            {
//                // 将游戏实体坐标转回 Itempot 的坐标空间
//                Vector3 targetPos = furniture.transform.position - posOffset;

//                // 在缓存池里找距离最近的匹配点
//                Itempot targetItem = _allItems.FirstOrDefault(item => Vector3.Distance(item.Position, targetPos) < 0.5f);

//                if (targetItem != null)
//                {
//                    if (!additive) Selection.Clear();

//                    if (!Selection.Contains(targetItem))
//                        Selection.Add(targetItem);

//                    return true;
//                }
//            }
//        }
//        return false;
//    }

//    /// <summary>
//    /// 框选：计算屏幕矩形内的所有家具
//    /// </summary>
//    /// <param name="minScreenPos">屏幕左下角 (鼠标按下位置)</param>
//    /// <param name="maxScreenPos">屏幕右上角 (鼠标松开位置)</param>
//    /// <param name="cam">主相机</param>
//    /// <param name="additive">是否加选</param>
//    /// <param name="posOffset">坐标偏移</param>
//    public void SelectByScreenBox(Vector2 minScreenPos, Vector2 maxScreenPos, Camera cam, bool additive = false, Vector3 posOffset = default)
//    {
//        if (!additive) Selection.Clear();
//        if (cam == null) return;

//        // 构建屏幕矩形
//        Rect screenRect = new Rect(
//            Mathf.Min(minScreenPos.x, maxScreenPos.x),
//            Mathf.Min(minScreenPos.y, maxScreenPos.y),
//            Mathf.Abs(minScreenPos.x - maxScreenPos.x),
//            Mathf.Abs(minScreenPos.y - maxScreenPos.y)
//        );

//        foreach (var item in _allItems)
//        {
//            // 将 Itempot 的本地坐标转换为世界坐标
//            Vector3 worldPos = item.Position + posOffset;

//            // 转为屏幕坐标
//            Vector3 screenPos = cam.WorldToScreenPoint(worldPos);

//            // 如果在相机前面，且屏幕坐标在框选矩形内
//            if (screenPos.z > 0 && screenRect.Contains(screenPos))
//            {
//                if (!Selection.Contains(item))
//                    Selection.Add(item);
//            }
//        }
//    }

//    // ==========================================
//    // 2. 变换逻辑 (Transform)
//    // ==========================================

//    public Vector3 GetPivot()
//    {
//        if (Selection.Count == 0) return Vector3.zero;
//        Vector3 center = Vector3.zero;
//        foreach (var item in Selection) center += item.Position;
//        return center / Selection.Count;
//    }

//    public void Translate(Vector3 offset)
//    {
//        foreach (var item in Selection) item.Position += offset;
//    }

//    public void Rotate(Quaternion rotation, Vector3? pivotPoint = null)
//    {
//        Vector3 pivot = pivotPoint ?? GetPivot();
//        foreach (var item in Selection)
//        {
//            Vector3 relativePos = item.Position - pivot;
//            item.Position = pivot + (rotation * relativePos);
//            item.Rotation = rotation * item.Rotation;
//        }
//    }

//    public void Scale(float factor, Vector3? pivotPoint = null)
//    {
//        Vector3 pivot = pivotPoint ?? GetPivot();
//        foreach (var item in Selection)
//        {
//            Vector3 relativePos = item.Position - pivot;
//            item.Position = pivot + (relativePos * factor);
//        }
//    }

//    /// <summary>
//    /// 4. 阵列复制 (Array Modifier) - 相当于 Blender 里的阵列
//    /// </summary>
//    public List<Itempot> Array(Vector3 stepOffset, int count, bool rotateAlong = false, Quaternion? rotationStep = null)
//    {
//        List<Itempot> result = new List<Itempot>(Selection);

//        // 以第一个选中的家具为基准进行阵列
//        if (Selection.Count == 0) return result;
//        Itempot baseItem = Selection[0];

//        for (int i = 1; i <= count; i++)
//        {
//            Vector3 newPos = baseItem.Position + (stepOffset * i);
//            Quaternion newRot = baseItem.Rotation;

//            if (rotateAlong && rotationStep.HasValue)
//            {
//                newRot = baseItem.Rotation * (Quaternion.Euler(rotationStep.Value.eulerAngles * i));
//            }

//            // 深拷贝一个新的点
//            result.Add(new Itempot(newPos, newRot, baseItem.id, baseItem.Name));
//        }
//        return result;
//    }

//    /// <summary>
//    /// 5. 对齐 (Align) - 把所有家具的朝向、坐标轴对齐
//    /// </summary>
//    public void AlignToGrid(float gridSize = 1.0f)
//    {
//        foreach (var item in Selection)
//        {
//            // 将坐标吸附到最近的网格点上
//            item.Position = new Vector3(
//                Mathf.Round(item.Position.x / gridSize) * gridSize,
//                Mathf.Round(item.Position.y / gridSize) * gridSize,
//                Mathf.Round(item.Position.z / gridSize) * gridSize
//            );

//            // 把家具旋转吸附到90度的整数倍 (面向东南西北)
//            Vector3 euler = item.Rotation.eulerAngles;
//            euler.x = Mathf.Round(euler.x / 90f) * 90f;
//            euler.y = Mathf.Round(euler.y / 90f) * 90f;
//            euler.z = Mathf.Round(euler.z / 90f) * 90f;
//            item.Rotation = Quaternion.Euler(euler);
//        }
//    }

//    /// <summary>
//    /// 6. 镜像翻转 (Mirror) - 沿某根轴翻转
//    /// </summary>
//    public void Mirror(Vector3 mirrorAxis, Vector3? pivotPoint = null)
//    {
//        Vector3 pivot = pivotPoint ?? GetPivot();
//        foreach (var item in Selection)
//        {
//            Vector3 relativePos = item.Position - pivot;

//            // 仅翻转位置
//            if (mirrorAxis.x != 0) relativePos.x = -relativePos.x;
//            if (mirrorAxis.y != 0) relativePos.y = -relativePos.y;
//            if (mirrorAxis.z != 0) relativePos.z = -relativePos.z;

//            item.Position = pivot + relativePos;

//            // 翻转家具的旋转（通常是绕 Y 轴翻转）
//            if (mirrorAxis.x != 0 || mirrorAxis.z != 0)
//            {
//                item.Rotation = Quaternion.LookRotation(
//                    Vector3.Reflect(item.Rotation * Vector3.forward, mirrorAxis),
//                    Vector3.Reflect(item.Rotation * Vector3.up, mirrorAxis)
//                );
//            }
//        }
//    }
//}