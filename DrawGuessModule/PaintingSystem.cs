using System;
using System.Collections.Generic;
using System.Globalization;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace DrawGuessModule
{
    /// <summary>
    /// Main-thread drawing view. Requests never change pixels: the runtime must validate,
    /// order and accept an operation before calling ApplyOperation on every participant.
    /// Coordinates use a bottom-left origin, 0..511; size is the integer brush radius, 1..32.
    /// A stroke token stays the same for all pointer segments. The runtime supplies
    /// newStroke=true for its first accepted segment, also during ordered replay.
    /// No texture data, secret words or network messages are sent by this class.
    /// </summary>
    public static partial class PaintingSystem
    {
        public const int CanvasSize = 512;
        public const int MaxUndoSteps = 8;
        public const int MaxBrushSize = 32;
        private const float RequestInterval = 0.05f;
        private static readonly Color32 White = new Color32(255, 255, 255, 255);
        private static readonly List<Color32[]> UndoBuffers = new List<Color32[]>(MaxUndoSteps);
        private static readonly List<Button> OwnedButtons = new List<Button>();
        private static readonly List<Button> DrawButtons = new List<Button>();
        private static readonly Color PanelColor = new Color(0.075f, 0.095f, 0.14f, 0.99f);
        private static readonly Color ButtonColor = new Color(0.18f, 0.25f, 0.35f, 1f);
        private static Action<string>? _command;
        private static Color32[]? _pixels;
        private static int[]? _fillQueue;
        private static Il2CppStructArray<Color32>? _upload;
        private static Texture2D? _texture;
        private static GameObject? _root, _ownedEventSystem;
        private static RectTransform? _drawRect;
        private static Text? _statusText, _detailText, _drawerText, _secretText, _scoreText, _toolText;
        private static InputField? _host, _port, _nickname, _room, _hostNickname, _guess;
        private static Font? _font;
        private static bool _visible, _canDraw, _dirty, _cursorSaved;
        private static bool _oldCursorVisible;
        private static CursorLockMode _oldCursorLock;
        private static string _status = "未连接", _detail = "连接服务器，然后创建或加入房间。";
        private static string _scoreboard = "等待加入房间", _drawer = "—", _secretWord = "";
        private static string _brushHex = "202020", _tool = "brush", _strokeToken = "";
        private static long _strokeNumber;
        private static int _brushSize = 4;
        private static bool _dragging, _pending;
        private static Vector2Int _lastSent, _pendingPixel, _lineStart;
        private static float _nextRequestTime;
        private static string _defaultHost = Core.PaintingDefaults.PublicHost, _defaultPort = Core.PaintingDefaults.PublicPort.ToString(CultureInfo.InvariantCulture);
        private static string _defaultNickname = "读取角色名称…", _defaultRoom = "画室", _defaultHostNickname = "";

        public static bool IsActive => _visible;
        public static bool CanDraw => _canDraw;
        public static int UndoCount => UndoBuffers.Count;

        public static void Initialize(Action<string> command)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            Dispose();
            _command = command;
            _automaticBoard = true;
            _freeMode = false;
            _defaultHost = Core.PaintingDefaults.PublicHost;
            _defaultPort = Core.PaintingDefaults.PublicPort.ToString(CultureInfo.InvariantCulture);
            _defaultNickname = "读取角色名称…";
            EnsureBuffer();
        }

        /// <summary>Only connection/room metadata; never put a secret word into public detail.</summary>
        public static void SetState(string status, string detail, bool canDraw)
        {
            _status = BoundText(status, 180);
            _detail = BoundText(detail, 1200);
            _canDraw = canDraw;
            if (!canDraw)
            {
                _secretWord = "";
                CancelPointer();
            }
            RefreshLabels();
        }

        /// <summary>The runtime must supply a secret only in the local drawer's private state.</summary>
        public static void SetRoomState(string scoreboard, string drawer, string? secretWord = null)
        {
            _scoreboard = BoundText(scoreboard, 2400);
            _drawer = BoundText(drawer, 80);
            _secretWord = _canDraw ? BoundText(secretWord, 80) : "";
            RefreshLabels();
        }

        public static void SetConnectionDefaults(string host, int port, string nickname, string room, string hostNickname)
        {
            if (port < 1 || port > 65535) throw new ArgumentOutOfRangeException(nameof(port));
            _defaultHost = BoundText(host, 253);
            _defaultPort = port.ToString(CultureInfo.InvariantCulture);
            _defaultNickname = BoundText(nickname, 32);
            _defaultRoom = BoundText(room, 64);
            _defaultHostNickname = BoundText(hostNickname, 32);
            RefreshServerFields();
            if (_nickname != null) _nickname.text = _defaultNickname;
            if (_room != null) _room.text = _defaultRoom;
            if (_hostNickname != null) _hostNickname.text = _defaultHostNickname;
        }

        public static void Show()
        {
            if (_command == null) return;
            if (_root == null) BuildUI();
            _boardWanted = true;
            EnsureSceneBoard();
            EnsureEventSystem();
            if (!_visible)
            {
                _oldCursorVisible = Cursor.visible;
                _oldCursorLock = Cursor.lockState;
                _cursorSaved = true;
            }
            _visible = true;
            _root!.SetActive(true);
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            RefreshLabels();
            UploadIfDirty();
        }

        public static void Hide()
        {
            CancelPointer();
            if (_root != null)
            {
                EventSystem? events = EventSystem.current;
                var selected = events != null ? events.currentSelectedGameObject : null;
                if (events != null && selected != null && selected.transform.IsChildOf(_root.transform))
                    events.SetSelectedGameObject(null);
                _root.SetActive(false);
            }
            _visible = false;
            RestoreCursor();
            ReleaseOwnedEventSystem();
        }

        public static void Tick()
        {
            if (_command == null) return;
            if (Input.GetKeyDown(KeyCode.F8))
            {
                if (_visible) Hide(); else Show();
            }
            TickSceneBoard();
            if (!_visible) return;
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                Hide();
                return;
            }
            EnsureEventSystem();
            HandlePointer();
            UploadIfDirty();
        }

        /// <summary>Authoritative accepted operations only; never call this from a local input handler.</summary>
        public static void ApplyOperation(string tool, int x1, int y1, int x2, int y2,
            string color, int size, bool newStroke)
        {
            string kind = (tool ?? "").ToLowerInvariant();
            if (kind != "line" && kind != "brush" && kind != "fill" && kind != "clear" && kind != "undo")
                throw new ArgumentException("Unknown drawing operation.", nameof(tool));
            if (kind == "undo")
            {
                EnsureBuffer();
                if (UndoBuffers.Count == 0) return;
                _pixels = UndoBuffers[UndoBuffers.Count - 1];
                UndoBuffers.RemoveAt(UndoBuffers.Count - 1);
                _dirty = true;
                return;
            }
            if (kind == "clear")
            {
                EnsureBuffer();
                if (IsWhite()) return;
                Snapshot();
                Array.Fill(_pixels!, White);
                _dirty = true;
                return;
            }
            CheckCoordinate(x1, y1);
            CheckCoordinate(x2, y2);
            if (size < 1 || size > MaxBrushSize) throw new ArgumentOutOfRangeException(nameof(size));
            Color32 ink = ParseColor(color);
            EnsureBuffer();
            if (kind == "fill")
            {
                Color32 target = _pixels![y1 * CanvasSize + x1];
                if (Same(target, ink)) return;
                Snapshot();
                FloodFill(x1, y1, target, ink);
            }
            else
            {
                if (newStroke) Snapshot();
                RasterLine(x1, y1, x2, y2, ink, size);
            }
            _dirty = true;
        }

        /// <summary>Authoritative round/reset event; resets history without emitting a command.</summary>
        public static void ResetCanvas()
        {
            EnsureBuffer();
            Array.Fill(_pixels!, White);
            UndoBuffers.Clear();
            CancelPointer();
            _dirty = true;
        }

        /// <summary>Detached managed copy for diagnostics, not a network image decoder.</summary>
        public static Color32[] CopyPixels()
        {
            EnsureBuffer();
            return (Color32[])_pixels!.Clone();
        }

        public static void Dispose()
        {
            Hide();
            foreach (Button button in OwnedButtons)
                if (button != null) button.onClick.RemoveAllListeners();
            OwnedButtons.Clear();
            DrawButtons.Clear();
            GuessOnly.Clear();
            _guessModeButton = _freeModeButton = _saveButton = _answerButton = null;
            _choosingTopic = _topicDrawer = _canGuess = false;
            _scoreHeading = _helpText = _canvasHint = _publicServerText = _serverPortLabel = null;
            if (_root != null) Object.Destroy(_root);
            RemoveSceneBoard();
            ClearChannelBoards();
            if (_texture != null) Object.Destroy(_texture);
            // Interop arrays have no IDisposable; release the wrapper/GC handle normally.
            _upload = null;
            _root = null;
            _texture = null;
            _drawRect = null;
            _statusText = _detailText = _drawerText = _secretText = _scoreText = _toolText = null;
            _host = _port = _nickname = _room = _hostNickname = _guess = null;
            _font = null; // Borrowed built-in font, never destroy it.
            _command = null;
            _pixels = null;
            _fillQueue = null;
            UndoBuffers.Clear();
            _dirty = _canDraw = false;
            _secretWord = "";
            _scoreboard = "等待加入房间";
            _drawer = "—";
            _status = "未连接";
            _detail = "连接服务器，然后创建或加入房间。";
            _tool = "brush";
            _brushHex = "202020";
            _brushSize = 4;
            _strokeNumber = 0;
            _nextRequestTime = 0;
        }

        private static void EnsureBuffer()
        {
            if (_pixels != null) return;
            _pixels = new Color32[CanvasSize * CanvasSize];
            Array.Fill(_pixels, White);
            _dirty = true;
        }

        private static void Snapshot()
        {
            if (UndoBuffers.Count == MaxUndoSteps) UndoBuffers.RemoveAt(0);
            UndoBuffers.Add((Color32[])_pixels!.Clone());
        }

        private static void CheckCoordinate(int x, int y)
        {
            if ((uint)x >= CanvasSize || (uint)y >= CanvasSize)
                throw new ArgumentOutOfRangeException(nameof(x), "Drawing coordinates must be 0..511.");
        }

        private static Color32 ParseColor(string hex)
        {
            if (hex == null) throw new ArgumentNullException(nameof(hex));
            if (hex.StartsWith("#", StringComparison.Ordinal)) hex = hex.Substring(1);
            if (hex.Length != 6 || !uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint rgb))
                throw new ArgumentException("Color must be six hexadecimal RGB digits.", nameof(hex));
            return new Color32((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb, 255);
        }

        private static bool Same(Color32 a, Color32 b) => a.r == b.r && a.g == b.g && a.b == b.b && a.a == b.a;
        private static bool IsWhite()
        {
            foreach (Color32 pixel in _pixels!) if (!Same(pixel, White)) return false;
            return true;
        }

        // Integer Bresenham and integer filled discs: identical operation order yields identical buffers.
        private static void RasterLine(int x, int y, int endX, int endY, Color32 ink, int radius)
        {
            int dx = Math.Abs(endX - x), dy = Math.Abs(endY - y);
            int sx = x < endX ? 1 : -1, sy = y < endY ? 1 : -1;
            int error = dx - dy;
            while (true)
            {
                Disc(x, y, radius, ink);
                if (x == endX && y == endY) return;
                int twice = error * 2;
                if (twice > -dy) { error -= dy; x += sx; }
                if (twice < dx) { error += dx; y += sy; }
            }
        }

        private static void Disc(int cx, int cy, int radius, Color32 ink)
        {
            int bottom = Math.Max(0, cy - radius), top = Math.Min(CanvasSize - 1, cy + radius);
            int left = Math.Max(0, cx - radius), right = Math.Min(CanvasSize - 1, cx + radius);
            int radiusSquared = radius * radius;
            for (int y = bottom; y <= top; y++)
                for (int x = left; x <= right; x++)
                {
                    int dx = x - cx, dy = y - cy;
                    if (dx * dx + dy * dy <= radiusSquared) _pixels![y * CanvasSize + x] = ink;
                }
        }

        // Mark before enqueue, so each pixel occupies the fixed queue at most once.
        private static void FloodFill(int x, int y, Color32 target, Color32 ink)
        {
            _fillQueue ??= new int[CanvasSize * CanvasSize];
            int head = 0, tail = 0, start = y * CanvasSize + x;
            _pixels![start] = ink;
            _fillQueue[tail++] = start;
            while (head < tail)
            {
                int index = _fillQueue[head++];
                int px = index % CanvasSize, py = index / CanvasSize;
                if (px > 0) EnqueueFill(index - 1, target, ink, ref tail);
                if (px + 1 < CanvasSize) EnqueueFill(index + 1, target, ink, ref tail);
                if (py > 0) EnqueueFill(index - CanvasSize, target, ink, ref tail);
                if (py + 1 < CanvasSize) EnqueueFill(index + CanvasSize, target, ink, ref tail);
            }
        }

        private static void EnqueueFill(int index, Color32 target, Color32 ink, ref int tail)
        {
            if (!Same(_pixels![index], target)) return;
            _pixels[index] = ink;
            _fillQueue![tail++] = index;
        }

        private static void UploadIfDirty()
        {
            if (!_dirty || _texture == null || _pixels == null) return;
            _upload ??= new Il2CppStructArray<Color32>(CanvasSize * CanvasSize);
            for (int i = 0; i < _pixels.Length; i++) _upload[i] = _pixels[i];
            _texture.SetPixels32(_upload);
            _texture.Apply(false, false);
            _dirty = false;
        }

        private static void CancelPointer()
        {
            _dragging = _pending = false;
            _strokeToken = "";
        }

        private static string NextToken() => (++_strokeNumber).ToString(CultureInfo.InvariantCulture);

        private static bool PixelUnderPointer(out Vector2Int pixel)
        {
            pixel = default;
            if (_drawRect == null || !RectTransformUtility.RectangleContainsScreenPoint(_drawRect, Input.mousePosition, null)) return false;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_drawRect, Input.mousePosition, null, out Vector2 local)) return false;
            Rect rect = _drawRect.rect;
            if (rect.width <= 0 || rect.height <= 0) return false;
            int x = Mathf.Clamp(Mathf.FloorToInt((local.x - rect.xMin) / rect.width * CanvasSize), 0, CanvasSize - 1);
            int y = Mathf.Clamp(Mathf.FloorToInt((local.y - rect.yMin) / rect.height * CanvasSize), 0, CanvasSize - 1);
            pixel = new Vector2Int(x, y);
            return true;
        }

        private static void HandlePointer()
        {
            if (!_canDraw) { CancelPointer(); return; }
            bool inside = PixelUnderPointer(out Vector2Int pixel);
            EventSystem? events = EventSystem.current;
            GameObject? selected = events != null ? events.currentSelectedGameObject : null;
            if (selected != null && selected.GetComponent<InputField>() != null)
            {
                if (inside && Input.GetMouseButtonDown(0)) events!.SetSelectedGameObject(null);
                else { CancelPointer(); return; }
            }
            float now = Time.unscaledTime;
            if (_pending && now >= _nextRequestTime)
            {
                RequestSegment(_lastSent, _pendingPixel, _strokeToken);
                _lastSent = _pendingPixel;
                _pending = false;
            }
            if (Input.GetMouseButtonDown(0) && inside && !_pending && now >= _nextRequestTime)
            {
                _strokeToken = NextToken();
                _lineStart = _lastSent = pixel;
                if (_tool == "fill")
                {
                    RequestDrawing("fill", pixel, pixel, _strokeToken);
                    return;
                }
                _dragging = true;
                if (_tool == "brush") RequestSegment(pixel, pixel, _strokeToken);
            }
            if (!_dragging) return;
            if (inside && _tool == "brush" && pixel != _lastSent)
            {
                _pendingPixel = pixel;
                _pending = true;
                if (now >= _nextRequestTime)
                {
                    RequestSegment(_lastSent, pixel, _strokeToken);
                    _lastSent = pixel;
                    _pending = false;
                }
            }
            if (Input.GetMouseButtonUp(0) || !inside || !Input.GetMouseButton(0))
            {
                if (_tool == "line" && inside) RequestSegment(_lineStart, pixel, _strokeToken);
                _dragging = false;
            }
        }

        private static void RequestSegment(Vector2Int from, Vector2Int to, string token) => RequestDrawing("line", from, to, token);
        private static void RequestDrawing(string tool, Vector2Int from, Vector2Int to, string token)
        {
            if (!_canDraw || !_visible) return;
            _nextRequestTime = Time.unscaledTime + RequestInterval;
            Dispatch(string.Format(CultureInfo.InvariantCulture, "dg draw {0} {1} {2} {3} {4} {5} {6} {7}",
                tool, from.x, from.y, to.x, to.y, "#" + _brushHex, _brushSize, token));
        }

        private static void AtomicRequest(string tool)
        {
            if (!_canDraw || Time.unscaledTime < _nextRequestTime) return;
            CancelPointer();
            RequestDrawing(tool, default, default, NextToken());
        }

        private static void Dispatch(string command)
        {
            try { _command?.Invoke(command); }
            catch (Exception exception)
            {
                _detail = "请求失败：" + BoundText(exception.Message, 180);
                RefreshLabels();
            }
        }

        private static string Token(InputField? input, string name)
        {
            string text = input != null ? input.text.Trim() : "";
            if (text.Length == 0 || text.Length > 253) throw new ArgumentException(name + "不能为空或过长");
            foreach (char ch in text)
                if (char.IsWhiteSpace(ch) || char.IsControl(ch)) throw new ArgumentException(name + "不能包含空格或换行");
            return text;
        }

        private static void Connect()
        {
            try
            {
                if (PublicServerSelected) { Dispatch("dg connect public"); return; }
                string host = Token(_host, "服务器地址"), port = Token(_port, "端口");
                if (!int.TryParse(port, out int number) || number < 1 || number > 65535) throw new ArgumentException("端口须为 1..65535");
                Dispatch("dg connect " + host + " " + number.ToString(CultureInfo.InvariantCulture));
            }
            catch (Exception exception) { _detail = exception.Message; RefreshLabels(); }
        }

        private static void RoomRequest(bool join)
        {
            try
            {
                string room = Token(_room, "房间名");
                string expected = _hostNickname != null ? _hostNickname.text.Trim() : "";
                Dispatch(join ? "dg join " + room + (expected.Length > 0 ? " " + Token(_hostNickname, "房主昵称") : "") : "dg host " + room);
            }
            catch (Exception exception) { _detail = exception.Message; RefreshLabels(); }
        }

        private static void Guess()
        {
            string text = _guess != null ? _guess.text.Trim() : "";
            if (text.Length == 0 || text.Length > 120) { _detail = "答案须为 1..120 个字符。"; RefreshLabels(); return; }
            foreach (char ch in text)
                if (char.IsControl(ch)) { _detail = "答案不能包含换行或控制字符。"; RefreshLabels(); return; }
            bool topic = _choosingTopic && _topicDrawer;
            if (topic && text.Length > 16) { _detail = "题目须为 1–16 个字符。"; RefreshLabels(); return; }
            if ((!topic && !_canGuess) || _freeMode) return;
            Dispatch((topic ? "dg word " : "dg guess ") + text);
            if (_guess != null) _guess.text = "";
        }

        private static void RefreshLabels()
        {
            if (_statusText != null) _statusText.text = (_freeMode ? "自由同步  ·  " : "你画我猜  ·  ") + HidePublicEndpoint(_status);
            if (_detailText != null) _detailText.text = HidePublicEndpoint(_detail);
            if (_drawerText != null) _drawerText.text = "当前画家：" + _drawer;
            if (_secretText != null) _secretText.text = _canDraw ? "你的题目（仅自己可见）\n" + (_secretWord.Length > 0 ? _secretWord : "等待题目") : "观众 / 猜题者\n题目不会发送给猜题者";
            if (_scoreText != null) _scoreText.text = _scoreboard;
            if (_toolText != null) _toolText.text = "工具：" + (_tool == "brush" ? "画笔" : _tool == "line" ? "直线" : "填充") + "   粗细：" + _brushSize;
            foreach (Button button in DrawButtons) if (button != null) button.interactable = _canDraw;
            RefreshModeLabels();
            RefreshTopicControls();
        }

        private static string BoundText(string? text, int maximum)
        {
            if (text == null) return "";
            return text.Length <= maximum ? text : text.Substring(0, maximum);
        }

        private static void EnsureEventSystem()
        {
            if (_ownedEventSystem != null && EventSystem.current != null && EventSystem.current.gameObject != _ownedEventSystem)
                ReleaseOwnedEventSystem();
            if (Object.FindObjectOfType<EventSystem>() != null) return;
            _ownedEventSystem = new GameObject("DrawGuess_OwnedEventSystem");
            Object.DontDestroyOnLoad(_ownedEventSystem);
            _ownedEventSystem.AddComponent<EventSystem>();
            _ownedEventSystem.AddComponent<StandaloneInputModule>();
        }

        private static void ReleaseOwnedEventSystem()
        {
            if (_ownedEventSystem == null) return;
            _ownedEventSystem.SetActive(false);
            Object.Destroy(_ownedEventSystem);
            _ownedEventSystem = null;
        }

        private static void RestoreCursor()
        {
            if (!_cursorSaved) return;
            Cursor.lockState = _oldCursorLock;
            Cursor.visible = _oldCursorVisible;
            _cursorSaved = false;
        }

        private static void BuildUI()
        {
            EnsureTexture();
            _font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            _root = new GameObject("DrawGuess_UI");
            Object.DontDestroyOnLoad(_root);
            Canvas canvas = _root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 22000;
            CanvasScaler scaler = _root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 800);
            scaler.matchWidthOrHeight = 0.5f;
            _root.AddComponent<GraphicRaycaster>();
            GameObject backdrop = Box(_root.transform, "Backdrop", Vector2.zero, Vector2.zero, new Color(0, 0, 0, 0.78f));
            RectTransform backRect = backdrop.GetComponent<RectTransform>();
            backRect.anchorMin = Vector2.zero; backRect.anchorMax = Vector2.one;
            backRect.offsetMin = backRect.offsetMax = Vector2.zero;
            Transform panel = Box(_root.transform, "Panel", Vector2.zero, new Vector2(1220, 760), PanelColor).transform;
            _statusText = Label(panel, "Status", "", new Vector2(-310, 345), new Vector2(540, 42), 19);
            AddModeControls(panel);
            Label(panel, "HostLabel", "服务器", new Vector2(-535, 313), new Vector2(65, 25), 15);
            Button(panel, "公网", new Vector2(-465, 313), new Vector2(60, 24), () => ChooseServer(false));
            Button(panel, "本地", new Vector2(-395, 313), new Vector2(60, 24), () => ChooseServer(true));
            _serverPortLabel = Label(panel, "PortLabel", "端口", new Vector2(-300, 313), new Vector2(100, 25), 15);
            Label(panel, "NameLabel", "角色名称（自动）", new Vector2(-140, 313), new Vector2(190, 25), 15);
            Label(panel, "RoomLabel", "房间名称", new Vector2(65, 313), new Vector2(190, 25), 15);
            Label(panel, "OwnerLabel", "房主名称（选填）", new Vector2(270, 313), new Vector2(190, 25), 15);
            _host = Field(panel, "Host", new Vector2(-465, 280), new Vector2(200, 42), PublicServerSelected ? "" : _defaultHost, 253);
            _port = Field(panel, "Port", new Vector2(-300, 280), new Vector2(100, 42), PublicServerSelected ? "" : _defaultPort, 5);
            _publicServerText = Label(panel, "PublicServer", "已选择公网服务器", new Vector2(-410, 280), new Vector2(310, 42), 18);
            RefreshServerFields();
            _nickname = Field(panel, "Nickname", new Vector2(-140, 280), new Vector2(190, 42), _defaultNickname, 32);
            _nickname.readOnly = true;
            _room = Field(panel, "Room", new Vector2(65, 280), new Vector2(190, 42), _defaultRoom, 64);
            _hostNickname = Field(panel, "HostNickname", new Vector2(270, 280), new Vector2(190, 42), _defaultHostNickname, 32);
            Button(panel, "连接", new Vector2(475, 280), new Vector2(190, 42), Connect);
            Button(panel, "创建房间", new Vector2(-490, 225), new Vector2(150, 40), () => RoomRequest(false));
            Button(panel, "Alt＋点击加入", new Vector2(-325, 225), new Vector2(150, 40), () => { _detail = "关闭面板后，按住 Alt 点击房主创建并标有游戏房间的画架加入。"; RefreshLabels(); Hide(); });
            Button(panel, "离开房间", new Vector2(-160, 225), new Vector2(150, 40), () => Dispatch("dg leave"));
            GuessOnly.Add(Button(panel, "开始游戏", new Vector2(5, 225), new Vector2(150, 40), () => Dispatch("dg start")).gameObject);
            GuessOnly.Add(Button(panel, "下一轮", new Vector2(170, 225), new Vector2(150, 40), () => Dispatch("dg next")).gameObject);
            Button(panel, "放置画架", new Vector2(335, 225), new Vector2(150, 40), () => Dispatch("dg board"));
            Button(panel, "隐藏 (F8)", new Vector2(500, 225), new Vector2(140, 40), () => { Hide(); Dispatch("dg hide"); });
            _detailText = Label(panel, "Detail", "", new Vector2(-440, 112), new Vector2(270, 150), 18);
            _detailText.alignment = TextAnchor.UpperLeft;
            _drawerText = Label(panel, "Drawer", "", new Vector2(-440, 8), new Vector2(270, 44), 19);
            _secretText = Label(panel, "PrivateWord", "", new Vector2(-440, -67), new Vector2(270, 100), 20);
            Button(panel, "画笔", new Vector2(-525, -145), new Vector2(80, 40), () => SelectTool("brush"));
            Button(panel, "直线", new Vector2(-435, -145), new Vector2(80, 40), () => SelectTool("line"));
            Button(panel, "填充", new Vector2(-345, -145), new Vector2(80, 40), () => SelectTool("fill"));
            _toolText = Label(panel, "ToolInfo", "", new Vector2(-440, -190), new Vector2(270, 34), 18);
            Button(panel, "细 −", new Vector2(-510, -232), new Vector2(120, 38), () => { _brushSize = Math.Max(1, _brushSize - 1); RefreshLabels(); });
            Button(panel, "粗 +", new Vector2(-370, -232), new Vector2(120, 38), () => { _brushSize = Math.Min(MaxBrushSize, _brushSize + 1); RefreshLabels(); });
            DrawButtons.Add(Button(panel, "撤销", new Vector2(-510, -280), new Vector2(120, 38), () => AtomicRequest("undo")));
            DrawButtons.Add(Button(panel, "清空", new Vector2(-370, -280), new Vector2(120, 38), () => AtomicRequest("clear")));
            string[] colors = { "202020", "EA4335", "34A853", "4285F4", "FBBC05", "FF8C00", "983CD9", "FFFFFF" };
            for (int i = 0; i < colors.Length; i++)
            {
                string hex = colors[i];
                Button swatch = Button(panel, "", new Vector2(-545 + (i % 4) * 70, -328 - (i / 4) * 35), new Vector2(58, 29), () => { _brushHex = hex; });
                swatch.GetComponent<Image>().color = ParseColor(hex);
            }
            Box(panel, "CanvasBorder", new Vector2(0, -65), new Vector2(520, 520), new Color(0.3f, 0.4f, 0.5f, 1));
            GameObject drawing = new GameObject("DrawingCanvas");
            drawing.transform.SetParent(panel, false);
            _drawRect = drawing.AddComponent<RectTransform>();
            _drawRect.anchorMin = _drawRect.anchorMax = _drawRect.pivot = new Vector2(0.5f, 0.5f);
            _drawRect.anchoredPosition = new Vector2(0, -65);
            _drawRect.sizeDelta = new Vector2(512, 512);
            RawImage image = drawing.AddComponent<RawImage>();
            image.texture = _texture;
            _canvasHint = Label(panel, "CanvasHint", "", new Vector2(0, -349), new Vector2(520, 40), 17);
            _scoreHeading = Label(panel, "ScoreHeading", "房间与积分榜", new Vector2(440, 172), new Vector2(270, 36), 22);
            _scoreText = Label(panel, "Scoreboard", "", new Vector2(440, 48), new Vector2(270, 208), 18);
            _scoreText.alignment = TextAnchor.UpperLeft;
            _guess = Field(panel, "Guess", new Vector2(440, -112), new Vector2(270, 46), "", 120);
            GuessOnly.Add(_guess.gameObject);
            _answerButton = Button(panel, "提交答案", new Vector2(440, -169), new Vector2(270, 44), Guess);
            GuessOnly.Add(_answerButton.gameObject);
            _helpText = Label(panel, "Help", "轮到你时使用左侧工具绘画。\n其他玩家在右侧提交答案。\n\nF8：显示 / 隐藏\nEsc：隐藏面板\nAlt＋点击画架：打开\n放置画架：移到玩家前方\n隐藏后场景画板持续同步。", new Vector2(440, -279), new Vector2(270, 158), 17);
            _root.SetActive(false);
            RefreshLabels();
        }

        private static void SelectTool(string tool)
        {
            CancelPointer();
            _tool = tool;
            RefreshLabels();
        }

        private static GameObject Box(Transform parent, string name, Vector2 position, Vector2 size, Color color)
        {
            var obj = new GameObject(name);
            obj.transform.SetParent(parent, false);
            var rect = obj.AddComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            obj.AddComponent<Image>().color = color;
            return obj;
        }

        private static Text Label(Transform parent, string name, string content, Vector2 position, Vector2 size, int fontSize)
        {
            var obj = new GameObject(name);
            obj.transform.SetParent(parent, false);
            Text text = obj.AddComponent<Text>();
            text.font = _font;
            text.fontSize = fontSize;
            text.color = new Color(0.91f, 0.94f, 0.98f, 1f);
            text.alignment = TextAnchor.MiddleCenter;
            text.supportRichText = false;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.text = content;
            RectTransform rect = obj.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            return text;
        }

        private static Button Button(Transform parent, string label, Vector2 position, Vector2 size, Action action)
        {
            GameObject obj = Box(parent, "Button_" + label, position, size, ButtonColor);
            Button button = obj.AddComponent<Button>();
            button.targetGraphic = obj.GetComponent<Image>();
            button.onClick.AddListener(new Action(action));
            OwnedButtons.Add(button);
            Text text = Label(obj.transform, "Label", label, Vector2.zero, size, 18);
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = 12;
            text.resizeTextMaxSize = 18;
            return button;
        }

        private static InputField Field(Transform parent, string name, Vector2 position, Vector2 size, string initial, int limit)
        {
            GameObject obj = Box(parent, name, position, size, new Color(0.12f, 0.16f, 0.23f, 1f));
            Text text = Label(obj.transform, "Text", initial, Vector2.zero, new Vector2(size.x - 16, size.y - 4), 18);
            text.alignment = TextAnchor.MiddleLeft;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            InputField input = obj.AddComponent<InputField>();
            input.targetGraphic = obj.GetComponent<Image>();
            input.textComponent = text;
            input.characterLimit = limit;
            input.lineType = InputField.LineType.SingleLine;
            input.text = initial;
            return input;
        }
    }
}
