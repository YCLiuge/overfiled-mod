# 立绘导出

本工具复用 `Logic/BundleHelper.cs` 的 `bundle export` 实现，仅通过独立程序集入口在游戏中运行，避免热重载整个 Logic 模块。

首次原有 `bundle export` 调用匹配到两张立绘，但 `LoadAsset<Object>` 返回的托管包装类型为 `Object`，导致只输出 `.txt`。源码现已改用 `loaded.GetIl2CppType().Name` 分派到 Texture2D 导出器。当前游戏的原 Logic 程序集未替换；本次导出使用单独编译的修复版本。

构建：

```powershell
dotnet build tools/PortraitExport/PortraitExport.csproj -c Release
```

通过本机游戏指令接口依次执行（绝对路径自行替换，call 参数路径不可含空格）：

```text
call System.Reflection.Assembly.LoadFile C:/Users/86156/Desktop/工具/mod/tools/PortraitExport/bin/Release/net6.0/PortraitBundleExport.dll
call PortraitExport.Entry.Run assets_development_arts_ui_atlas_cn_resbundle_texture_character_character_01.bundle C:/Users/86156/Desktop/工具/mod/artifacts/portrait-export-20261122/character_01
```

入口内部执行的实际资源指令：

```text
bundle export assets_development_arts_ui_atlas_cn_resbundle_texture_character_character_01.bundle quanshen "输出目录"
```

`Export-Loaded.ps1` 导出本次已确认加载的其余角色包，必须先载入辅助程序集。`package.py` 检查全部 PNG、保留原像素与透明通道，生成短文件名的 ZIP、清单与总览预览。只包含已加载包中的 `quanshen` 全身立绘，不代表游戏所有立绘。
