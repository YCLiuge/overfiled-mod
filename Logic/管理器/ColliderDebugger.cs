using System.Collections.Generic;
using MelonLoader;
using UnityEngine;
using Object = UnityEngine.Object;

public static class ColliderDebugger
{
    public static bool IsActive = false;
    public static bool UseRaycastMode = false;

    private static readonly List<GameObject> _targetObjects = new List<GameObject>();
    private static readonly Dictionary<GameObject, LineRenderer> _visuals = new();
    private static readonly List<GameObject> _toRemove = new();

    private static Material _lineMaterial;

    // 面数限制：如果模型面数太密，线框会糊成一团，超过此值退化为包围盒
    private const int MAX_TRIANGLES_FOR_WIREFRAME = 3000;

    public static void Tick()
    {
        if (!IsActive) return;

        InitMaterial();

        _toRemove.Clear();
        foreach (var kvp in _visuals)
        {
            var go = kvp.Key;
            if (go == null || !go.activeInHierarchy)
            {
                if (kvp.Value != null) Object.Destroy(kvp.Value.gameObject);
                _toRemove.Add(go);
            }
        }
        foreach (var go in _toRemove) _visuals.Remove(go);

        foreach (var obj in _targetObjects)
        {
            if (obj != null) UpdateVisualForObject(obj);
        }

        if (UseRaycastMode)
        {
            Camera cam = Camera.main;
            if (cam != null)
            {
                Ray ray = new Ray(cam.transform.position, cam.transform.forward);
                if (Physics.Raycast(ray, out RaycastHit hit, 100f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                {
                    UpdateVisualForObject(hit.collider.gameObject);
                }
            }
        }

        foreach (var kvp in _visuals)
        {
            var go = kvp.Key;
            var line = kvp.Value;
            if (go != null && line != null)
            {
                // 动态调整线宽：距离越远，线越粗（防止远处看不清）
                if (Camera.main != null)
                {
                    float dist = Vector3.Distance(Camera.main.transform.position, go.transform.position);
                    float width = Mathf.Clamp(dist * 0.003f, 0.015f, 0.08f);
                    line.startWidth = width;
                    line.endWidth = width;
                }

                DrawModelWireframe(go, line);
            }
        }
    }

    private static void InitMaterial()
    {
        if (_lineMaterial == null)
        {
            // 寻找无光照的 Shader，确保颜色纯正不被光影影响
            Shader shader = Shader.Find("GUI/Text Shader") ?? Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color") ?? Shader.Find("Standard");
            _lineMaterial = new Material(shader);

            // 极高对比度的亮青色
            _lineMaterial.color = Color.cyan;

            // 无视深度遮挡，强行渲染在最前面
            _lineMaterial.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
            _lineMaterial.renderQueue = 4000;
        }
    }

    public static void AddTarget(GameObject obj)
    {
        if (obj != null && !_targetObjects.Contains(obj))
        {
            _targetObjects.Add(obj);
            MelonLogger.Msg($"[Debug] 已绑定模型线框: {obj.name}");
        }
    }

    public static void ClearAll()
    {
        _targetObjects.Clear();
        foreach (var kvp in _visuals)
            if (kvp.Value != null) Object.Destroy(kvp.Value.gameObject);
        _visuals.Clear();
    }

    private static void UpdateVisualForObject(GameObject obj)
    {
        if (obj == null || !obj.activeInHierarchy) return;

        if (obj.GetComponent<Renderer>() == null)
        {
            var r = obj.GetComponentInChildren<Renderer>();
            if (r != null) obj = r.gameObject;
        }

        if (obj.GetComponent<Renderer>() == null) return;

        if (!_visuals.ContainsKey(obj))
        {
            var lineObj = new GameObject($"Wireframe_{obj.name}");
            var line = lineObj.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.material = _lineMaterial;
            line.startColor = Color.cyan;
            line.endColor = Color.cyan;

            _visuals[obj] = line;
        }
    }

    private static void DrawModelWireframe(GameObject obj, LineRenderer line)
    {
        Mesh mesh = null;
        Transform meshTransform = obj.transform;
        var smr = obj.GetComponent<SkinnedMeshRenderer>();

        if (smr != null)
        {
            mesh = new Mesh();
            smr.BakeMesh(mesh, true);
            meshTransform = smr.transform;
        }
        else
        {
            var mf = obj.GetComponent<MeshFilter>();
            if (mf != null)
            {
                mesh = mf.sharedMesh;
                meshTransform = mf.transform;
            }
        }

        if (mesh == null || mesh.vertexCount == 0)
        {
            line.positionCount = 0;
            return;
        }

        // 如果模型面数太高，直接画线框会糊成绿块/青块，退化为画清晰的包围盒
        if (mesh.triangles.Length > MAX_TRIANGLES_FOR_WIREFRAME * 3)
        {
            DrawBoundsBox(obj, line);
            return;
        }

        Vector3[] vertices = mesh.vertices;
        int[] triangles = mesh.triangles;

        int ptCount = triangles.Length * 2;
        if (line.positionCount != ptCount) line.positionCount = ptCount;

        int idx = 0;
        for (int i = 0; i < triangles.Length; i += 3)
        {
            Vector3 v0 = meshTransform.TransformPoint(vertices[triangles[i]]);
            Vector3 v1 = meshTransform.TransformPoint(vertices[triangles[i + 1]]);
            Vector3 v2 = meshTransform.TransformPoint(vertices[triangles[i + 2]]);

            line.SetPosition(idx++, v0);
            line.SetPosition(idx++, v1);
            line.SetPosition(idx++, v1);
            line.SetPosition(idx++, v2);
            line.SetPosition(idx++, v2);
            line.SetPosition(idx++, v0);
        }
    }

    private static void DrawBoundsBox(GameObject obj, LineRenderer line)
    {
        var r = obj.GetComponent<Renderer>();
        if (r == null) return;

        Bounds b = r.bounds;
        Vector3 c = b.center;
        Vector3 e = b.extents;

        Vector3[] p = new Vector3[8];
        p[0] = c + new Vector3(-e.x, -e.y, -e.z);
        p[1] = c + new Vector3(e.x, -e.y, -e.z);
        p[2] = c + new Vector3(e.x, -e.y, e.z);
        p[3] = c + new Vector3(-e.x, -e.y, e.z);
        p[4] = c + new Vector3(-e.x, e.y, -e.z);
        p[5] = c + new Vector3(e.x, e.y, -e.z);
        p[6] = c + new Vector3(e.x, e.y, e.z);
        p[7] = c + new Vector3(-e.x, e.y, e.z);

        int[] edges = { 0, 1, 1, 2, 2, 3, 3, 0, 4, 5, 5, 6, 6, 7, 7, 4, 0, 4, 1, 5, 2, 6, 3, 7 };

        if (line.positionCount != 24) line.positionCount = 24;
        for (int i = 0; i < edges.Length; i++)
        {
            line.SetPosition(i, p[edges[i]]);
        }
    }
}