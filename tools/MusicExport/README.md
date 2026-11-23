# 游戏音乐导出

本次音乐包清单由游戏中 `bundle` 辅助代码扫描已加载的 `assets_development_arts_sound_bgm_` 包获得，写入 artifacts/music-export-20261122/catalog.json。

Unity 首首试导出发现 AudioClip 是 Streaming，`GetData` 返回失败。因此改为离线读取同一批 bundle，无需更改游戏音频播放状态。

步骤：

1. `map_bundles.py` 读取 YooAsset manifest，使用名称解析文件哈希并定位磁盘包，检查大小、MD5、CRC32。
2. Offline 项目读取 disk-map.json，通过本机安装的 AssetStudio / FMOD 解码 AudioClip，逐首检查 PCM 格式、采样非零、音量，并调用本机 FFmpeg 无损压缩为 FLAC。转换成功后删除该首临时 WAV。
3. `package.py` 验证 FLAC STREAMINFO 与解码时长/声道/位深一致，生成 SHA256、清单、播放列表、ZIP。

本机复现（根目录执行）：

```powershell
# 使用当前会话的 Python 路径运行 map_bundles.py，然后执行：
dotnet run --project tools/MusicExport/Offline/Offline.csproj -c Release -- "C:/Users/86156/Desktop/工具/mod/artifacts/music-export-20261122" "C:/Users/86156/Desktop/测试/新建文件夹/JianyingPro/11.6.0.14552/ffmpeg.exe"
# 最后使用 Python 执行 tools/MusicExport/package.py
```

请通过 `dotnet run` 或编译生成的 Offline.exe 启动；AssetStudio 的原生库预加载器依赖宿主进程所在目录，直接使用 `dotnet Offline.dll` 会出现原生库定位失败。

Logic/BundleHelper.cs 同时修正了运行时音频导出的两个问题：GetData 使用显式 IL2CPP 数组并读回托管数组；读取失败时不再创建静音 WAV。游戏中的原 Logic 模块未替换。Streaming 类型仍需离线提取。

FLAC 保留本次解码产生的采样数据，不代表游戏源编码是无损格式。
