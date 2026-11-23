using Il2CppInutan;
using MelonLoader;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;
namespace mod.st
{
    public class OceanCreator
    {
        public static OceanCreator Instance { get; private set; } = new OceanCreator();

        // ---------- 状态 ----------
        public GameObject OceanObject { get; private set; }
        public bool IsActive => OceanObject != null;
        private Mesh _mesh;
        private Vector3[] _baseVertices;
        private Vector3[] _workVertices;
        private Color[] _colors;
        private float _time;
        private int _frameCounter;

        // ---------- 网格参数 ----------
        public float Size = 200f;
        public int Resolution = 120;
        public int NormalUpdateInterval = 2;

        // ---------- 波浪参数 ----------
        public float WaveHeight = 0.8f;
        public float WaveLength = 12f;
        public float WaveSpeed = 1.5f;
        public Vector2 WaveDirection = new Vector2(1f, 0.3f);
        public int WaveLayers = 4;

        // ---------- 材质参数 ----------
        public Color ShallowColor = new Color(0.15f, 0.55f, 0.75f, 0.85f);
        public Color DeepColor = new Color(0.02f, 0.15f, 0.35f, 0.95f);
        public Color FoamColor = new Color(0.95f, 0.97f, 1f, 1f);
        public float Smoothness = 0.95f;
        public float Metallic = 0.25f;
        public float FoamThreshold = 0.7f;

        // ---------- 跟随玩家 ----------
        public bool FollowPlayer = true;
        private Transform _player;

        // ================== 巨浪系统 ==================
        public bool EnableGiantWave = true;        // 是否启用随机巨浪
        public float GiantWaveMinInterval = 8f;    // 最小间隔
        public float GiantWaveMaxInterval = 20f;   // 最大间隔
        public float GiantWaveHeight = 6f;         // 巨浪高度（普通波高 0.8，巨浪可以 6~10）
        public float GiantWaveWidth = 30f;         // 巨浪宽度（横跨范围）
        public float GiantWaveDuration = 4f;       // 巨浪持续时间
        public float GiantWaveTravelSpeed = 15f;   // 巨浪传播速度

        private float _nextGiantWaveTime = 0f;
        private List<GiantWave> _activeGiantWaves = new List<GiantWave>();

        // 巨浪数据结构
        private class GiantWave
        {
            public Vector2 Origin;          // 生成起点（XZ）
            public Vector2 Direction;       // 传播方向
            public float Height;            // 峰值高度
            public float Width;             // 波峰宽度
            public float Speed;             // 传播速度
            public float Duration;          // 总时长
            public float Age;               // 已存活时间
            public float FrontPosition;     // 波前位置
        }

        // ================== 创建海面 ==================
        public GameObject CreateOcean(Vector3 position)
        {
            if (OceanObject != null) DestroyOcean();

            OceanObject = new GameObject("CustomOcean");
            OceanObject.transform.position = position;

            var mf = OceanObject.AddComponent<MeshFilter>();
            _mesh = CreatePlaneMesh(Size, Resolution);
            mf.mesh = _mesh;

            var mr = OceanObject.AddComponent<MeshRenderer>();
            mr.material = CreateWaterMaterial();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = true;

            _baseVertices = _mesh.vertices;
            _workVertices = new Vector3[_baseVertices.Length];
            _colors = new Color[_baseVertices.Length];

            _player = TipMsgCore.i.mainplayer.transform;

            // 随机安排第一次巨浪时间
            _nextGiantWaveTime = Time.time + Random.Range(3f, 6f);

            MelonLogger.Msg($"海面已创建: 分辨率={Resolution}, 顶点数={_baseVertices.Length}");
            return OceanObject;
        }

        public void DestroyOcean()
        {
            if (OceanObject != null)
            {
                GameObject.Destroy(OceanObject);
                OceanObject = null;
                _mesh = null;
                _baseVertices = null;
                _workVertices = null;
                _colors = null;
                _activeGiantWaves.Clear();
                MelonLogger.Msg("海面已销毁");
            }
        }

        // ================== 每帧更新 ==================
        public void OnUpdate()
        {
            if (!IsActive || _mesh == null) return;

            // 跟随玩家（XZ）
            if (FollowPlayer && _player != null)
            {
                Vector3 p = _player.position;
                Vector3 pos = OceanObject.transform.position;
                OceanObject.transform.position = new Vector3(p.x, pos.y, p.z);
            }

            _time += Time.deltaTime * WaveSpeed;

            // ---------- 巨浪更新 ----------
            UpdateGiantWaves();

            Vector2 dir1 = WaveDirection.normalized;
            Vector2 dir2 = new Vector2(-dir1.y, dir1.x);
            Vector2 dir3 = (dir1 + dir2).normalized;
            Vector2 dir4 = (dir1 - dir2).normalized;

            float maxHeight = WaveHeight * WaveLayers * 0.8f + GiantWaveHeight;

            for (int i = 0; i < _baseVertices.Length; i++)
            {
                Vector3 v = _baseVertices[i];
                float h = 0f;

                // ---------- 普通波浪 ----------
                h += GerstnerWave(v.x, v.z, dir1, 1.0f, WaveHeight, WaveLength, _time);
                h += GerstnerWave(v.x, v.z, dir2, 0.7f, WaveHeight * 0.8f, WaveLength * 1.3f, _time * 0.9f);
                h += GerstnerWave(v.x, v.z, dir3, 0.5f, WaveHeight * 0.5f, WaveLength * 0.7f, _time * 1.2f);
                h += GerstnerWave(v.x, v.z, dir4, 0.3f, WaveHeight * 0.3f, WaveLength * 2f, _time * 0.7f);

                // ---------- 叠加巨浪 ----------
                foreach (var wave in _activeGiantWaves)
                {
                    h += EvaluateGiantWave(wave, v.x, v.z);
                }

                _workVertices[i] = new Vector3(v.x, h, v.z);

                // ---------- 顶点颜色 ----------
                float hNorm = Mathf.Clamp01((h + maxHeight) / (2f * maxHeight));
                if (hNorm > FoamThreshold)
                {
                    float f = (hNorm - FoamThreshold) / (1f - FoamThreshold);
                    _colors[i] = Color.Lerp(ShallowColor, FoamColor, f);
                }
                else
                {
                    float f = hNorm / FoamThreshold;
                    _colors[i] = Color.Lerp(DeepColor, ShallowColor, f);
                }
            }

            _mesh.vertices = _workVertices;
            _mesh.colors = _colors;

            _frameCounter++;
            if (_frameCounter >= NormalUpdateInterval)
            {
                _mesh.RecalculateNormals();
                _frameCounter = 0;
            }

            _mesh.RecalculateBounds();
        }

        // ================== 巨浪逻辑 ==================
        private void UpdateGiantWaves()
        {
            // 1. 更新已有的巨浪
            for (int i = _activeGiantWaves.Count - 1; i >= 0; i--)
            {
                var wave = _activeGiantWaves[i];
                wave.Age += Time.deltaTime;
                wave.FrontPosition += wave.Speed * Time.deltaTime;

                if (wave.Age >= wave.Duration)
                    _activeGiantWaves.RemoveAt(i);
            }

            // 2. 随机生成新巨浪
            if (EnableGiantWave && Time.time >= _nextGiantWaveTime)
            {
                SpawnGiantWave();
                _nextGiantWaveTime = Time.time + Random.Range(GiantWaveMinInterval, GiantWaveMaxInterval);
            }
        }

        // ---------- 生成一个巨浪 ----------
        public void SpawnGiantWave()
        {
            // 随机方向（XZ 平面）
            float angle = Random.Range(0f, Mathf.PI * 2f);
            Vector2 dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));

            // 起点在波传播方向的反方向一侧，确保从海面边缘进入
            float half = Size * 0.5f;
            Vector2 origin = -dir * half;

            var wave = new GiantWave
            {
                Origin = origin,
                Direction = dir,
                Height = GiantWaveHeight * Random.Range(0.8f, 1.3f), // 高度随机
                Width = GiantWaveWidth * Random.Range(0.8f, 1.2f),
                Speed = GiantWaveTravelSpeed * Random.Range(0.8f, 1.2f),
                Duration = GiantWaveDuration * Random.Range(0.8f, 1.2f),
                Age = 0f,
                FrontPosition = 0f
            };

            _activeGiantWaves.Add(wave);
            MelonLogger.Msg($"巨浪生成! 方向({dir.x:F2}, {dir.y:F2}), 高度{wave.Height:F1}, 宽度{wave.Width:F1}");
        }

        // ---------- 计算某个点在巨浪中的高度 ----------
        private float EvaluateGiantWave(GiantWave wave, float x, float z)
        {
            // 点在波浪方向上的投影距离
            float dist = x * wave.Direction.x + z * wave.Direction.y;

            // 与波前的距离
            float diff = dist - wave.FrontPosition;

            // 波峰宽度内才有影响
            if (Mathf.Abs(diff) > wave.Width)
                return 0f;

            // 波形：余弦钟形，在波前最尖，向两侧衰减
            float t = diff / wave.Width;                       // -1 ~ 1
            float shape = Mathf.Cos(t * Mathf.PI * 0.5f);      // 1 → 0
            shape = shape * shape;                              // 更尖锐

            // 生命周期衰减：生成时从0涨到1，然后逐渐回落
            float lifeT = wave.Age / wave.Duration;            // 0 ~ 1
            float lifeFade;
            if (lifeT < 0.15f)
                lifeFade = lifeT / 0.15f;                       // 0 → 1 快速涨起
            else if (lifeT > 0.7f)
                lifeFade = 1f - (lifeT - 0.7f) / 0.3f;          // 1 → 0 缓慢消退
            else
                lifeFade = 1f;                                  // 中间保持

            return wave.Height * shape * lifeFade;
        }

        // ================== Gerstner 波 ==================
        private float GerstnerWave(float x, float z, Vector2 dir, float steepness, float amp, float wavelength, float t)
        {
            float k = 2f * Mathf.PI / wavelength;
            float f = k * (dir.x * x + dir.y * z - t * Mathf.Sqrt(9.8f / k));
            return steepness * amp * Mathf.Sin(f) / (1f + Mathf.Abs(steepness) * Mathf.Sin(f));
        }

        // ================== 创建网格 ==================
        private Mesh CreatePlaneMesh(float size, int resolution)
        {
            Mesh mesh = new Mesh();
            mesh.name = "CustomOceanMesh";
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

            int vCount = (resolution + 1) * (resolution + 1);
            Vector3[] verts = new Vector3[vCount];
            Vector2[] uvs = new Vector2[vCount];
            float half = size * 0.5f;

            int idx = 0;
            for (int y = 0; y <= resolution; y++)
            {
                for (int x = 0; x <= resolution; x++)
                {
                    float fx = (float)x / resolution;
                    float fy = (float)y / resolution;
                    verts[idx] = new Vector3(
                        Mathf.Lerp(-half, half, fx),
                        0f,
                        Mathf.Lerp(-half, half, fy)
                    );
                    uvs[idx] = new Vector2(fx * 20f, fy * 20f);
                    idx++;
                }
            }

            int[] tris = new int[resolution * resolution * 6];
            int t = 0;
            for (int y = 0; y < resolution; y++)
            {
                for (int x = 0; x < resolution; x++)
                {
                    int i0 = y * (resolution + 1) + x;
                    int i1 = i0 + 1;
                    int i2 = i0 + (resolution + 1);
                    int i3 = i2 + 1;
                    tris[t++] = i0; tris[t++] = i2; tris[t++] = i1;
                    tris[t++] = i1; tris[t++] = i2; tris[t++] = i3;
                }
            }

            mesh.vertices = verts;
            mesh.uv = uvs;
            mesh.triangles = tris;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        // ================== 材质 ==================
        private Material CreateWaterMaterial()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit")
                         ?? Shader.Find("Standard");

            Material mat = new Material(shader);

            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", ShallowColor);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", ShallowColor);

            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", Smoothness);
            if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", Smoothness);
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", Metallic);

            mat.SetFloat("_Mode", 3);
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.EnableKeyword("_ALPHABLEND_ON");
            mat.renderQueue = 3000;

            if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1);
            if (mat.HasProperty("_Blend")) mat.SetFloat("_Blend", 0);
            if (mat.HasProperty("_AlphaClip")) mat.SetFloat("_AlphaClip", 0);

            mat.EnableKeyword("_ENVIRONMENTREFLECTIONS_ON");
            mat.EnableKeyword("_SPECULARHIGHLIGHTS_ON");

            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", new Color(0.05f, 0.15f, 0.25f) * 0.4f);

            MelonLogger.Msg($"水面 Shader: {shader.name}");
            return mat;
        }

        // ================== 外部接口 ==================
        public void SetWaveHeight(float h) => WaveHeight = Mathf.Max(0f, h);
        public void SetWaveSpeed(float s) => WaveSpeed = Mathf.Max(0f, s);
        public void SetWaveLength(float l) => WaveLength = Mathf.Max(0.1f, l);
        public void SetColor(Color shallow, Color deep, Color foam)
        {
            ShallowColor = shallow;
            DeepColor = deep;
            FoamColor = foam;
            if (OceanObject != null)
            {
                var mr = OceanObject.GetComponent<MeshRenderer>();
                if (mr != null && mr.material != null)
                {
                    if (mr.material.HasProperty("_BaseColor")) mr.material.SetColor("_BaseColor", shallow);
                    if (mr.material.HasProperty("_Color")) mr.material.SetColor("_Color", shallow);
                }
            }
        }
        public void SetFollowPlayer(bool follow) => FollowPlayer = follow;

        // 巨浪参数设置
        public void SetGiantWaveEnabled(bool enable) => EnableGiantWave = enable;
        public void SetGiantWaveHeight(float h) => GiantWaveHeight = Mathf.Max(0f, h);
        public void SetGiantWaveInterval(float min, float max)
        {
            GiantWaveMinInterval = Mathf.Max(0.5f, min);
            GiantWaveMaxInterval = Mathf.Max(GiantWaveMinInterval, max);
        }
        // 立即触发一次巨浪
        public void TriggerGiantWaveNow() => SpawnGiantWave();
    }
}