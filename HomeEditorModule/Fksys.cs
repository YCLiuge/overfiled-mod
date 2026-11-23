using mod;
using Il2CppInutan;
using Il2CppKinematicCharacterController;
using Il2CppPb;
using MelonLoader;
using System.Collections;

namespace HomeEditorModule.Core
{
    using Il2CppTMPro;
    using UnityEngine;
    using UnityEngine.UI;

    internal class Fksys
    {
        public static Fksys i = new Fksys();
        Fksys() { if (i == null) i = this; }

        public static bool use = false;
        float movestep = 1.14f;
        float rotateStep = 90f; // 每次旋转 90 度

        // 存储当前绝对朝向角度（Y 轴旋转）
        private float _currentYaw = 0f;
        Vector3 _bzPos;
        Vector3 _rbzPos;
        float keycd = 0.1f;
        float mrcd = 0.1f;
        // 移动目标（默认玩家，可外部赋值）
        public Transform moveTarget;

        public void start()
        {

            if (TipMsgCore.i.mainplayer != null)
                moveTarget = TipMsgCore.i.mainplayer.transform;

            // 标准化玩家位置到最近网格点
            var player = TipMsgCore.i.mainplayer;
            if (player != null)
            {
                Vector3 pos = player.transform.position;
                float step = movestep;
                float newX = Mathf.Round(pos.x / step) * step;
                float newZ = Mathf.Round(pos.z / step) * step;
                float newY = Mathf.Round(pos.y / step) * step; ;
                _bzPos = new Vector3(newX, newY, newZ);
                try
                {
                    player.SetPosition(_bzPos);
                }
                catch
                {
                    player.transform.position = _bzPos;
                }
                stc.Log($"玩家位置已标准化到网格点: {_bzPos}");
            }

            // 初始化朝向为 0（面向 Z 正方向？可根据游戏调整）
            _currentYaw = 0f;
            var motor = player?.GetComponent<KinematicCharacterMotor>();
            if (motor != null)
                motor.SetRotation(Quaternion.Euler(0, _currentYaw, 0));
            Fk_fanction.start();
            CenterTMPManager.Initialize();
        }
        bool user = false;
        float R=1;
        int Rn=0;
        float A=0;
        int An=0;
        public void updata(string message)
        {
            FreeSetHomeItem.i.canfly = false;
            use = true;
            if (message.Length == 0 && Input.GetKeyDown(KeyCode.F6))
                FkCamera.SetEnabled(!FkCamera.Active);
            FkCamera.Tick();
            if(user)HandleRotement();
            else HandleMovement();
            string[] msg = message.Split(' ');
            switch (msg[0])
            {
                case "-cam":
                    if (msg.Length == 1) FkCamera.SetEnabled(!FkCamera.Active);
                    else if (msg.Length == 2 && (msg[1] == "on" || msg[1] == "off"))
                        FkCamera.SetEnabled(msg[1] == "on");
                    else TipMsgCore.i.Send("用法：-cam [on|off]");
                    break;
                case "use":
                    //EditorRuntime.Start(Fk_fanction.putAllitems());
                    break;
                case "setstep":

                    if (msg.Length == 2 && float.TryParse(msg[1], out var a)) movestep = a;
                    break;
                case "setrostep":

                    if (msg.Length == 2 && float.TryParse(msg[1], out var b)) rotateStep = b;
                    break;
                // 其他指令...
                case "setbp":
                    if (msg.Length == 4 && float.TryParse(msg[1], out var a1) && float.TryParse(msg[2], out var a2) && float.TryParse(msg[3], out var a3)) {
                        _bzPos = new Vector3(a1, a2, a3);
                        }
                    break;
                case "sq"://r a
                    if (msg.Length == 3)
                    {
                        if (float.TryParse(msg[1], out var b1)) { R = b1; }
                        if (float.TryParse(msg[2], out var b2)) { A = b2; }
                        _rbzPos = _bzPos;
                        user = true;
                    }
                    else
                    {
                        _bzPos = _rbzPos;
                        user = false;
                    }
                        break;
            }
            bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            float scroll = Input.GetAxis("Mouse ScrollWheel");
            if (ctrl && scroll != 0f) 
            {
                movestep += scroll * 0.2f;
                CenterTMPManager.SetText("步长:" + movestep.ToString("0.00"));
            }
            bool shift = Input.GetKey(KeyCode.LeftShift);
            if (shift && scroll != 0f)
            {
                rotateStep += (int)(scroll * 50);
                CenterTMPManager.SetText("旋转角度:" + rotateStep.ToString("0.00"));
            }
            Fk_fanction.updata();
        }

        private void HandleMovement()
        {
            if (moveTarget == null)
            {
                if (TipMsgCore.i.mainplayer != null)
                    moveTarget = TipMsgCore.i.mainplayer.transform;
                else
                    return;
            }

            bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

            var player = TipMsgCore.i.mainplayer;
            KinematicCharacterMotor motor = null;
            if (player != null)
                motor = player.GetComponent<KinematicCharacterMotor>();

            // ---------- Shift + WASD 控制绝对朝向 ----------
            if (shift && motor != null)
            {
                // 每次按键旋转 90 度
                if (Input.GetKeyDown(KeyCode.W) || Input.GetKeyDown(KeyCode.A))
                    _currentYaw -= rotateStep; // 左转 90°
                if (Input.GetKeyDown(KeyCode.S) || Input.GetKeyDown(KeyCode.D))
                    _currentYaw += rotateStep; // 右转 90°

            }
            motor.SetRotation(Quaternion.Euler(0, _currentYaw, 0));


            // ---------- 常规移动（非 Shift） ----------
            float step = movestep;
            Vector3 move = Vector3.zero;

            // 计算当前朝向的方向向量
            Quaternion rotation = Quaternion.Euler(0, _currentYaw, 0);
            Vector3 forward = rotation * Vector3.forward;
            Vector3 right = rotation * Vector3.right;

            if (keycd <= 0)
            {
                if (Input.GetKey(KeyCode.W))
                {
                    if (ctrl)
                        move.y += step;          // 上（世界Y轴）
                    else
                        move += forward * step;  // 向前（朝向方向）
                    keycd = mrcd;
                }
                if (Input.GetKey(KeyCode.S))
                {
                    if (ctrl)
                        move.y -= step;          // 下（世界Y轴）
                    else
                        move -= forward * step;  // 向后
                    keycd = mrcd;
                }
                if (Input.GetKey(KeyCode.A))
                { move -= right * step; keycd = mrcd; }      // 向左
                if (Input.GetKey(KeyCode.D))
                { move += right * step; keycd = mrcd; } // 向右
            }
            else
            {
                keycd -= Time.deltaTime;
            }

            if (shift) move = Vector3.zero;
            var player2 = TipMsgCore.i.mainplayer;
            if (moveTarget == player2?.transform)
            {
                _bzPos = _bzPos + move;
                player2.SetPosition(_bzPos);
            }
            else
            {
                moveTarget.position += move;
            }

        }
        private void HandleRotement()
        {
            var player = TipMsgCore.i.mainplayer;
            if (player == null) return;
            var motor = player.GetComponent<KinematicCharacterMotor>();
            if (motor == null) return;

            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.LeftControl);
            // 【修改】如果按住 Shift 平移，方向以“世界坐标”为准；否则以“当前朝向”为准（虽然不按Shift时不用方向，但留着备用）
            Vector3 forward = shift ? Vector3.forward : (Quaternion.Euler(0, A * An, 0) * Vector3.forward);
            Vector3 right = shift ? Vector3.right : (Quaternion.Euler(0, A * An, 0) * Vector3.right);

            if (Input.GetKeyDown(KeyCode.W))
            {
                if (shift) _rbzPos += forward * movestep;
                else if (ctrl) _rbzPos += Vector3.up;
                else Rn++;
            }
            if (Input.GetKeyDown(KeyCode.S))
            {
                if (shift) _rbzPos -= forward * movestep;
                else if (ctrl) _rbzPos += Vector3.down;
                else Rn--;
            }
            if (Input.GetKeyDown(KeyCode.A))
            {
                if (shift) _rbzPos -= right * movestep;
                else if (ctrl) _currentYaw -= rotateStep;
                else An--;
            }
            if (Input.GetKeyDown(KeyCode.D))
            {
                if (shift) _rbzPos += right * movestep;
                else if (ctrl) _currentYaw += rotateStep;
                else An++;
            }

            // 统一重算坐标（完美）
            _bzPos = GetPointOnCircle(_rbzPos, R + (Rn * movestep), A * An);

            // 强制瞬移
            
            if (shift)
            {
                motor.SetPositionAndRotation(_rbzPos, Quaternion.Euler(0, _currentYaw, 0));
            }
            else
            {
                motor.SetPositionAndRotation(_bzPos, Quaternion.Euler(0, (A * An) + _currentYaw, 0));
            }
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                _bzPos = _rbzPos;
                user = false;
            }
        }
        public static Vector3 GetPointOnCircle(Vector3 center, float radius, float angle)
        {
            // 将角度转换为弧度
            float rad = angle * Mathf.Deg2Rad;

            // 计算X和Z的偏移量
            float x = Mathf.Sin(rad) * radius;
            float z = Mathf.Cos(rad) * radius;

            // 返回中心点加上偏移量（Y轴高度保持与中心点一致）
            return center + new Vector3(x, 0, z);
        }
    }

    public static class Fk_fanction
    {
        static List<List<Itempot>> tempitems = new();
        static List<Itempot> showitems = new();
        static int index = 0;
        static int tempId = 5040015;
        static int carrytableid;
        static ms Ms = ms.pot;
        static Vector3 startpot;
        static Vector3 endpot;
        static GameObject root;
        enum ms
        {
            pot,
            line,
            pice,
            box,
        }
        public static void start()
        {
            Clear();
            root = GameObject.Find("放置网格/PlacementGrid_9999(Clone)");
            index = 0;
        }
        public static void Clear()
        {
            tempitems.Clear();
        }
        public static void updata()
        {

            if (IHomeSystem.sys.carryingItem != null && carrytableid != IHomeSystem.sys.carryingItem.tableId)
            {
                clearshowfk();
                carrytableid = IHomeSystem.sys.carryingItem.tableId;
            }
            if (Input.GetKeyDown(KeyCode.G))
            {
                UIHomeSystem.Instance.OnClickPutdown(null);
            }
            if (Input.GetKeyDown(KeyCode.P))
            {
                if (Ms != ms.pot)
                {
                    startpot = TipMsgCore.i.mainplayer.transform.position;
                    Ms = ms.pot;
                }
            }
            if (Input.GetKeyDown(KeyCode.L))
            {
                if (Ms != ms.line)
                {
                    startpot = TipMsgCore.i.mainplayer.transform.position;
                    Ms = ms.line;
                }
            }
            if (Input.GetKeyDown(KeyCode.K))
            {
                if (Ms != ms.pice)
                {
                    startpot = TipMsgCore.i.mainplayer.transform.position;
                    Ms = ms.pice;
                }
            }
            if (Input.GetKeyDown(KeyCode.M))
            {
                if (Ms != ms.box)
                {
                    startpot = TipMsgCore.i.mainplayer.transform.position;
                    Ms = ms.box;
                }
            }
            if (Input.GetKeyDown(KeyCode.B))
            {
                AutoHomeItem.i.Copyitems_part();
            }
            if (Input.GetKeyDown(KeyCode.N))
            {
                EditorRuntime.Start(AutoHomeItem.i.Pasteitems(AutoHomeItem.i.items, true, true));
                stc.Log("NNNNN");
            }

        }
        static void clearshowfk()
        {
            foreach (var a in showitems)
            {
               // GameObject.Destroy(a?.g);
            }
            showitems.Clear();
        }
        static void post_showfk(int num)
        {
            while (num < showitems.Count)
            {
                PutDown_fk_temp((uint)carrytableid, Vector3.zero, Vector3.zero);
            }
            for (int i = 0; i < showitems.Count; i++)
            {
                //if (i < num) { showitems[i].g.SetActive(true); }
                //else { showitems[i].g.SetActive(false); }
            }
        }
        static void draw_Line(Vector3 sta, Vector3 end, int stanum, int endnum)
        {
            for (int i = stanum; i < endnum; i++)
            {

            }
        }
        static void show_to_temp()
        {
            if (showitems.Count > 0)
            {
                tempitems.Add(showitems);
                showitems.Clear();
            }
        }
        static void temptostatic()
        {
            foreach (var a in tempitems)
            {
                EditorRuntime.Start(AutoHomeItem.i.Pasteitems(a, false, true));
            }
        }
        public static IEnumerator PutDown_fk_temp(uint ItemId, Vector3 pos, Vector3 rot)
        {
            var info = new FurnitureDetailsInfo()
            {
                FurnitureItemId = ItemId,
                FurnitureId = tempId++,
                Pos = new Il2CppPb.Vector3(),
                Rotation = new Il2CppPb.Vector3()
            };
            // 调用 CreateAndPutDown 进行“临时放置”
            IHomeSystem.sys.CreateAndPutDown(info, 0);
            while (true)
            {
                if (IHomeSystem.sys.allFurnitures.TryGetValue(info.FurnitureId, out var item))
                {
                    item.SetPosRot(pos, rot.y);
                    showitems.Add(new(item.gameObject));
                    break;
                }

                yield return null;
            }
        }
    }


    public static class CenterTMPManager
    {
        private static TextMeshProUGUI _tmpText;
        private static object _hideCoroutine;
        private static bool _initialized = false;

        /// <summary>
        /// 初始化屏幕中心 TMP 文本，从游戏现有 UI 获取字体样式
        /// </summary>
        public static void Reset() { _initialized = false; _tmpText = null; _hideCoroutine = null; }
        public static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;

            // 1. 创建 Canvas
            GameObject canvasGO = new GameObject("CenterTMPCanvas");
            EditorRuntime.Own(canvasGO);
            Canvas canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasGO.AddComponent<CanvasScaler>();
            canvasGO.AddComponent<GraphicRaycaster>();

            // 2. 创建 TMP 文本
            GameObject textGO = new GameObject("CenterTMPText");
            textGO.transform.SetParent(canvasGO.transform, false);

            // 3. RectTransform 居中
            RectTransform rect = textGO.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(800, 150);
            rect.anchoredPosition = Vector2.zero;

            // 4. 添加 TMP 组件
            _tmpText = textGO.AddComponent<TextMeshProUGUI>();
            _tmpText.text = "";
            _tmpText.color = Color.white;
            _tmpText.alignment = TextAlignmentOptions.Center;

            // ---------- 从指定游戏对象获取字体和字号 ----------
            TMP_FontAsset font = null;
            float fontSize = 48;

            try
            {
                // 查找目标对象（参考你的代码）
                var root = GameObject.Find("[UIMainRoot]/UI_Root_Title(Clone)/Canvas/StateRoot/TitleParent/Root").transform.GetChild(0).FindChild("ActiveRoot/DisplayRoot/OffsetRoot/StateRoot/名字文字");//TipMsgCore.i.players[TipMsgCore.i.mainplayername]?.transform.Find("ActiveRoot/DisplayRoot/OffsetRoot/StateRoot/名字文字");
                if (root != null)
                {
                    var srcTMP = root.GetComponent<TextMeshProUGUI>();
                    if (srcTMP != null)
                    {
                        font = srcTMP.font;
                        fontSize = srcTMP.fontSize;
                        MelonLogger.Msg($"从游戏 UI 获取 TMP 字体: {font?.name}, 字号: {fontSize}");
                    }
                    else
                    {
                        MelonLogger.Warning("目标对象上没有 TextMeshProUGUI 组件，使用默认设置");
                    }
                }
                else
                {
                    MelonLogger.Warning("未找到目标对象，使用默认字体");
                }
            }
            catch (System.Exception ex)
            {
                MelonLogger.Warning($"获取字体时出错: {ex.Message}，使用默认设置");
            }

            // 备选：如果获取失败，尝试从场景其他 TMP 复制
            if (font == null)
            {
                var existingTMP = Object.FindObjectOfType<TextMeshProUGUI>();
                if (existingTMP != null)
                {
                    font = existingTMP.font;
                    fontSize = existingTMP.fontSize;
                    MelonLogger.Msg($"从场景其他 TMP 复制字体: {font?.name}");
                }
            }

            // 最终备选：加载内置字体（如果存在）
            if (font == null)
            {
                font = Resources.Load<TMP_FontAsset>("TMP Default Font");
                if (font == null)
                    MelonLogger.Warning("无法找到任何 TMP 字体，文本可能无法显示");
            }

            _tmpText.font = font;
            _tmpText.fontSize = fontSize;

            // 5. 初始隐藏
            _tmpText.gameObject.SetActive(false);

            // 6. 跨场景不销毁
            Object.DontDestroyOnLoad(canvasGO);

            MelonLogger.Msg("屏幕中心 TMP 文本已初始化完成");
        }

        /// <summary>
        /// 显示文本 1 秒后自动消失
        /// </summary>
        public static void SetText(string content)
        {
            if (!_initialized)
            {
                MelonLogger.Warning("CenterTMPManager 未初始化，请先调用 Initialize()");
                return;
            }
            if (_tmpText == null) return;

            // 停止之前的隐藏协程
            if (_hideCoroutine != null)
            {
                EditorRuntime.Stop(_hideCoroutine);
                _hideCoroutine = null;
            }

            _tmpText.text = content;
            _tmpText.gameObject.SetActive(true);

            _hideCoroutine = EditorRuntime.Start(HideTextAfterDelay());
        }

        private static IEnumerator HideTextAfterDelay()
        {
            yield return new WaitForSeconds(1f);
            if (_tmpText != null)
            {
                _tmpText.text = "";
                _tmpText.gameObject.SetActive(false);
            }
            _hideCoroutine = null;
        }
    }
}