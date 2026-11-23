using Chat.Client;
using Il2Cpp;
using Il2CppInutan;
using MelonLoader;
using mod.tool;

using UnityEngine;

namespace mod.st
{
    /// <summary>
    /// 船式控制器（单例，基于平面水面）
    /// Center 是 List<Transform> 的父对象
    /// </summary>
    public class BoatShapeController
    {
        public static BoatShapeController Instance { get; private set; } = new BoatShapeController();
        public static GameObject boat;
        // ================== 状态 ==================
        public bool IsActive { get; private set; } = false;

        // ================== 中心与形状 ==================
        public Transform Center;
        public List<Transform> ShapePoints = new List<Transform>();
        private List<Vector3> _baseLocalPositions = new List<Vector3>();

        // ================== 推进参数 ==================
        public float MaxForwardSpeed = 100f;
        public float MaxReverseSpeed = 4f;
        public float Acceleration = 5f;
        public float Deceleration = 10f;
        public float WaterDrag = 1.5f;

        // ================== 转向参数 ==================
        public float TurnSpeedAtFull = 60f;
        public float TurnSpeedAtStop = 5f;
        public float TurnSmooth = 3f;

        // ================== 浮力参数 ==================
        public float WaterLevel = -7f;              // 水面 Y 高度（由平面决定）
        public float BuoyancyForce = 6f;
        public float BobAmplitude = 0.15f;
        public float BobFrequency = 1.5f;

        // ================== 姿态参数 ==================
        public float BankAngleMax = 15f;
        public float PitchAngleMax = 5f;
        public float AttitudeSmooth = 4f;

        // ================== 变形参数 ==================
        public bool EnableDeform = false;
        public float DeformBowStrength = 0.3f;
        public float DeformWaveStrength = 0.1f;
        public float DeformWaveSpeed = 2f;

        // ================== 输入 ==================
        public KeyCode ForwardKey = KeyCode.W;
        public KeyCode BackwardKey = KeyCode.S;
        public KeyCode LeftKey = KeyCode.A;
        public KeyCode RightKey = KeyCode.D;
        public KeyCode BrakeKey = KeyCode.Space;

        // ================== 内部状态 ==================
        private float _currentSpeed = 0f;
        private float _currentTurn = 0f;
        private float _bobPhase = 0f;
        private float _currentBank = 0f;
        private float _currentPitch = 0f;
        private float _deformPhase = 0f;

        // ================== 激活 ==================
        public void Activate(Transform center)
        {
            if (center == null)
            {
                MelonLogger.Error("Center 为空");
                return;
            }

            Center = center;
            ShapePoints.Clear();
            _baseLocalPositions.Clear();

            for (int i = 0; i < center.childCount; i++)
            {
                Transform child = center.GetChild(i);
                ShapePoints.Add(child);
                _baseLocalPositions.Add(child.localPosition);
            }

            _currentSpeed = 0f;
            _currentTurn = 0f;
            _currentBank = 0f;
            _currentPitch = 0f;
            _bobPhase = 0f;
            _deformPhase = 0f;

            IsActive = true;
            MelonLogger.Msg($"船控制器已激活: {center.name}, 形状点数: {ShapePoints.Count}");

            
            List<Net_BoatData> net_s = new List<Net_BoatData>();
            foreach( var da in AutoHomeItem.i.items)
            {
                net_s.Add(da);
            }
            NetChater.Instance.Send(EncodeBinary(net_s));
        }

        public void Deactivate()
        {
            IsActive = false;
            Center = null;
            ShapePoints.Clear();
            _baseLocalPositions.Clear();
            MelonLogger.Msg("船控制器已关闭");
            NetChater.Instance.Send("boat stop");
        }

        public void Toggle(Transform center)
        {
            if (IsActive) Deactivate();
            else Activate(center);
        }

        // ================== 每帧更新 ==================
        public void OnUpdate()
        {
            if (!IsActive) return;
            if (Center == null || Center.gameObject == null)
            {
                MelonLogger.Warning("Center 已销毁，自动关闭");
                Deactivate();
                return;
            }
            TipMsgCore.i.mainplayer.SetPosition(Center.position);
            TipMsgCore.i.mainplayer.SetRotation(Center.rotation);
            HandleInput();
            UpdateMovement();
            UpdateAttitude();
            UpdateBuoyancy();
            UpdateShapeDeform();
        }

        private void HandleInput()
        {
            float throttle = 0f;
            if (Input.GetKey(ForwardKey)) throttle += 1f;
            if (Input.GetKey(BackwardKey)) throttle -= 1f;

            if (Input.GetKey(BrakeKey))
            {
                _currentSpeed = Mathf.MoveTowards(_currentSpeed, 0f, Deceleration * 3f * Time.deltaTime);
            }
            else
            {
                if (throttle != 0f)
                {
                    float targetSpeed = throttle > 0f ? MaxForwardSpeed : -MaxReverseSpeed;
                    _currentSpeed = Mathf.MoveTowards(_currentSpeed, targetSpeed, Acceleration * Time.deltaTime);
                }
                else
                {
                    _currentSpeed = Mathf.MoveTowards(_currentSpeed, 0f, WaterDrag * Time.deltaTime);
                }
            }

            float turnInput = 0f;
            if (Input.GetKey(LeftKey)) turnInput -= 1f;
            if (Input.GetKey(RightKey)) turnInput += 1f;

            float speedRatio = Mathf.Abs(_currentSpeed) / MaxForwardSpeed;
            float turnSpeed = Mathf.Lerp(TurnSpeedAtStop, TurnSpeedAtFull, speedRatio);
            float targetTurn = turnInput * turnSpeed;
            _currentTurn = Mathf.Lerp(_currentTurn, targetTurn, TurnSmooth * Time.deltaTime);
        }

        private void UpdateMovement()
        {
            Center.Rotate(Vector3.up, _currentTurn * Time.deltaTime, Space.World);
            Center.position += Center.forward * _currentSpeed * Time.deltaTime;
        }

        private void UpdateAttitude()
        {
            float targetBank = -_currentTurn / TurnSpeedAtFull * BankAngleMax;
            float accelRatio = _currentSpeed / MaxForwardSpeed;
            float targetPitch = accelRatio * PitchAngleMax;

            _currentBank = Mathf.Lerp(_currentBank, targetBank, AttitudeSmooth * Time.deltaTime);
            _currentPitch = Mathf.Lerp(_currentPitch, targetPitch, AttitudeSmooth * Time.deltaTime);

            Vector3 euler = Center.eulerAngles;
            Center.rotation = Quaternion.Euler(_currentPitch, euler.y, _currentBank);
        }

        private void UpdateBuoyancy()
        {
            _bobPhase += BobFrequency * Time.deltaTime;
            float bob = Mathf.Sin(_bobPhase) * BobAmplitude;

            Vector3 pos = Center.position;
            float targetY = WaterLevel + bob;
            pos.y = Mathf.Lerp(pos.y, targetY, BuoyancyForce * Time.deltaTime);
            Center.position = pos;
        }

        private void UpdateShapeDeform()
        {
            if (!EnableDeform || ShapePoints.Count == 0) return;

            _deformPhase += DeformWaveSpeed * Time.deltaTime;

            for (int i = 0; i < ShapePoints.Count; i++)
            {
                var p = ShapePoints[i];
                if (p == null) continue;

                Vector3 baseLocal = _baseLocalPositions[i];
                Vector3 offset = Vector3.zero;

                float forwardDistance = baseLocal.z;
                offset.y += forwardDistance * DeformBowStrength * (_currentSpeed / MaxForwardSpeed);

                float wave = Mathf.Sin(forwardDistance * 2f - _deformPhase) * DeformWaveStrength;
                offset.y += wave;

                p.localPosition = baseLocal + offset;
            }
        }

        // ================== 外部接口 ==================
        public float GetSpeed() => _currentSpeed;
        public float GetSpeedKmh() => _currentSpeed * 3.6f;
        public void SetWaterLevel(float level) => WaterLevel = level;
        public void SetMaxSpeed(float speed) => MaxForwardSpeed = speed;
        public void SetAcceleration(float accel) => Acceleration = accel;
        private const string PREFIX = "boatB%";

        /// <summary>编码：List<Net_BoatData> → Base64 字符串</summary>
        public static string EncodeBinary(List<Net_BoatData> list)
        {
            if (list == null || list.Count == 0) return PREFIX;

            using (var ms = new MemoryStream())
            using (var bw = new BinaryWriter(ms))
            {
                bw.Write((byte)1);                      // 版本号
                bw.Write((short)list.Count);            // 数量（short 最大 32767）

                foreach (var d in list)
                {
                    bw.Write((int)Math.Round(d.pos.x * 100));   // ×100 = 厘米精度
                    bw.Write((int)Math.Round(d.pos.y * 100));
                    bw.Write((int)Math.Round(d.pos.z * 100));
                    bw.Write((short)Math.Round(d.rot.y * 1000)); // ×1000 = 毫弧度，只存 yaw
                    bw.Write((int)d.id);
                }

                return PREFIX + Convert.ToBase64String(ms.ToArray());
            }
        }

        /// <summary>解码：Base64 字符串 → List<Net_BoatData></summary>
        public static List<Net_BoatData> DecodeBinary(string text)
        {
            var result = new List<Net_BoatData>();
            if (string.IsNullOrEmpty(text) || !text.StartsWith(PREFIX)) return result;

            try
            {
                byte[] bytes = Convert.FromBase64String(text.Substring(PREFIX.Length));
                using (var ms = new MemoryStream(bytes))
                using (var br = new BinaryReader(ms))
                {
                    byte version = br.ReadByte();
                    short count = br.ReadInt16();        // ★ 用 ReadInt16

                    for (int i = 0; i < count; i++)
                    {
                        int px = br.ReadInt32();
                        int py = br.ReadInt32();
                        int pz = br.ReadInt32();
                        short ry = br.ReadInt16();       // ★ 用 ReadInt16
                        int id = br.ReadInt32();

                        result.Add(new Net_BoatData(new Net_Vector3(px / 100f, py / 100f, pz / 100f),new Net_Vector3(0f, ry / 1000f, 0f), id));
                        
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLoader.MelonLogger.Error($"[BoatCodec] 解码失败: {ex.Message}");
            }

            return result;
        }
        //网络事件
        public static void Req(ChatMessage message)
        {
            if(message.Text== "boat stop")
            {
                if (NetPlayer.Instance.players.TryGetValue(message.From, out var p)) 
                {
                    var b = p.transform.Find(message.From + "boat");
                    if (b != null) 
                    { 
                        b.transform.SetParent(null);
                        NetPlayer.DestroySmoothly(b.gameObject);
                    }
                }
            }
            if (message.Text.Split('%').Length == 2) 
            {
                var list = DecodeBinary(message.Text);
                if (list == null) 
                {
                    stc.Log("boat指令解析失败");
                    return; 
                }
                stc.Log("boat指令解析1");
                var g = new GameObject(message.From + "boat");
                if (NetPlayer.Instance.players.TryGetValue(message.From, out var p))
                {
                    g.transform.SetParent(p.transform);
                    NetPlayer.Instance.FollowplayersList[message.From].Add(g);
                    int i = 0;
                    foreach (var item in list)
                    {
                        var info = new Il2CppPb.FurnitureDetailsInfo()
                        {
                            FurnitureItemId = (uint)item.id,
                            FurnitureId = i++,
                            Pos = new Il2CppPb.Vector3() { X = (int)(item.pos.x*100), Y = (int)(item.pos.y * 100), Z = (int)(item.pos.z * 100) },
                            Rotation = new Il2CppPb.Vector3() { X = (int)(item.rot.x * 100), Y = (int)(item.rot.y * 100), Z = (int)(item.rot.z * 100) },
                        };
                        IHomeSystem.sys.CreateAndPutDown(info, 0);
                    }

                    g.transform.position = AutoHomeItem.GetCenterAverage(list);
                    List<HomeItem_Furniture> longs = new();
                    foreach (var item in IHomeSystem.sys.allFurnitures)
                    {
                        if (item.Value.playerId == 0)
                        {
                            item.Value.transform.SetParent(g.transform);
                            longs.Add(item.value);
                        }
                    }
                    stc.Log("boat指令解析2");
                    foreach (var item in longs)
                    {
                        IHomeSystem.sys.UnRegisterFurniture(item);
                    }
                }
                else
                {
                    stc.Log("boat指令解析失败1");
                }
            }
        }
    }

}