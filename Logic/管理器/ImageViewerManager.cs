namespace Logic.管理器
{
    using MelonLoader;
    using MelonLoader.Utils;
    using System.IO;
    using System.Reflection;
    using UnityEngine;
    using UnityEngine.UI;

    public static class ImageViewerManager
    {
        private static GameObject _canvasObj;
        private static Canvas _canvas;
        private static RectTransform _canvasRect;
        private static RectTransform _imgRect;
        private static RawImage _rawImage;
        private static Texture2D _currentTexture;

        // 交互状态
        private static bool _isDragging = false;
        private static Vector2 _lastMousePos;
        private static Vector3 _originalScale;

        // 参数配置
        private const float ZoomSpeed = 0.1f;
        private const float MinScale = 0.5f;
        private const float MaxScale = 5f;

        public static bool IsShowing => _canvasObj != null;

        /// <summary>
        /// 显示图片
        /// </summary>
        public static void ShowImage(Texture2D texture)
        {
            if (texture == null) return;

            CloseImage(); // 确保之前清理干净

            // 1. 创建 Canvas
            _canvasObj = new GameObject("ImageViewerCanvas");
            _canvas = _canvasObj.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 9999; // 最顶层

            var scaler = _canvasObj.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);

            // 注意：这里故意不加 GraphicRaycaster，因为我们不用 EventSystem

            _canvasRect = _canvasObj.GetComponent<RectTransform>();

            // 2. 创建背景（可选）
            var bgObj = new GameObject("Background");
            bgObj.transform.SetParent(_canvasObj.transform, false);
            var bgImage = bgObj.AddComponent<Image>();
            bgImage.color = new Color(0f, 0f, 0f, 0.8f);
            var bgRect = bgObj.GetComponent<RectTransform>();
            bgRect.anchorMin = Vector2.zero;
            bgRect.anchorMax = Vector2.one;
            bgRect.offsetMin = Vector2.zero;
            bgRect.offsetMax = Vector2.zero;

            // 3. 创建图片
            var imgObj = new GameObject("Image");
            imgObj.transform.SetParent(_canvasObj.transform, false);
            _rawImage = imgObj.AddComponent<RawImage>();
            _rawImage.texture = texture;

            _imgRect = imgObj.GetComponent<RectTransform>();
            _imgRect.anchorMin = new Vector2(0.5f, 0.5f);
            _imgRect.anchorMax = new Vector2(0.5f, 0.5f);
            _imgRect.pivot = new Vector2(0.5f, 0.5f);
            _imgRect.sizeDelta = new Vector2(texture.width, texture.height);
            _imgRect.anchoredPosition = Vector2.zero;

            _originalScale = Vector3.one;
            _imgRect.localScale = _originalScale;

            _currentTexture = texture;
            _isDragging = false;

            MelonLogger.Msg($"[ImageViewer] 已显示：{texture.name}");
        }

        // 获取当前 Mod 的 DLL 所在目录
        private static string GetModDirectory()
        {
            return Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        }

        public static void ShowImageFromFile(string filePath, bool useUserDataAsBase = true)
        {
            string fullPath = filePath;

            // 1. 如果传的是相对路径，根据需求拼接
            if (!Path.IsPathRooted(filePath))
            {
                if (useUserDataAsBase)
                {
                    // 优先在 UserData 目录下找
                    string userDataPath = Path.Combine(MelonEnvironment.UserDataDirectory, filePath);
                    // 如果在 UserData 下找到了，就用它
                    if (File.Exists(userDataPath))
                    {
                        fullPath = userDataPath;
                    }
                    else
                    {
                        // 没找到就退回游戏根目录
                        fullPath = Path.Combine(Directory.GetCurrentDirectory(), filePath);
                    }
                }
                else
                {
                    // 如果不使用 UserData，默认当前目录（游戏根目录）
                    fullPath = Path.GetFullPath(filePath);
                }
            }

            // 2. 检查文件存在性
            if (!File.Exists(fullPath))
            {
                MelonLogger.Warning($"[ImageViewer] 文件不存在: {fullPath}");
                return;
            }

            // 3. 读取并加载
            try
            {
                byte[] data = File.ReadAllBytes(fullPath);
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (tex.LoadImage(data))
                {
                    ShowImage(tex);
                    MelonLogger.Msg($"[ImageViewer] 加载成功: {fullPath}");
                }
                else
                {
                    MelonLogger.Warning("[ImageViewer] 图片解析失败");
                }
            }
            catch (System.Exception ex)
            {
                MelonLogger.Error($"[ImageViewer] 读取文件异常: {ex.Message}");
            }
        }
        public static void CloseImage()
        {
            if (_canvasObj != null)
            {
                Object.Destroy(_canvasObj);
                _canvasObj = null;
                _canvas = null;
                _canvasRect = null;
                _imgRect = null;
                _rawImage = null;

                if (_currentTexture != null)
                {
                    Object.Destroy(_currentTexture); // 释放动态生成的纹理
                    _currentTexture = null;
                }
                MelonLogger.Msg("[ImageViewer] 已关闭");
            }
        }

        /// <summary>
        /// 【核心】需要放到主更新循环（如 OnUpdate）中每帧调用！
        /// </summary>
        public static void Tick()
        {

            if (_canvasObj == null || _imgRect == null) return;

            Vector2 mousePos = Input.mousePosition;

            // 1. 滚轮缩放 (无论鼠标在哪，只要滚了就缩放)
            float scroll = Input.mouseScrollDelta.y;
            if (Mathf.Abs(scroll) > 0.01f)
            {
                Vector3 newScale = _imgRect.localScale + Vector3.one * scroll * ZoomSpeed;
                newScale.x = Mathf.Clamp(newScale.x, MinScale, MaxScale);
                newScale.y = Mathf.Clamp(newScale.y, MinScale, MaxScale);
                _imgRect.localScale = newScale;
            }

            // 2. 拖拽逻辑 (使用鼠标左键或中键)
            // 检查鼠标是否在图片矩形内 (Screen Space Overlay 模式下 Camera 传 null)
            bool isHovered = RectTransformUtility.RectangleContainsScreenPoint(_imgRect, mousePos, null);

            if (Input.GetMouseButtonDown(0) && isHovered)
            {
                _isDragging = true;
                _lastMousePos = mousePos;
            }
            else if (Input.GetMouseButtonUp(0))
            {
                _isDragging = false;
            }

            if (_isDragging)
            {
                // 计算鼠标位移
                Vector2 delta = mousePos - _lastMousePos;

                // 累加到 anchoredPosition
                _imgRect.anchoredPosition += delta;

                _lastMousePos = mousePos;
            }

            // 3. 快捷关闭：按 ESC
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                CloseImage();
            }
        }
    }
}
