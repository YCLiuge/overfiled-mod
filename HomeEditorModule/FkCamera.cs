using Il2Cpp;
using mod;
using UnityEngine;

namespace HomeEditorModule.Core;

// Let the game keep handling orbit/zoom; only separate its follow point from the player.
internal static class FkCamera
{
    private static GameObject? anchor;
    private static Transform? previousTarget;
    internal static bool Active => anchor != null;

    internal static void SetEnabled(bool enabled)
    {
        if (!enabled)
        {
            bool wasActive = Active;
            Stop();
            if (wasActive) CenterTMPManager.SetText("相机分离已关闭");
            return;
        }
        if (Active) return;
        var system = CameraSystem.Instance;
        if (system == null || system.MainCamera == null || TipMsgCore.i.mainplayer == null)
        {
            TipMsgCore.i.Send("相机或角色尚未就绪");
            return;
        }
        // The host custom camera writes the camera pose in LateUpdate and cannot share ownership.
        if (mod.tool.CustomCameraController.Instance.IsActive)
        {
            TipMsgCore.i.Send("请先关闭自定义相机，再开启 -fk 相机分离");
            return;
        }
        previousTarget = system.FollowTarget;
        anchor = new GameObject("HomeEditorFkCameraTarget");
        anchor.transform.position = previousTarget != null
            ? previousTarget.position : TipMsgCore.i.mainplayer.transform.position;
        anchor.transform.rotation = previousTarget != null
            ? previousTarget.rotation : TipMsgCore.i.mainplayer.transform.rotation;
        try { system.SetCameraFollowUnit(anchor); }
        catch { Stop(); throw; }
        CenterTMPManager.SetText("相机已分离：方向键移动，PageUp/PageDown 升降，F6 恢复");
    }

    internal static void Tick()
    {
        if (!Active) return;
        var system = CameraSystem.Instance;
        if (system == null || system.MainCamera == null ||
            system.FollowTarget != anchor!.transform || mod.tool.CustomCameraController.Instance.IsActive)
        {
            // A different camera tool took control; do not overwrite its follow target.
            Stop();
            return;
        }
        var rotation = Quaternion.Euler(0, system.MainCamera.transform.eulerAngles.y, 0);
        Vector3 direction = Vector3.zero;
        if (Input.GetKey(KeyCode.UpArrow)) direction += rotation * Vector3.forward;
        if (Input.GetKey(KeyCode.DownArrow)) direction -= rotation * Vector3.forward;
        if (Input.GetKey(KeyCode.LeftArrow)) direction -= rotation * Vector3.right;
        if (Input.GetKey(KeyCode.RightArrow)) direction += rotation * Vector3.right;
        if (Input.GetKey(KeyCode.PageUp)) direction += Vector3.up;
        if (Input.GetKey(KeyCode.PageDown)) direction -= Vector3.up;
        anchor.transform.position += direction.normalized * 5f * Time.deltaTime;
    }

    internal static void Stop()
    {
        try
        {
            var system = CameraSystem.Instance;
            var target = previousTarget != null ? previousTarget : TipMsgCore.i.mainplayer?.transform;
            if (anchor != null)
            {
                var custom = mod.tool.CustomCameraController.Instance;
                if (custom.Target == anchor.transform) custom.SetTarget(target);
                if (system != null && system.FollowTarget == anchor.transform && target != null)
                    system.SetCameraFollowUnit(target.gameObject);
            }
        }
        finally
        {
            if (anchor != null) UnityEngine.Object.Destroy(anchor);
            anchor = null;
            previousTarget = null;
        }
    }
}
