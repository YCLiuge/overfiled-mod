using MelonLoader;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace mod.tool
{
    public static class ModelExporter
    {
        // 纹理属性名 → MTL 关键字映射表（Blender 可识别）
        private static readonly Dictionary<string, string> TexturePropertyToMTLMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "_MainTex", "map_Kd" },
            { "mainTexture", "map_Kd" },
            { "_BumpMap", "map_Norm" },
            { "_NormalMap", "map_Norm" },
            { "_MetallicGlossMap", "map_metallic" },
            { "_SpecGlossMap", "map_specular" },
            { "_EmissionMap", "map_emission" },
            { "_OcclusionMap", "map_ao" },
            { "_ParallaxMap", "map_disp" },
            { "_HeightMap", "map_disp" },
            { "_RoughnessMap", "map_roughness" },
            // 可扩展更多
        };

        /// <summary>
        /// 导出 GameObject 的网格为 OBJ 格式
        /// </summary>
        /// <param name="gameObject">要导出的游戏对象</param>
        /// <param name="fileName">文件名（不含扩展名）</param>
        /// <param name="exportPath">导出目录（默认游戏根目录下的 ExportedModels）</param>
        /// <param name="lodLevel">要导出的 LOD 级别（-1=全部, 0=最高, 1=中, 2=低）</param>
        /// <param name="exportTextures">是否导出纹理（需设置为 true 才会生成 PNG）</param>
        public static void ExportToOBJ(GameObject gameObject, string fileName,
                                       string exportPath = null, int lodLevel = 0,
                                       bool exportTextures = false)
        {
            MelonLogger.Msg($"ExportToOBJ 调用: {gameObject?.name}, exportTextures={exportTextures}");

            if (gameObject == null)
            {
                MelonLogger.Error("导出失败：GameObject 为空");
                return;
            }

            if (string.IsNullOrEmpty(exportPath))
                exportPath = Path.Combine(Application.dataPath, "..", "ExportedModels");

            if (!Directory.Exists(exportPath))
                Directory.CreateDirectory(exportPath);

            // ======== 1. 处理所有 LODGroup，强制激活指定 LOD ========
            List<LODGroup> lodGroups = new List<LODGroup>();
            List<bool> lodOriginalEnabled = new List<bool>();

            var allLODs = gameObject.GetComponentsInChildren<LODGroup>(true);
            foreach (var lod in allLODs)
            {
                LOD[] lods = lod.GetLODs();
                if (lods == null || lods.Length == 0) continue;

                lodGroups.Add(lod);
                lodOriginalEnabled.Add(lod.enabled);

                if (lodLevel >= 0)
                {
                    lod.enabled = false;
                    foreach (var lodData in lods)
                        foreach (var renderer in lodData.renderers)
                            if (renderer != null) renderer.gameObject.SetActive(false);

                    int targetIndex = Mathf.Clamp(lodLevel, 0, lods.Length - 1);
                    foreach (var renderer in lods[targetIndex].renderers)
                        if (renderer != null) renderer.gameObject.SetActive(true);
                }
            }

            try
            {
                // ======== 2. 收集所有激活的 Renderer ========
                var renderers = gameObject.GetComponentsInChildren<Renderer>(false);
                MelonLogger.Msg($"共找到 {renderers.Length} 个处于激活状态的 Renderer");

                List<Mesh> meshes = new List<Mesh>();
                List<Transform> transforms = new List<Transform>();
                List<Material> materials = new List<Material>();
                List<string> meshNames = new List<string>();

                // 用于存储材质的所有纹理属性及其导出的文件名
                Dictionary<Material, Dictionary<string, string>> materialTextureMap = new Dictionary<Material, Dictionary<string, string>>();

                foreach (var renderer in renderers)
                {
                    if (renderer == null) continue;

                    Mesh meshToExport = null;
                    Transform transform = renderer.transform;

                    SkinnedMeshRenderer smr = renderer.GetComponent<SkinnedMeshRenderer>();
                    if (smr != null && smr.sharedMesh != null)
                    {
                        Mesh bakedMesh = new Mesh();
                        smr.BakeMesh(bakedMesh);
                        if (bakedMesh.vertexCount > 0)
                            meshToExport = bakedMesh;
                        else
                            meshToExport = smr.sharedMesh;
                    }
                    else
                    {
                        MeshRenderer mr = renderer.GetComponent<MeshRenderer>();
                        if (mr != null)
                        {
                            MeshFilter mf = renderer.GetComponent<MeshFilter>();
                            if (mf != null && mf.sharedMesh != null)
                                meshToExport = mf.sharedMesh;
                        }
                    }

                    if (meshToExport != null && meshToExport.vertexCount > 0)
                    {
                        meshes.Add(meshToExport);
                        transforms.Add(transform);
                        materials.Add(renderer.sharedMaterial);
                        meshNames.Add(renderer.gameObject.name);
                    }
                }

                if (meshes.Count == 0)
                {
                    MelonLogger.Warning("导出失败：没有找到任何可导出的网格");
                    return;
                }

                // ======== 3. 导出所有纹理（如果启用） ========
                if (exportTextures)
                {
                    MelonLogger.Msg("开始导出所有纹理...");
                    int totalTextureCount = 0;
                    Dictionary<Texture, string> exportedTextures = new Dictionary<Texture, string>();

                    foreach (var mat in materials)
                    {
                        if (mat == null)
                        {
                            MelonLogger.Warning("材质为空，跳过");
                            continue;
                        }

                        var allTex = GetAllTexturesWithNames(mat);
                        if (allTex.Count == 0)
                        {
                            MelonLogger.Warning($"材质 {mat.name} 没有任何纹理属性，跳过");
                            continue;
                        }

                        var texMap = new Dictionary<string, string>();
                        foreach (var (propName, tex) in allTex)
                        {
                            if (!exportedTextures.TryGetValue(tex, out string texFileName))
                            {
                                string texName = tex.name;
                                foreach (char c in Path.GetInvalidFileNameChars())
                                    texName = texName.Replace(c, '_');
                                texFileName = texName + ".png";
                                string savePath = Path.Combine(exportPath, texFileName);

                                MelonLogger.Msg($"正在导出纹理：{tex.name} ({tex.width}x{tex.height}) -> {texFileName}");
                                if (SaveTextureToPNG(tex, savePath))
                                {
                                    exportedTextures[tex] = texFileName;
                                    MelonLogger.Msg($"✓ 导出纹理成功：{texFileName}");
                                    totalTextureCount++;
                                }
                                else
                                {
                                    MelonLogger.Warning($"⚠ 导出纹理失败：{tex.name}");
                                    continue;
                                }
                            }
                            texMap[propName] = texFileName;
                        }
                        if (texMap.Count > 0)
                            materialTextureMap[mat] = texMap;
                    }
                    MelonLogger.Msg($"纹理导出完成，共导出 {totalTextureCount} 个纹理（去重后）");
                }

                // ======== 4. 生成 OBJ 和 MTL ========
                StringBuilder objContent = new StringBuilder();
                StringBuilder mtlContent = new StringBuilder();

                string objFileName = fileName + ".obj";
                string mtlFileName = fileName + ".mtl";

                objContent.AppendLine($"# Exported from Unity Mod");
                objContent.AppendLine($"# Object: {gameObject.name}");
                objContent.AppendLine($"mtllib {mtlFileName}");

                int vertexOffset = 1;
                int uvOffset = 1;
                int normalOffset = 1;

                for (int i = 0; i < meshes.Count; i++)
                {
                    Mesh mesh = meshes[i];
                    Transform transform = transforms[i];
                    Material mat = materials[i];

                    objContent.AppendLine($"\ng {meshNames[i]}");

                    Vector3[] vertices = mesh.vertices;
                    Vector3[] normals = mesh.normals;
                    Vector2[] uv = mesh.uv;

                    Matrix4x4 localToWorld = transform.localToWorldMatrix;
                    foreach (var v in vertices)
                    {
                        Vector3 worldPos = localToWorld.MultiplyPoint(v);
                        objContent.AppendLine($"v {worldPos.x} {worldPos.y} {worldPos.z}");
                    }

                    if (uv.Length > 0)
                    {
                        foreach (var u in uv)
                            objContent.AppendLine($"vt {u.x} {u.y}");
                    }

                    if (normals.Length > 0)
                    {
                        Matrix4x4 normalMatrix = localToWorld.inverse.transpose;
                        foreach (var n in normals)
                        {
                            Vector3 worldNormal = normalMatrix.MultiplyVector(n).normalized;
                            objContent.AppendLine($"vn {worldNormal.x} {worldNormal.y} {worldNormal.z}");
                        }
                    }

                    int[] triangles = mesh.triangles;
                    for (int t = 0; t < triangles.Length; t += 3)
                    {
                        int i1 = triangles[t] + vertexOffset;
                        int i2 = triangles[t + 1] + vertexOffset;
                        int i3 = triangles[t + 2] + vertexOffset;

                        if (uv.Length > 0 && normals.Length > 0)
                            objContent.AppendLine($"f {i1}/{i1}/{i1} {i2}/{i2}/{i2} {i3}/{i3}/{i3}");
                        else if (uv.Length > 0)
                            objContent.AppendLine($"f {i1}/{i1} {i2}/{i2} {i3}/{i3}");
                        else if (normals.Length > 0)
                            objContent.AppendLine($"f {i1}//{i1} {i2}//{i2} {i3}//{i3}");
                        else
                            objContent.AppendLine($"f {i1} {i2} {i3}");
                    }

                    vertexOffset += vertices.Length;
                    uvOffset += uv.Length;
                    normalOffset += normals.Length;

                    // ---- 生成 MTL 材质 ----
                    if (mat != null)
                    {
                        string matName = meshNames[i];
                        mtlContent.AppendLine($"newmtl {matName}");
                        mtlContent.AppendLine($"Ka 0.5 0.5 0.5");
                        mtlContent.AppendLine($"Kd {mat.color.r} {mat.color.g} {mat.color.b}");
                        mtlContent.AppendLine($"Ks 0.5 0.5 0.5");
                        mtlContent.AppendLine($"Ns 10.0");
                        mtlContent.AppendLine($"d {mat.color.a}");

                        // ---- 纹理输出（按标准 MTL 格式） ----
                        if (exportTextures && materialTextureMap.TryGetValue(mat, out var texMap))
                        {
                            // 先输出映射表中的标准属性
                            // 使用映射表将纹理属性名转换为 MTL 关键字
                            foreach (var kv in texMap)
                            {
                                string propName = kv.Key;
                                string texFile = kv.Value;
                                if (TexturePropertyToMTLMap.TryGetValue(propName, out string mtlKeyword))
                                {
                                    mtlContent.AppendLine($"{mtlKeyword} {texFile}");
                                }
                                else
                                {
                                    // 未映射的属性，作为注释保留
                                    mtlContent.AppendLine($"# Texture: {propName} -> {texFile}");
                                }
                            }

                            // 确保漫反射纹理（map_Kd）存在，如果映射表中没有 _MainTex 或 mainTexture，但材质有主纹理，则单独添加（但主纹理已经在映射中）
                            // 这里不需要额外处理，因为映射表已经包含。
                        }
                        mtlContent.AppendLine("");
                    }
                }

                // ======== 5. 写入文件 ========
                string objPath = Path.Combine(exportPath, objFileName);
                string mtlPath = Path.Combine(exportPath, mtlFileName);

                File.WriteAllText(objPath, objContent.ToString());
                File.WriteAllText(mtlPath, mtlContent.ToString());

                MelonLogger.Msg($"✅ 模型已导出到：{objPath} （包含 MTL）");
                if (exportTextures)
                {
                    MelonLogger.Msg($"纹理文件保存在同一目录：{exportPath}");
                }
                else
                {
                    MelonLogger.Msg("未导出纹理（exportTextures = false）");
                }
            }
            finally
            {
                // ======== 6. 恢复 LODGroup 原始状态 ========
                for (int i = 0; i < lodGroups.Count; i++)
                    lodGroups[i].enabled = lodOriginalEnabled[i];
            }
        }

        /// <summary>
        /// 获取 Material 中所有已赋值的纹理，返回属性名和纹理的元组列表。
        /// </summary>
        public static List<(string propertyName, Texture texture)> GetAllTexturesWithNames(Material mat)
        {
            var result = new List<(string, Texture)>();
            if (mat == null) return result;

            string[] propertyNames = mat.GetTexturePropertyNames();
            foreach (string name in propertyNames)
            {
                Texture tex = mat.GetTexture(name);
                if (tex != null)
                    result.Add((name, tex));
            }
            return result;
        }

        // ---------- 辅助方法：导出纹理为 PNG ----------
        private static bool SaveTextureToPNG(Texture texture, string savePath)
        {
            if (texture == null)
            {
                MelonLogger.Warning("纹理为空");
                return false;
            }

            try
            {
                int width = texture.width;
                int height = texture.height;
                MelonLogger.Msg($"  开始导出纹理: {texture.name}, 尺寸: {width}x{height}");

                RenderTexture tempRT = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.Default, RenderTextureReadWrite.Default);
                Graphics.Blit(texture, tempRT);
                MelonLogger.Msg("  Blit 完成");

                Texture2D readableTex = new Texture2D(width, height, TextureFormat.RGBA32, false);
                RenderTexture.active = tempRT;
                readableTex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                readableTex.Apply();
                MelonLogger.Msg($"  ReadPixels 完成，像素数: {width * height}");

                byte[] pngData = ImageConversion.EncodeToPNG(readableTex);
                if (pngData == null || pngData.Length == 0)
                {
                    MelonLogger.Warning("  EncodeToPNG 返回空数据");
                    return false;
                }
                MelonLogger.Msg($"  PNG 编码完成，大小: {pngData.Length / 1024} KB");

                File.WriteAllBytes(savePath, pngData);
                MelonLogger.Msg($"  ✅ 纹理写入完成: {savePath}");

                RenderTexture.active = null;
                RenderTexture.ReleaseTemporary(tempRT);
                UnityEngine.Object.Destroy(readableTex);

                return true;
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"  导出纹理异常: {ex.Message}\n{ex.StackTrace}");
                return false;
            }
        }
    }
}