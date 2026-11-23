using mod;
using Il2Cpp;
using MelonLoader;
using UnityEngine;
using static UnityEngine.GraphicsBuffer;

namespace HomeEditorModule.Core
{
    public class ObjectController
    {
        // ---------- 状态 ----------
        public bool IsActive { get; private set; } = false;
        private Transform _target;
        private Transform _originalFollow;

        // ---------- 参数 ----------
        public float MoveSpeed = 5f;              // 移动速度（米/秒）
        public float RotateSpeed = 5f;           // 旋转速度（度/每格滚轮）
        public bool MoveOnXZ = true;              // 是否限制在 XZ 平面移动
        public bool UseCameraDirection = true;    // 移动方向是否基于相机

        // ---------- 初始化 ----------
        public void Activate(Transform target)
        {
            if (target == null)
            {
                MelonLogger.Error("目标为空");
                return;
            }
            _originalFollow=CameraSystem.Instance.FollowTarget;
            _target = target;
            IsActive = true;
            MelonLogger.Msg($"控制器已激活，目标: {target.name}");
            CameraSystem.Instance.SetCameraFollowUnit(_target.gameObject);
        }

        public void Deactivate()
        {
            if (!IsActive) return;
            if (_originalFollow != null && (CameraSystem.Instance.FollowTarget == _target || CameraSystem.Instance.FollowTarget == TipMsgCore.i.mainplayer?.transform)) CameraSystem.Instance.SetCameraFollowUnit(_originalFollow.gameObject);
            IsActive = false;
            _target = null;
            MelonLogger.Msg("控制器已关闭");
        }

        public void Toggle(Transform target)
        {
            if (IsActive) Deactivate();
            else Activate(target);
        }

        // ---------- 每帧更新（在 OnUpdate 中调用） ----------
        public void OnUpdate()
        {
            if (!IsActive || _target == null) return;
            if (Input.GetKeyDown(KeyCode.CapsLock))
            {
                if (CameraSystem.Instance.FollowTarget == TipMsgCore.i.mainplayer.transform)
                {
                    CameraSystem.Instance.SetCameraFollowUnit(_target.gameObject);
                }
                else
                {
                    CameraSystem.Instance.SetCameraFollowUnit(TipMsgCore.i.mainplayer.gameObject);
                }
            }
            HandleRotation();
            HandleMovement();
        }

        // ---------- 滚轮旋转 ----------
        private void HandleRotation()
        {
            float scroll = Input.mouseScrollDelta.y;
            if (scroll == 0f) return;

            // 按住 Ctrl 时不旋转（用于其他功能）
            if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) return;

            // 滚轮控制绕 Y 轴旋转
            float angle = scroll * RotateSpeed;
            _target.Rotate(Vector3.up, angle, Space.World);

            MelonLogger.Msg($"旋转 {angle}°，当前角度: {_target.eulerAngles.y:F1}");
        }

        // ---------- WASD 移动 ----------
        private void HandleMovement()
        {
            float h = 0f, v = 0f, up = 0f;

            // ---------- 水平移动：WASD ----------
            if (Input.GetKey(KeyCode.W)) v += 1f;
            if (Input.GetKey(KeyCode.S)) v -= 1f;
            if (Input.GetKey(KeyCode.A)) h -= 1f;
            if (Input.GetKey(KeyCode.D)) h += 1f;

            // ---------- 垂直移动：Shift 升高，Ctrl 下降 ----------
            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);

            if (shift) up += 1f;
            if (ctrl) up -= 1f;

            // 如果同时按住 Shift 和 Ctrl，相互抵消
            if (shift && ctrl) up = 0f;

            // 没有任何输入则跳过
            if (h == 0f && v == 0f && up == 0f) return;

            // ---------- 计算方向 ----------
            Vector3 forward, right;
            if (UseCameraDirection && Camera.main != null)
            {
                forward = Camera.main.transform.forward;
                right = Camera.main.transform.right;
            }
            else
            {
                forward = Vector3.forward;
                right = Vector3.right;
            }

            // 限制在 XZ 平面时，去掉 forward/right 的 Y 分量
            if (MoveOnXZ)
            {
                forward.y = 0;
                right.y = 0;
                forward.Normalize();
                right.Normalize();
            }

            // ---------- 组合移动方向 ----------
            Vector3 moveDir = forward * v + right * h + Vector3.up * up;
            if (moveDir.sqrMagnitude > 1f) moveDir.Normalize();

            // ---------- 应用位置 ----------
            _target.position += moveDir * MoveSpeed * Time.deltaTime;
        }
    }
}