# 角色模型和骨骼导出

本工具读取游戏本地 YooAsset bundle，使用本机 AssetStudio 0.16.53 导出带蒙皮的二进制 FBX。

流程：

1. `map_models.py` 解析 YooAsset 2.0.0 manifest 的资产、bundle、依赖关系，定位磁盘资源。
2. `Offline` 项目筛选 `character_01_skin` 至 `character_39_skin`，加载主包和递归依赖。对每个含 SkinnedMeshRenderer 的根节点导出 FBX、可识别的关联 PNG 贴图、节点层级 JSON 和统计 JSON。保留原始高细节模型，不选择 LOD1/2 包。
3. `validate_fbx.py` 独立解析生成的二进制 FBX，验证网格、骨骼节点、Skin/Cluster、索引和权重数组、绑定矩阵。
4. `package.py` 仅打包通过导出及文件验证的模型，并生成 CSV 与 SHA256。

映射命令：`python tools/ModelExport/map_models.py [--game-data PATH] [--manifest PATH] [--output PATH] [--verify]`。默认生成全部候选的尺寸校验映射；`--verify` 额外检查 MD5/CRC32。已有映射的39个本体及依赖可用 `python tools/ModelExport/verify_models.py` 独立校验。

```powershell
dotnet run --project tools/ModelExport/Offline/Offline.csproj -c Release -- "C:/Users/86156/Desktop/工具/mod/artifacts/model-export-20261122"
# 对无有效蒙皮权重的附件，补导为刚性网格：
dotnet run --no-build --project tools/ModelExport/Offline/Offline.csproj -c Release -- "C:/Users/86156/Desktop/工具/mod/artifacts/model-export-20261122" --retry-rigid
# 使用会话 Python 执行 tools/ModelExport/validate_fbx.py 和 package.py
```

依赖使用本机工具目录现有 AssetStudio DLL；项目把 x64 原生 DLL 复制到自己的输出目录。请使用 `dotnet run` 或生成的 EXE，避免使用 `dotnet Offline.dll` 导致 AssetStudio 原生预加载器的路径错误。

导出设置：完整节点层级、蒙皮、blend shape（源模型存在时）、缩放1、FBX版本选项3、二进制。动画未启用。每个角色源FBX可能把身体和头饰分开；输出沿用原始根节点名。

检查范围：源模型蒙皮权重非零、权重和接近1、骨骼索引有效；生成FBX的几何与骨骼和绑定矩阵结构有效。未进行 Blender/Maya 中的人工姿态或材质复现测试。游戏自定义 Shader 和描边效果需在目标软件中调整。

没有修改游戏资源或运行时角色状态。
