using Il2Cpp;
using Il2CppInutan;
using Il2CppKinematicCharacterController;
using MelonLoader;
using UnityEngine;

namespace mod
{
    public class DroneFlyController
    {
        public static DroneFlyController Instance { get; private set; } = new DroneFlyController();

        // ---------- 状态 ----------
        public bool IsFlying { get; private set; } = false;

        // ---------- 玩家引用 ----------
        private Unit _playerUnit;
        private KinematicCharacterMotor _motor;
        private Transform _playerTransform;
        private bool _originalMotorEnabled;

        // ---------- 飞行参数 ----------
        public float MoveSpeed = 15f;
        public float BoostMultiplier = 3f;
        public float Smoothness = 12f;
        public float MouseSensitivity = 3f;
        public float MinPitch = -89f;
        public float MaxPitch = 89f;
        public bool InvertY = false;

        // ---------- 输入 ----------
        public KeyCode ToggleKey = KeyCode.F8;
        public bool RequireRightMouse = false;   // 是否必须按住右键才能旋转视角

        // ---------- 内部状态 ----------
        private Vector3 _velocity;
        private float _yaw = 0f;
        private float _pitch = 0f;

        // ---------- 切换飞行 ----------
        public void Toggle()
        {
            if (IsFlying) StopFlying();
            else StartFlying();
        }

        public void StartFlying()
        {
            if (IsFlying) return;

            var playerEntity = PlayerSystem.Instance?.MainPlayerEntity;
            if (playerEntity == null)
            {
                MelonLogger.Error("找不到玩家");
                return;
            }

            _playerUnit = TipMsgCore.i.mainplayer.GetComponent<Unit>();
            if (_playerUnit == null)
            {
                MelonLogger.Error("找不到 Unit 组件");
                return;
            }

            _playerTransform = _playerUnit.transform;
            _motor = _playerUnit.GetComponent<KinematicCharacterMotor>();

            if (_motor != null)
            {
                _originalMotorEnabled = _motor.enabled;
                _motor.enabled = false;
            }
            // 从当前相机朝向初始化 yaw/pitch
            var cam = CameraSystem.Instance?.MainCamera;
            if (cam != null)
            {
                Vector3 euler = cam.transform.eulerAngles;
                _yaw = euler.y;
                _pitch = euler.x;
                if (_pitch > 180f) _pitch -= 360f; // 归一化到 -180~180
            }
            else
            {
                _yaw = _playerTransform.eulerAngles.y;
                _pitch = 0f;
            }

            _velocity = Vector3.zero;
            IsFlying = true;
            MelonLogger.Msg($"无人机飞行：开启 (速度 {MoveSpeed})");

        }

        public void StopFlying()
        {
            if (!IsFlying) return;

            if (_motor != null)
                _motor.enabled = _originalMotorEnabled;

            IsFlying = false;
            _velocity = Vector3.zero;
            TipMsgCore.i.mainplayer.SetPosition(TipMsgCore.i.mainplayer.transform.position);
            MelonLogger.Msg("无人机飞行：关闭");
        }

        // ---------- 每帧更新 ----------
        public void OnUpdate()
        {
            if (Input.GetKeyDown(ToggleKey))
            {
                Toggle();
                return;
            }

            if (!IsFlying || _playerTransform == null) return;

            UpdateRotation();
            UpdateMovement();

            if (DroneFlyController.Instance.IsFlying)
            {
                bool alt = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
                if (alt)
                {
                    float scroll = Input.mouseScrollDelta.y;
                    if (scroll != 0f)
                    {
                        float newSpeed = DroneFlyController.Instance.MoveSpeed + scroll * 2f;
                        DroneFlyController.Instance.SetSpeed(newSpeed);
                        MelonLogger.Msg($"飞行速度: {DroneFlyController.Instance.MoveSpeed:F1}");
                    }
                }

                // Ctrl + 滚轮 调节灵敏度
                bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
                if (ctrl)
                {
                    float scroll = Input.mouseScrollDelta.y;
                    if (scroll != 0f)
                    {
                        float newSens = DroneFlyController.Instance.MouseSensitivity + scroll * 0.5f;
                        DroneFlyController.Instance.SetSensitivity(newSens);
                        MelonLogger.Msg($"鼠标灵敏度: {DroneFlyController.Instance.MouseSensitivity:F1}");
                    }
                }
            }
        }
        

        // ---------- 鼠标控制视角 ----------
        private void UpdateRotation()
        {
            bool canRotate = !RequireRightMouse || Input.GetMouseButton(1);
            if (canRotate)
            {
                float mx = Input.GetAxis("Mouse X") * MouseSensitivity;
                float my = Input.GetAxis("Mouse Y") * MouseSensitivity * (InvertY ? 1f : -1f);

                _yaw += mx;
                _pitch += my;
                _pitch = Mathf.Clamp(_pitch, MinPitch, MaxPitch);
            }

            // 应用到相机（让玩家看到的视角跟随鼠标）
            var cam = CameraSystem.Instance?.MainCamera;
            if (cam != null)
            {
                cam.transform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);
            }

            // 玩家角色也同步朝向（只同步水平方向）
            _playerTransform.rotation = Quaternion.Euler(0f, _yaw, 0f);
        }

        // ---------- WASD 沿视线方向移动 ----------
        private void UpdateMovement()
        {
            float h = 0f, v = 0f, up = 0f;

            if (Input.GetKey(KeyCode.W)) v += 1f;
            if (Input.GetKey(KeyCode.S)) v -= 1f;
            if (Input.GetKey(KeyCode.A)) h -= 1f;
            if (Input.GetKey(KeyCode.D)) h += 1f;
            if (Input.GetKey(KeyCode.Space)) up += 1f;
            if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) up -= 1f;

            bool boost = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            float speed = MoveSpeed * (boost ? BoostMultiplier : 1f);

            // 基于当前 yaw/pitch 计算方向
            Quaternion lookRot = Quaternion.Euler(_pitch, _yaw, 0f);
            Vector3 forward = lookRot * Vector3.forward; // 包含俯仰（朝向视线）
            Vector3 right = lookRot * Vector3.right;     // 水平方向

            // 组合移动方向
            Vector3 moveDir = forward * v + right * h + Vector3.up * up;

            if (moveDir.sqrMagnitude > 1f)
                moveDir.Normalize();

            // 平滑速度
            Vector3 targetVelocity = moveDir * speed;
            float lerp = 1f - Mathf.Exp(-Smoothness * Time.deltaTime);
            _velocity = Vector3.Lerp(_velocity, targetVelocity, lerp);

            // 应用位置
            _playerTransform.position += _velocity * Time.deltaTime;
        }

        // ---------- 外部接口 ----------
        public void SetSpeed(float speed) => MoveSpeed = Mathf.Clamp(speed, 1f, 200f);
        public void SetSensitivity(float s) => MouseSensitivity = Mathf.Clamp(s, 0.1f, 20f);
        public void SetInvertY(bool b) => InvertY = b;
        public void SetRequireRightMouse(bool b) => RequireRightMouse = b;
        public float GetYaw() => _yaw;
        public float GetPitch() => _pitch;
    }
}