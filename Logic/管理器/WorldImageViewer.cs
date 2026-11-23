namespace Logic.管理器
{
    using System.Collections.Generic;
    using MelonLoader;
    using UnityEngine;
    using UnityEngine.UI;

    public static class WorldImageViewer
    {
        private class ImageItem
        {
            public GameObject Root;
            public RectTransform Rect;
            public BoxCollider Collider;
            public Texture2D Texture;

            // 记录当前的 Y 轴旋转角度
            public float CurrentRotY = 0f;
        }

        private static readonly Dictionary<BoxCollider, ImageItem> _items = new();
        private static ImageItem _activeItem = null;
        private static ImageItem _hoverItem = null;

        private static Vector3 _dragOffset;
        private static Plane _dragPlane;

        private const float SpawnScale = 0.001f;
        private const float ZoomSpeed = 0.5f;
        private const float RotateSpeed = 15f; // Y轴旋转速度（度/每格滚轮）
        private const float MinScale = 0.1f;
        private const float MaxScale = 10f;

        public static bool HasImages => _items.Count > 0;

        public static void SpawnImage(Texture2D texture, Vector3? worldPos = null)
        {
            if (texture == null) return;

            Camera cam = Camera.main;
            if (cam == null) return;

            Vector3 pos = worldPos ?? (cam.transform.position + cam.transform.forward * 3f);

            GameObject root = new GameObject($"WorldImage_{texture.name}");
            root.transform.position = pos;
            root.transform.localScale = Vector3.one * SpawnScale;

            // 【修改】生成时默认让图片面向相机，给个好的初始视角。之后就不再强制朝向相机了。
            root.transform.rotation = Quaternion.LookRotation(cam.transform.forward, cam.transform.up);

            Canvas canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;

            var scaler = root.AddComponent<CanvasScaler>();
            scaler.dynamicPixelsPerUnit = 10f;

            RectTransform canvasRect = root.GetComponent<RectTransform>();
            canvasRect.sizeDelta = new Vector2(texture.width, texture.height);

            var imgObj = new GameObject("Image");
            imgObj.transform.SetParent(root.transform, false);
            var rawImage = imgObj.AddComponent<RawImage>();
            rawImage.texture = texture;

            RectTransform imgRect = imgObj.GetComponent<RectTransform>();
            imgRect.anchorMin = Vector2.zero;
            imgRect.anchorMax = Vector2.one;
            imgRect.offsetMin = Vector2.zero;
            imgRect.offsetMax = Vector2.zero;

            var collider = root.AddComponent<BoxCollider>();
            collider.size = new Vector3(texture.width, texture.height, 10f);

            var item = new ImageItem
            {
                Root = root,
                Rect = canvasRect,
                Collider = collider,
                Texture = texture,
                CurrentRotY = 0f // 初始局部 Y 轴旋转为 0
            };

            _items[collider] = item;
            MelonLogger.Msg($"[WorldImageViewer] 生成图片 {texture.name}");
        }

        public static void SpawnImageFromFile(string filePath, Vector3? worldPos = null)
        {
            try
            {
                byte[] data = System.IO.File.ReadAllBytes(filePath);
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (tex.LoadImage(data))
                    SpawnImage(tex, worldPos);
            }
            catch (System.Exception ex)
            {
                MelonLogger.Error($"[WorldImageViewer] 读取失败: {ex.Message}");
            }
        }

        public static void ClearAll()
        {
            foreach (var item in _items.Values)
            {
                if (item.Root != null) Object.Destroy(item.Root);
                if (item.Texture != null) Object.Destroy(item.Texture);
            }
            _items.Clear();
            _activeItem = null;
            _hoverItem = null;
        }

        public static void Tick()
        {
            if (_items.Count == 0) return;

            Camera cam = Camera.main;
            if (cam == null) return;

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                ClearAll();
                return;
            }

            Vector3 mousePos = Input.mousePosition;
            Ray ray = cam.ScreenPointToRay(mousePos);
            bool hitSomething = Physics.Raycast(ray, out RaycastHit hit, 100f);

            // 1. 更新悬停状态
            if (hitSomething && _items.TryGetValue((BoxCollider)hit.collider, out var hoverItem))
                _hoverItem = hoverItem;
            else
                _hoverItem = null;

            // 2. 鼠标左键按下：选定并准备拖拽
            if (Input.GetMouseButtonDown(0))
            {
                if (_hoverItem != null)
                {
                    _activeItem = _hoverItem;
                    // 拖拽平面始终朝向相机（这样即使图片转到了侧面，拖拽起来也符合鼠标直觉）
                    _dragPlane = new Plane(-cam.transform.forward, _activeItem.Root.transform.position);
                    if (_dragPlane.Raycast(ray, out float distance))
                    {
                        Vector3 point = ray.GetPoint(distance);
                        _dragOffset = _activeItem.Root.transform.position - point;
                    }
                }
                else
                {
                    _activeItem = null;
                }
            }

            // 3. 鼠标左键松开：取消拖拽
            if (Input.GetMouseButtonUp(0))
            {
                _activeItem = null;
            }

            // 4. 处理滚轮（缩放与 Y 轴旋转）
            float scroll = Input.mouseScrollDelta.y;
            if (Mathf.Abs(scroll) > 0.01f)
            {
                var target = _activeItem ?? _hoverItem;

                if (target != null)
                {
                    // 判断是否按住了 Ctrl
                    if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))
                    {
                        // --- 绕 Y 轴旋转 ---
                        target.CurrentRotY += scroll * RotateSpeed;
                        target.CurrentRotY = (target.CurrentRotY % 360f + 360f) % 360f; // 限制在 0-360
                    }
                    else
                    {
                        // --- 缩放 ---
                        Vector3 newScale = target.Root.transform.localScale + Vector3.one * scroll * ZoomSpeed * SpawnScale;
                        newScale.x = Mathf.Clamp(newScale.x, MinScale * SpawnScale, MaxScale * SpawnScale);
                        newScale.y = Mathf.Clamp(newScale.y, MinScale * SpawnScale, MaxScale * SpawnScale);
                        target.Root.transform.localScale = newScale;
                    }
                }
            }

            // 5. 执行拖拽
            if (Input.GetMouseButton(0) && _activeItem != null)
            {
                _dragPlane = new Plane(-cam.transform.forward, _activeItem.Root.transform.position);
                if (_dragPlane.Raycast(ray, out float distance))
                {
                    Vector3 point = ray.GetPoint(distance);
                    _activeItem.Root.transform.position = point + _dragOffset;
                }
            }

            // 6. 应用 Y 轴旋转（不再朝向相机）
            foreach (var item in _items.Values)
            {
                if (item.Root == null) continue;
                // 绕自身局部坐标系的 Y 轴旋转
                item.Root.transform.localRotation = Quaternion.Euler(0f, item.CurrentRotY, 0f);
            }
        }
    }
}
