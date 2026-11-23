using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace mod.tool
{
    public static class OBJLoader
    {
        public static Mesh LoadOBJ(string objPath)
        {
            if (!File.Exists(objPath))
            {
                MelonLoader.MelonLogger.Error($"OBJ 文件不存在：{objPath}");
                return null;
            }

            string[] lines = File.ReadAllLines(objPath);

            List<Vector3> vertices = new List<Vector3>();
            List<Vector2> uvs = new List<Vector2>();
            List<Vector3> normals = new List<Vector3>();
            List<int> triangles = new List<int>();

            int lineNum = 0;
            foreach (string rawLine in lines)
            {
                lineNum++;
                string line = rawLine.Trim();
                if (string.IsNullOrEmpty(line) || line.StartsWith("#")) continue;

                // 按空格分割，同时处理多个空格
                string[] parts = line.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0) continue;

                string first = parts[0];

                if (first == "v" && parts.Length >= 4)
                {
                    float x = float.Parse(parts[1]);
                    float y = float.Parse(parts[2]);
                    float z = float.Parse(parts[3]);
                    vertices.Add(new Vector3(x, y, z));
                }
                else if (first == "vt" && parts.Length >= 3)
                {
                    float u = float.Parse(parts[1]);
                    float v = float.Parse(parts[2]);
                    uvs.Add(new Vector2(u, v));
                }
                else if (first == "vn" && parts.Length >= 4)
                {
                    float nx = float.Parse(parts[1]);
                    float ny = float.Parse(parts[2]);
                    float nz = float.Parse(parts[3]);
                    normals.Add(new Vector3(nx, ny, nz));
                }
                else if (first == "f" && parts.Length >= 4)
                {
                    // 解析面顶点索引
                    List<int> faceIndices = new List<int>();
                    for (int i = 1; i < parts.Length; i++)
                    {
                        string token = parts[i];
                        // token 可能是 "v", "v/vt", "v/vt/vn", "v//vn"
                        string[] subParts = token.Split('/');
                        if (subParts.Length == 0) continue;
                        if (int.TryParse(subParts[0], out int vi))
                        {
                            // OBJ 索引从 1 开始，转为 0-based
                            faceIndices.Add(vi - 1);
                        }
                        else
                        {
                            MelonLoader.MelonLogger.Warning($"行 {lineNum}: 无法解析顶点索引 '{token}'，跳过");
                        }
                    }

                    if (faceIndices.Count < 3)
                    {
                        MelonLoader.MelonLogger.Warning($"行 {lineNum}: 有效顶点数不足3，跳过该面");
                        continue;
                    }

                    // 三角化（扇型）
                    int firstIdx = faceIndices[0];
                    for (int i = 1; i < faceIndices.Count - 1; i++)
                    {
                        triangles.Add(firstIdx);
                        triangles.Add(faceIndices[i]);
                        triangles.Add(faceIndices[i + 1]);
                    }
                }
            }

            if (vertices.Count == 0)
            {
                MelonLoader.MelonLogger.Error("OBJ 文件没有顶点数据");
                return null;
            }

            Mesh mesh = new Mesh();
            mesh.name = Path.GetFileNameWithoutExtension(objPath);
            mesh.vertices = vertices.ToArray();
            if (uvs.Count > 0)
                mesh.uv = uvs.ToArray();
            if (normals.Count > 0)
                mesh.normals = normals.ToArray();
            else
                mesh.RecalculateNormals();
            mesh.triangles = triangles.ToArray();
            mesh.RecalculateBounds();

            MelonLoader.MelonLogger.Msg($"OBJ 加载成功：顶点 {vertices.Count}, 三角形 {triangles.Count / 3}");
            return mesh;
        }
    }
}