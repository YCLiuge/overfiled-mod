# GameOptimizerModule 1.2.1

Unity IL2CPP / MelonLoader 热更新模块，目标 .NET 6 / x64，使用宿主 `ModLoader.ILogic`，模块 ID `game_optimizer`，优先级 50。

## 性能面板

面板默认隐藏，位于右上角，使用固定像素缩放，随屏幕高度缩至 65%–100%。保留自定义按键：F9 显示/隐藏，F10 切换紧凑/详情。界面不接收鼠标点击，不创建 EventSystem。热卸载会清理自有 UI、字体和补丁。

- 每 0.5 秒刷新。FPS 是最近最多 512 个有效帧的总帧数除以总时间；平均帧时间和 nearest-rank P95 包含加载卡顿。隐藏时继续采样。
- 显示画质档位、目标帧率、前后台状态、优化暂停状态和 Unity 分配内存。
- 详情显示 .NET 托管堆、Unity 保留内存、.NET 各代 GC 累计次数。这些计数不是进程总内存，也不代表所有 IL2CPP 堆内存；不可用显示 N/A。
- `Bundles` 是每 5 秒枚举获得的实际已加载 Bundle 数量，与缓存条目数分开。
- `async req` 是本模块启用监控后观察到的异步资源加载提交次数，不表示完成次数或完成耗时。
- `Sync req/done` 是观察到的同步请求/正常返回次数，正常返回可包含 null。异常退出不计为完成。`Sync ms total/max/last` 是这些调用的累计/最长/最近一次耗时。
- 加载监控挂在 `AssetBundle.LoadAsset_Internal` 和 `LoadAssetAsync_Internal`，不保证覆盖其他批量加载入口或所有绕过该入口的游戏原生调用。挂钩失败显示 unavailable 并记录告警，不影响原调用。
- `Helper cache` 的 hit/miss 仅对应 `Logic.BundleHelper.ListAssets(bundle, false)` 查询；正常游玩时均为 0 可能只是没有使用 Bundle 工具，不代表游戏没有加载资源。统计不追溯模块加载前的请求。

## 实际帧设置与 GPU 时间

`Unity cap` 每 0.5 秒读取实际 Application.targetFrameRate，与策略中的 target 分开显示；非正值显示 default(原值)，不推断为无限帧。`VSync` 是当前 QualitySettings.vSyncCount。`Display` 使用当前显示器刷新率；VSync ceiling 为刷新率除以 vSyncCount 的理论值，关闭 VSync 显示 OFF。这些是 Unity 当前设置，无法检测驱动、系统节能或游戏内部其他限帧器。

`GPU` 来自 FrameTimingManager.gpuFrameTime，`CPU timing` 来自 cpuFrameTime；逐帧请求采样，0.5 秒读取一次最近结果，不是 512 帧平均，也不是 Stopwatch 推算值。只有新的有效时间戳与有限正数才更新；超过 2 秒没有新数据、API 异常或不支持时显示 N/A。部分游戏构建未启用 Frame Timing Stats 或驱动不支持，加载模块无法保证开启这些构建功能。CPU timing 也不等于主线程脚本独占耗时。

1.2.1 将 GPU N/A 分为具体原因：`no samples` 表示 GetLatestTimings 返回 0；`no GPU data` 表示有记录但 GPU 时间不是有限正数；`no timestamp` 表示 GPU 值为正但两个可用时间戳均为 0，无法验证新鲜度；`stale` 表示已无新鲜有效数据；`API error` 表示采样接口抛异常。最初 2 秒为启动等待期，之后只记录一次详细警告，包含 returned、rawGpuMs/rawCpuMs、complete/present 时间戳和 gpuHz/cpuHz。频率查询为 0 或失败不单独作为“不支持”的判据，非零也不能证明 Frame Timing Stats 已启用。后续有效数据到来仍可恢复显示（采样 API 抛异常除外）。

收到 `no samples` 时，仅凭日志无法区分构建禁用与平台不支持。检查实际游戏构建/图形后端才能确认；本模块不修改游戏启动参数或构建文件，不承诺热加载可以开启构建统计。此警告不影响 URP 比例、帧率策略或其他监控。

## 可调渲染比例

在现有配置中加入 `"RenderScale": 0.75`，每 2 秒检查的配置重读将应用它；无需改变快捷键。默认 0 表示完全交由游戏管理，其余允许 0.5–1.0，独立于自适应档位。

通过实际当前 URP asset 的 renderScale 调整场景渲染目标尺寸，不改变窗口分辨率。0.75 为宽高各 75%，理论像素数约为原来的 56.25%，最终效果仍取决于管线及自定义渲染。ScreenSpaceOverlay 面板不随场景比例变糊，游戏自己的 UI 是否受影响取决于其相机设置。建议先试 0.85 或 0.75，对比同场景 FPS 与 GPU 时间；CPU/限帧瓶颈可能无明显提升。

面板展示实际 `URP scale`、请求值与 applied/game/game override 状态。只有 URP 支持此项；Built-in、HDRP 或其他不匹配管线显示 unsupported，不伪报生效，也不偷偷改变屏幕分辨率。每秒检测当前管线，切换时恢复旧 asset 并应用到新 asset；不每帧抢写。游戏后续覆盖该值时显示 game override，重新编辑有效配置可再次请求。设置 0、Enabled=false、配置重读、优化暂停或卸载时，恢复仍等于模块最后写入值的设置，保留外部改动。正常采样/应用错误会隔离该功能并显示 N/A；配置或卸载时的恢复错误按现有设置恢复机制向生命周期处理器传播，暂停优化并尝试回滚，失败项保留以供重试。

## AssetBundle 优化

`BundleMetadataCache=true` 为仓库现有 Bundle 工具的重复资源名查询加入 5 秒缓存，避免反复跨 IL2CPP 读取同一 Bundle 的完整资源名。最多 64 个 Bundle、32768 个资源名；返回数组采用防御复制，防止调用方污染缓存。显式打印资源列表仍执行原路径，保留日志语义。缓存只保存字符串，不保留资源实例或 Bundle 引用。

使用 Bundle native pointer 和 Unity instance ID 区分对象；缓存过期、Bundle Unload/UnloadAsync/UnloadAll、主逻辑模块卸载或重新配置时失效。模块监听 logic_main 热重载，重新挂接新方法。卸载钩子不可用时禁用缓存。游戏原生侧绕过公开卸载入口的行为无法完整观测，5 秒过期限制缓存滞留；本模块不自动卸载游戏资源，不改变资源所有权，不复用 LoadAsset 的返回实例。

`OptimizeAsyncLoading=true` 将 `Application.backgroundLoadingPriority` 设为 Low，将 `QualitySettings.asyncUploadTimeSlice` 上限降至 2ms。目标是减少异步资源集成和上传期间占用主线程的预算，可能增加总加载时间；对同步 LoadAsset 和不经过 Unity 对应通道的工作不保证有效。这两个全局设置可能影响其他异步加载，关闭该选项可保留原值。

## 画质与后台降耗

默认前台 60 FPS、后台 15 FPS，关闭垂直同步。初始档位 1；每 3 秒采样，连续 2 个窗口低于目标 80% 时降低画质一级，连续 5 个窗口达到目标 95% 时恢复一级，每次变化后冷却 10 秒。画质策略排除后台、切换焦点、超过 0.5 秒的停顿和非法时间值；面板统计仍包含有效长停顿。

| 档位 | 阴影距离 | 阴影分辨率 | MSAA | LOD Bias |
| --- | --- | --- | --- | --- |
| 0 | 原值 | 原值 | 原值 | 原值 |
| 1 | 60 | High | 2x | 1.0 |
| 2 | 35 | Medium | 关闭 | 0.7 |
| 3 | 15 | Low | 关闭 | 0.45 |

数值为原值与上限的较低者，档位 0 恢复原始画质。URP/HDRP、自定义管线及未配置 LOD 的模型可能忽略对应设置。关闭垂直同步可能撕裂。CPU/网络/逻辑瓶颈不保证随降画质改善。

禁用、有效配置重读或卸载时恢复仍由模块控制的设置：当前值必须等于模块最后写入值，才会恢复原值，以保留外部修改。无法识别外部恰好写入相同值的操作。Unity 设置异常后暂停优化并尝试恢复，重新编辑有效配置可重试。面板异常单独停用，避免影响优化逻辑。每帧不强制 GC，不改网络、物理、时间缩放或战斗逻辑。

## 配置

首次加载在游戏进程当前工作目录创建 `UserData/GameOptimizer.json`。旧配置未填写新增选项时使用默认值：

```json
{
  "Enabled": true,
  "BundleMetadataCache": true,
  "OptimizeAsyncLoading": true,
  "RenderScale": 0,
  "TargetFps": 60,
  "BackgroundFps": 15,
  "DisableVSync": true,
  "AdaptiveQuality": true,
  "InitialTier": 1,
  "MaxTier": 3,
  "SampleSeconds": 3,
  "CooldownSeconds": 10,
  "SlowWindows": 2,
  "FastWindows": 5
}
```

每 2 秒检查修改时间，配置最大 16KB；非法编辑保留原配置，下一次编辑重试。Enabled=false 恢复设置并停用缓存，面板及监控仍可用。只想观察原画质时可设置 Enabled=false。

TargetFps 允许 30–240，BackgroundFps 允许 1–TargetFps；InitialTier/MaxTier 允许 0–3 且初始不超过最大；SampleSeconds 允许 1–30；CooldownSeconds 允许 3–300；SlowWindows 允许 1–20；FastWindows 允许 1–60。AdaptiveQuality=false 固定初始档位；DisableVSync=false 前台保留原垂直同步，后台仍暂时关闭。

## 构建与加载

仓库根目录执行：

```powershell
$env:MSBuildEnableWorkloadResolver='false'
$env:DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE='true'
dotnet build GameOptimizerModule/GameOptimizerModule.csproj -c Release
dotnet run --project tools/GameOptimizerVerification/GameOptimizerVerification.csproj -c Release
```

生成 `GameMods/Mods/GameOptimizerModule.dll` 并自动更新完整 `GameMods/Mods/version.json` 的版本、SHA-256，保留其他模块记录。将 DLL 和完整清单通过现有热更新服务发布；不要将它当独立 MelonMod 使用。宿主主线程也可手动加载：

```csharp
ModLoader.LogicLoader.Instance.LoadModuleFromFile(
    "game_optimizer", @"实际绝对路径\GameOptimizerModule.dll", 50);
```

相同 ID 再次加载时会先卸载旧实例。依赖仓库 libs 中的 Unity、Harmony 和 MelonLoader，不复制这些依赖到模块产物。

## 验证与游戏验收

策略验证程序通过 60 项检查（新增渲染比例范围及恢复/外部覆盖保护），GPU 诊断另有 26 项隔离检查（包含缺失原因、频率查询失败和恢复），渲染比例控制另有 18 项生命周期/异常恢复检查，覆盖画质迟滞/冷却、前后台策略、设置恢复与失败重试、异步预算、真实 Unity ThreadPriority 枚举、缓存容量/过期/复制/失效、帧窗口/P95/卡顿。监控还通过隔离测试验证采样节流、计数上限、状态宽度、异常恢复和清理。Release 主工程构建要求 0 警告、0 错误；测试工具有既有 .NET 6 生命周期提示。

已有游戏截图确认旧版面板与加载数量监控运行；1.2.0 新增 GPU 采样与 URP 比例仍需游戏内验证：F9/F10 保持原行为、显示实际 cap/VSync、GPU 有新数据或明确 N/A、RenderScale=0.75 后 URP scale 变为 0.75、改回 0 恢复、切换场景/管线后正确应用、热卸载恢复且无重复 UI/补丁。比较性能时使用相同场景/视角/设置，并同时关注加载耗时与帧尖峰；尚未进行可量化的游戏性能对比，不声称固定提升比例。
