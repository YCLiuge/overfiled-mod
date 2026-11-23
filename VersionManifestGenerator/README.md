# version.json 统一生成程序

`mod`、`Logic`、`HotUpdateModule` 三个游戏 DLL 项目统一输出到工作区的 `GameMods/Mods`。三个项目都导入 `build/HotUpdate.targets`：构建依赖先编译本程序，DLL 构建完成后自动调用它，重新扫描整个公共输出目录并生成 `version.json`。

在工作区根目录编译全部项目：

```powershell
dotnet build .\mod\mod.sln -c Release -p:Platform=x64
```

也可以在 Visual Studio 中打开主解决方案并构建 Release / x64。单独构建任意一个游戏 DLL 项目也会刷新完整清单。生成器和 TCP 服务端属于工具程序，保留各自 bin 输出，不进入游戏 DLL 发布目录。

## 手动生成

在工作区根目录运行：

```powershell
# 自动编译生成器并刷新公共目录的清单
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Generate-Version.ps1

# 已经编译生成器时，直接调用程序，唯一参数是 DLL 目录
.\VersionManifestGenerator\bin\Release\VersionManifestGenerator.exe .\GameMods\Mods
```

程序需要 .NET 6 运行时。移动程序时应一同保留相邻的 DLL、deps.json 和 runtimeconfig.json。参数可指定其他发布目录，输出始终写入该目录的 version.json；失败返回非零退出码，使自动构建也报错。

## 模块识别和字段来源

- 永远按文件名排除 `mod.dll`，比较不区分大小写。
- 只扫描目录顶层 DLL，不递归扫描旧包或缓存。
- 新模块通过程序集中的 `HotUpdate.ModuleId` 元数据识别。SDK 在导入共享 targets 后自动将项目的模块属性写入 DLL。
- 兼容旧的、没有模块元数据但直接实现 `ModLoader.ILogic` 的程序集：优先从现有清单按 DLL 文件名保留 ID、优先级、enabled 和 notes；没有旧记录时使用程序集名作为 ID。
- 其他依赖 DLL 和原生 DLL不会进入清单。
- 版本来自 `AssemblyInformationalVersionAttribute`，去掉 `+commit` 后缀；不存在时读取程序集版本。必须能够解析为数字版本。
- `hash` 是当前 DLL 内容的 SHA256，使用小写十六进制。元数据和哈希取自同一份字节快照。
- `url` 是 DLL 相对于服务端根目录的文件名，例如 `Logic.dll`。服务地址和端口仍由宿主更新器配置决定。

现在的业务模块配置：

| 项目 | ModuleId | Version | ModulePriority |
| --- | --- | --- | --- |
| Logic | logic_main | 1.0.1 | 10 |
| HotUpdateModule | hot_update_demo | 1.0.0 | 100 |

在对应 csproj 中修改 `Version`、`ModuleId`、`ModulePriority`。可选 `ModuleEnabled` 默认 true，`ModuleNotes` 默认不设置。元数据存在时以项目配置为准，因此需要持久修改 enabled 或优先级时，应修改项目属性，而非只修改生成的清单。

新增同级模块项目时，配置模块属性并导入共享构建文件即可接入：

```xml
<PropertyGroup>
  <Version>1.0.0</Version>
  <ModuleId>another_logic</ModuleId>
  <ModulePriority>200</ModulePriority>
  <ModuleEnabled>true</ModuleEnabled>
</PropertyGroup>
<!-- 放在项目文件末尾、</Project> 之前 -->
<Import Project="..\build\HotUpdate.targets" />
```

新项目仍需引用宿主提供的 ILogic，并按需添加游戏依赖。不要复制宿主契约源码。

## 多项目构建与发布

生成器使用跨进程文件锁，将扫描和发布串行执行；临时 JSON 完整写入后在同目录重命名覆盖，UTF-8 无 BOM。重复模块 ID 会直接报错，并保留上一次成功的清单。删除模块 DLL 后，下一次扫描会移除对应条目。清单按优先级、模块 ID 排序。

公共目录中的 DLL 使用固定文件名。请在构建全部完成后触发客户端 update，避免构建期间客户端读取到新 DLL 和旧清单而校验失败。TCP 服务端根目录设为整个 `GameMods/Mods`；可直接使用 HotUpdateModule/Start-Server.ps1，它默认使用端口 10086 和这个共享目录。

## 验证

在工作区根目录运行：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\VersionManifestGenerator\Verify.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\HotUpdateModule\Verify.ps1
```

前者验证新目录生成、宿主/依赖排除、SHA256、无 BOM、并发生成、重复 ID 失败保留旧清单和删除模块。后者运行原 TCP 服务端源码并调用实际宿主 DLL 的下载、路径、哈希、版本比较和入口检查方法。它们不启动游戏，不代替实际 Harmony/Unity 生命周期测试。
