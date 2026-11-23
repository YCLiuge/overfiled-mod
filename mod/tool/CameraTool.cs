using Il2Cpp;
using Il2CppInutan;
using MelonLoader;
using System.Collections;
using UnityEngine;

namespace mod.tool
{

    public class CustomCameraController
    {
        public static CustomCameraController Instance { get; private set; } = new CustomCameraController();

        // ---------- 状态 ----------
        public bool IsActive { get; private set; } = false;
        private Camera _camera;
        private Transform _cameraTransform;

        // ---------- 目标 ----------
        public Transform Target;          // 跟随目标
        public Vector3 TargetOffset = new Vector3(0, 1f, 0); // 目标偏移（看向目标的上方）

        // ---------- 相机参数 ----------
        public float Distance = 5f;        // 距离目标的距离
        public float Yaw = 0f;             // 水平角度
        public float Pitch = 15f;          // 俯仰角度
        public float Roll = 0f;            // Z 轴翻滚角度（游戏原本禁止）

        // ---------- 鼠标控制 ----------
        public bool MouseControlEnabled = true;
        public float MouseSensitivity = 0.4f;
        public bool InvertY = false;

        // ---------- 平滑 ----------
        public float PositionSmooth = 15f;  // 位置平滑速度
        public float RotationSmooth = 15f;  // 旋转平滑速度

        // ---------- 保存原始状态（用于恢复） ----------
        private Vector3 _originalLocalPos;
        private Quaternion _originalLocalRot;
        private Transform _originalParent;

        // ---------- 初始化 ----------
        public void Activate()
        {
            if (IsActive) return;

            _camera = CameraSystem.Instance?.MainCamera;
            if (_camera == null)
            {
                MelonLogger.Error("找不到主相机");
                return;
            }
            _cameraTransform = _camera.transform;

            // 保存原始状态
            _originalParent = _cameraTransform.parent;
            _originalLocalPos = _cameraTransform.localPosition;
            _originalLocalRot = _cameraTransform.localRotation;

            IsActive = true;
            MelonLogger.Msg("自定义相机控制器已激活");
        }

        public void Deactivate()
        {
            if (!IsActive) return;

            // 恢复原始状态
            if (_cameraTransform != null && _originalParent != null)
            {
                _cameraTransform.SetParent(_originalParent, false);
                _cameraTransform.localPosition = _originalLocalPos;
                _cameraTransform.localRotation = _originalLocalRot;
            }

            IsActive = false;
            MelonLogger.Msg("自定义相机控制器已关闭");
        }

        public void Toggle()
        {
            if (IsActive)
            {
                Deactivate();
                CameraSystem.Instance._MainCameraController.enabled = true;
            }
            else
            {
                Activate();
                CameraSystem.Instance._MainCameraController.enabled = false;
            }
        }

        // ---------- 每帧更新（在 Mod 的 OnLateUpdate 中调用） ----------
        public void OnLateUpdate()
        {
            if (!IsActive || _cameraTransform == null) return;

            // 1. 鼠标控制角度
            if (MouseControlEnabled&&!Input.GetKey(KeyCode.LeftAlt))
            {
                float mouseX = Input.GetAxis("Mouse X") * MouseSensitivity;
                float mouseY = Input.GetAxis("Mouse Y") * MouseSensitivity * (InvertY ? 1 : -1);

                Yaw += mouseX;
                Pitch += mouseY;
                Pitch = Mathf.Clamp(Pitch, -80f, 80f);
            }

            // 2. 计算旋转
            Quaternion rotation = Quaternion.Euler(Pitch, Yaw, Roll);

            // 3. 计算位置（基于旋转和距离，围绕目标）
            Vector3 targetPos = Target != null
                ? Target.position + TargetOffset
                : Vector3.zero;

            Vector3 offset = rotation * new Vector3(0, 0, -Distance);
            Vector3 desiredPos = targetPos + offset;

            // 4. 平滑插值
            float posLerp = 1f - Mathf.Exp(-PositionSmooth * Time.deltaTime);
            float rotLerp = 1f - Mathf.Exp(-RotationSmooth * Time.deltaTime);

            _cameraTransform.position = Vector3.Lerp(_cameraTransform.position, desiredPos, posLerp);
            _cameraTransform.rotation = Quaternion.Slerp(_cameraTransform.rotation, rotation, rotLerp);
        }

        // ---------- 外部接口 ----------
        public void SetTarget(Transform target) => Target = target;
        public void SetDistance(float distance) => Distance = Mathf.Clamp(distance, 0.5f, 50f);
        public void SetYaw(float yaw) => Yaw = yaw;
        public void SetPitch(float pitch) => Pitch = Mathf.Clamp(pitch, -80f, 80f);
        public void SetRoll(float roll) => Roll = roll;
        public void AddRoll(float delta) => Roll += delta;

        /// <summary>
        /// 在 duration 秒内平滑旋转到指定 Roll 角度
        /// </summary>
        public void RotateRollTo(float targetRoll, float duration)
        {
            MelonCoroutines.Start(TweenRoll(targetRoll, duration));
        }

        private IEnumerator TweenRoll(float targetRoll, float duration)
        {
            float startRoll = Roll;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                Roll = Mathf.LerpAngle(startRoll, targetRoll, Mathf.SmoothStep(0, 1, t));
                yield return null;
            }
            Roll = targetRoll;
        }
        public void Update()
        {
            var ctrl = CustomCameraController.Instance;

            // F5: 开关自定义相机
            if (Input.GetKeyDown(KeyCode.F5))
            {
                ctrl.Toggle();

                // 激活时自动绑定玩家作为目标
                if (ctrl.IsActive && ctrl.Target == null)
                {
                    var player = CameraSystem.Instance.FollowTarget;
                    if (player != null)
                        ctrl.SetTarget(player);
                }
            }

            // 滚轮调节距离
            if (ctrl.IsActive)
            {
                float scroll = Input.GetAxis("Mouse ScrollWheel");
                if (scroll != 0f)
                    ctrl.SetDistance(ctrl.Distance - scroll * 3f);
            }

            // Q/E 旋转 Roll（Z 轴翻滚）
            if (ctrl.IsActive)
            {
                //if (Input.GetKey(KeyCode.Q)) ctrl.AddRoll(90f * Time.deltaTime);
                //if (Input.GetKey(KeyCode.E)) ctrl.AddRoll(-90f * Time.deltaTime);
            }
        }     
    }
}
    

