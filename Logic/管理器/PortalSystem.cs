using MelonLoader;
using mod;
using System;
using UnityEngine;
using Object = UnityEngine.Object;

public static class PortalSystem
{
    public static bool IsActive = false;

    // 发射力度
    private static float _launchSpeed = 15f;
    private static float _launchAngle = 45f;
    private static readonly Vector3 Gravity = new Vector3(0, -9.81f, 0);

    private class Portal
    {
        public GameObject Obj;
        public bool IsPlaced => Obj != null && Obj.activeSelf;
        public Vector3 Position;
        public Vector3 Normal;
    }

    private static Portal _portalA = new Portal();
    private static Portal _portalB = new Portal();
    private static int _nextPortalIndex = 0;

    private static GameObject _projectileObj;
    private static bool _isShooting = false;
    private static Vector3 _projPos;
    private static Vector3 _projVel;

    // 记录玩家上一帧的位置
    private static Vector3 _lastPlayerPos;

    public static void Init()
    {
        IsActive = true;
        _portalA = CreatePortalBase("Portal_A", new Color(0.2f, 0.8f, 1f, 0.5f)); // 半透明蓝色
        _portalB = CreatePortalBase("Portal_B", new Color(0.8f, 0.2f, 1f, 0.5f)); // 半透明紫色

        _projectileObj = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        _projectileObj.name = "Portal_Projectile";
        _projectileObj.transform.localScale = Vector3.one * 0.2f;
        _projectileObj.GetComponent<Renderer>().material = new Material(Shader.Find("Sprites/Default")) { color = Color.cyan };
        var col = _projectileObj.GetComponent<Collider>();
        if (col != null) Object.Destroy(col);
        _projectileObj.SetActive(false);

        if (TipMsgCore.i != null && TipMsgCore.i.mainplayer != null)
            _lastPlayerPos = TipMsgCore.i.mainplayer.transform.position;

        MelonLogger.Msg("[Portal] 传送门系统已开启 (无CD跨越版)");
    }

    public static void Tick()
    {
        if (!IsActive) return;

        HandleInput();
        UpdateProjectile();
        CheckTeleport();
    }

    public static void Stop()
    {
        IsActive = false;
        if (_portalA.Obj != null) Object.Destroy(_portalA.Obj);
        if (_portalB.Obj != null) Object.Destroy(_portalB.Obj);
        if (_projectileObj != null) Object.Destroy(_projectileObj);
        MelonLogger.Msg("[Portal] 传送门系统已关闭");
    }

    private static void HandleInput()
    {
        // 【滚轮控制发射力度】
        float scroll = Input.mouseScrollDelta.y;
        if (Mathf.Abs(scroll) > 0.01f)
        {
            _launchSpeed = Mathf.Clamp(_launchSpeed + scroll * 2f, 5f, 60f);
            MelonLogger.Msg($"[Portal] 当前发射力度: {_launchSpeed:F1}");
        }

        if (Input.GetKeyDown(KeyCode.P) && !_isShooting)
        {
            if (TipMsgCore.i == null || TipMsgCore.i.mainplayer == null) return;
            Camera cam = Camera.main;
            if (cam == null) return;

            Transform player = TipMsgCore.i.mainplayer.transform;
            _isShooting = true;

            // 从人物胸口发射
            _projPos = player.position + Vector3.up * 1.2f + player.forward * 0.3f;

            // 方向由相机控制
            Vector3 forward = cam.transform.forward;
            Vector3 launchDir = (forward + Vector3.up * Mathf.Tan(_launchAngle * Mathf.Deg2Rad)).normalized;

            _projVel = launchDir * _launchSpeed;
            _projectileObj.SetActive(true);
            _projectileObj.transform.position = _projPos;
        }
    }

    private static void UpdateProjectile()
    {
        if (!_isShooting) return;

        Vector3 lastPos = _projPos;
        _projVel += Gravity * Time.deltaTime;
        _projPos += _projVel * Time.deltaTime;

        _projectileObj.transform.position = _projPos;

        Vector3 dir = _projPos - lastPos;
        float dist = dir.magnitude;
        if (dist > 0.01f)
        {
            int ignoreMask = ~0;
            int charLayer = LayerMask.NameToLayer("Character");
            if (charLayer != -1) ignoreMask &= ~(1 << charLayer);
            int triggerLayer = LayerMask.NameToLayer("Trigger");
            if (triggerLayer != -1) ignoreMask &= ~(1 << triggerLayer);

            if (Physics.SphereCast(lastPos, 0.1f, dir.normalized, out RaycastHit hit, dist, ignoreMask))
            {
                if (!hit.collider.isTrigger && LayerMask.LayerToName(hit.collider.gameObject.layer) != "Trigger")
                {
                    PlacePortal(hit.point, hit.normal);
                    _isShooting = false;
                    _projectileObj.SetActive(false);
                }
            }
        }

        if (_projPos.y < -100f || _projVel.sqrMagnitude < 0.1f)
        {
            _isShooting = false;
            _projectileObj.SetActive(false);
        }
    }

    private static void PlacePortal(Vector3 point, Vector3 normal)
    {
        Portal target = _nextPortalIndex == 0 ? _portalA : _portalB;

        if (Vector3.Dot(normal, _projVel) > 0) normal = -normal;

        Quaternion rot = Quaternion.LookRotation(normal);
        if (Mathf.Abs(Vector3.Dot(normal, Vector3.up)) > 0.99f)
            rot = Quaternion.LookRotation(normal, Vector3.forward);
        else
            rot = Quaternion.LookRotation(normal, Vector3.up);

        target.Obj.transform.position = point + normal * 0.15f;
        target.Obj.transform.rotation = rot;
        target.Position = target.Obj.transform.position;
        target.Normal = normal;
        target.Obj.SetActive(true);

        _nextPortalIndex = 1 - _nextPortalIndex;
    }

    private static Portal CreatePortalBase(string name, Color color)
    {
        Portal p = new Portal();
        p.Obj = GameObject.CreatePrimitive(PrimitiveType.Cube);
        p.Obj.name = name;
        p.Obj.transform.localScale = new Vector3(2f, 2f, 0.2f);
        p.Obj.layer = 0;
        p.Obj.SetActive(false);

        var col = p.Obj.GetComponent<Collider>();
        if (col != null) Object.Destroy(col);

        Shader s = Shader.Find("Sprites/Default");
        if (s == null) s = Shader.Find("Unlit/Color");
        if (s == null) s = Shader.Find("Standard");
        p.Obj.GetComponent<Renderer>().material = new Material(s) { color = color };

        return p;
    }

    // ==========================================
    // 无 CD 的严格跨越判定
    // ==========================================
    private static void CheckTeleport()
    {
        if (!_portalA.IsPlaced || !_portalB.IsPlaced) return;
        if (TipMsgCore.i == null || TipMsgCore.i.mainplayer == null) return;

        Transform player = TipMsgCore.i.mainplayer.transform;
        Vector3 footPos = player.position;

        // 判定 A 门
        if (IsInsidePortalThickness(footPos, _portalA))
        {
            TeleportPlayer(player, _portalA, _portalB);
            return;
        }
        // 判定 B 门
        else if (IsInsidePortalThickness(footPos, _portalB))
        {
            TeleportPlayer(player, _portalB, _portalA);
            return;
        }
    }

    private static bool IsInsidePortalThickness(Vector3 footPos, Portal portal)
    {
        // 获取人物的三个关键点，把体积算进去 (脚底、腰部、头顶)
        Vector3[] checkPoints = new Vector3[]
        {
            footPos + Vector3.up * 0.2f,   // 脚底往上一点点
            footPos + Vector3.up * 0.75f,  // 腰部
            footPos + Vector3.up * 1.4f    // 头顶
        };

        foreach (var point in checkPoints)
        {
            Vector3 localPos = portal.Obj.transform.InverseTransformPoint(point);

            // 【放大判定范围实现“碰到即传”】
            // X 和 Y 判定：门半宽 1.0 + 人物半径 0.3 = 1.3。只要模型边缘贴到门就触发
            // Z 判定：厚度设为 1.0。只要人物靠近门面前后 0.5 米的空气墙，立刻触发
            if (Mathf.Abs(localPos.x) <= 1.3f && Mathf.Abs(localPos.y) <= 1.3f)
            {
                if (Mathf.Abs(localPos.z) <= 0.5f)
                {
                    return true;
                }
            }
        }
        return false;
    }

    private static void TeleportPlayer(Transform player, Portal entry, Portal exit)
    {
        // 局部坐标转换
        Vector3 localPos = entry.Obj.transform.InverseTransformPoint(player.position);
        localPos.z *= -1;

        Vector3 newPos = exit.Obj.transform.TransformPoint(localPos);

        // 【核心防回传】：因为现在判定范围变大了（前后各 0.5），
        // 传送后必须把玩家强行往门外推 2 米，这样才能彻底脱离吸附判定区！
        newPos += exit.Normal * 2.0f;

        // 强制位移
        TipMsgCore.i.mainplayer.SetPositionAndRotation(newPos, player.rotation, true);

        MelonLogger.Msg($"[Portal] 触碰传送，已送出 2 米。");
    }
}