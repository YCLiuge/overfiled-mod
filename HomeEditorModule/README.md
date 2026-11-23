# HomeEditorModule

家园编辑实现独立为 `home_editor` 热更新模块，入口 `HomeEditorModule.Entry : ModLoader.ILogic`，优先级 90，产物 `GameMods/Mods/HomeEditorModule.dll`。模块引用宿主 `mod.dll` 的类型身份和通用数据，不嵌入或创建网络客户端。

## 迁移边界

`FreeSetHomeItem`、`AutoHomeItem` 的编辑状态机、`Fksys/Fk_fanction/CenterTMPManager`、`Followsys`、`ItempotStorage`、编辑用 `ObjectController` 已实际迁入模块的 `HomeEditorModule.Core` 命名空间。宿主不再加载编辑补丁或直接调用编辑实现。

宿主保留 `Stute_tokensys.HomeSeting` 的原枚举位置，`Stute_AutoHomeItem`、`mod.Itempot` 数据结构，以及 `mod.AutoHomeItem.i/items/Copyitems/GetCenterAverage` 的共享船形剪贴板兼容 API；其余家园编辑代码不在宿主中。模块与船工具共用原 `List<mod.Itempot>`，卸载不清空剪贴板。宿主的兼容 `Copyitems(..., part:true)` 不承担矩形选择，矩形选择通过模块 `-cc part` 完成。`~sc`、`~emo`、`tp` 等通用宿主工具继续保留。

## 命令

只有当前角色自己的命令生效。在家园场景、玩家/地面/网格已就绪且没有其他宿主模式运行时发送 `~home`。模块不将旧宿主状态设为 `HomeSeting`，避免旧分支重复执行。

- `~stop`：退出并恢复编辑会话，可重复执行。
- `-fk`：网格/旋转编辑；`setstep`、`setrostep`、`setbp`、`sq` 保留原子模式命令。
- `-fk` 内发送 `-cam` 或按 F6：切换相机与角色分离；`-cam on` / `-cam off` 显式开启/关闭，默认关闭。开启后，相机观察点不再跟随角色，方向键按相机水平朝向移动观察点，PageUp/PageDown 升降，速度为每秒 5 单位；鼠标视角和滚轮缩放继续由游戏相机控制。WASD、Ctrl/Shift 组合键及 `sq` 仍控制角色，角色移动和旋转不改变相机观察点。`-fk` 中禁用数字 3 的共享飞行切换，避免方向键同时驱动角色。关闭分离、`-end`、`~stop`、切场景或模块卸载时恢复先前的相机跟随目标并销毁观察点；若目标已销毁则回到当前角色。宿主 F5 自定义相机已开启时不能开启分离；分离期间开启 F5 会结束分离并移交观察目标。
- `-fl 名字`：保留原跟随摆放模式，会真实放置家具。
- `-cc`：复制当前家具；`-cc part`：切换矩形辅助框；`-set`：设置参考角。
- `-cp [t]`：本地预览后粘贴，Tab 确认，Escape 取消。确认后会真实放置家具。预览只建立本地 MeshFilter/MeshRenderer 对象，不注册家具、不复制游戏脚本或生成服务器家具 ID；当前场景没有相同家具模板时该条目的预览为空，但位置仍可调整。蒙皮等非静态网格不生成预览。
- `-sv [Y] 名称`、`-rd [Y] 名称`：在原 `mod储存数据/` 目录保存/读取配置；不存在或空配置不会抹掉现有剪贴板。
- `-ck`：列出保存的文件；`-end`：取消当前会话全部协程与临时资源，恢复后重新进入家园编辑基态。

相机分离的独立验证：`dotnet run --project tools/FkCameraVerification/FkCameraVerification.csproj -c Release`，直接链接生产 `FkCamera.cs`，使用 Unity/相机替身检查启停、重复切换、目标失效回落、其他工具接管及输入隔离，共 23 项。替身验证不涵盖游戏原生镜头渲染；实机仍需检查鼠标环绕/缩放、方向键与角色互不影响，以及 `-end`、`~stop`、切场景和热卸载后正常恢复。

## 生命周期和旧宿主兼容

独立 Harmony owner 为 `mod.home_editor.runtime`。命令 Prefix 仅拦截家园命令与活动编辑子命令；通用宿主命令继续由宿主处理。模块帧由 `ILogic.OnUpdate` 驱动，不依赖家园场景中才调用的宿主事件，也不留下事件订阅。

激活前先校验依赖，再保存原 parent、localPosition/localRotation/localScale 和 activeSelf；退出恢复实际原值，不硬编码原点或单位缩放。普通放置临时隐藏只保存可见性，不回滚玩家已确认的家具放置位置。退出、切场景（scene.handle 变化，包括同名重载）、宿主切换模式、卸载和异常都会取消本 owner 的协程，恢复临时隐藏对象、相机和变换，销毁辅助网格克隆/矩形/中心 UI/纯本地预览并重置子状态和锁。粘贴协程在每次恢复执行前校验会话与场景，避免跨场景继续放置。

运行中的旧宿主仍含旧编辑代码。加载模块时，仅精确移除旧宿主 `mod.FreeSetHomeItem+comUIHomeSystem_OnClickPutdown` 的已知 Prefix，并接管家园命令；卸载时恢复原 Prefix，其他宿主补丁保持原样。此兼容逻辑允许当前旧宿主热载验证。新宿主已完全移除旧实现，重启后不再存在两套编辑代码。不要把新宿主构建产物直接覆盖运行中的游戏文件；安装新宿主仍需退出游戏替换并重启。

## 构建和验证接口

```powershell
dotnet build .\mod\mod.sln -c Release -p:Platform=x64
```

共享 `build/HotUpdate.targets` 自动生成/刷新 `GameMods/Mods/version.json`，包含模块 ID、版本、优先级和 SHA256，供现有 LogicLoader/更新器加载。构建只写工作区输出目录。

只读探测：`HomeEditorModule.Entry.Diagnostics()` 返回 JSON，包括 `active`、`generation`、`scene`、`coroutineCount`、`objectCount`、`snapshotCount`、`mode`、`clipboardCount`、`cameraSeparated`、`lastError`。它不激活编辑、不放置家具、不改变游戏状态。命令经正常 `TipMsgCore.command` 路径验证。

建议实机顺序：加载 → `~home`（重复不新增资源）→ `-cc` → 读取不存在配置（剪贴板不变）→ `~stop`（重复无害）→ 再激活 → `-cc part` → 热卸载 → 等待至少一秒确认资源恢复 → 重载再激活/停止。此序列不执行粘贴或跟随真实摆放；真实摆放与场景切换需在明确允许修改家园的环境中另外验证。

2026-11-13 最终 Release x64 构建成功（0 errors）。最终产物已通过 14 项元数据验证及 13 项真实游戏验证：进入/重复进入、共享复制、缺失配置保留剪贴板、停止/重复停止、`-end` 清理子模式并回到家园基态、矩形协程中热卸载及延迟检查、重载后再次进入退出。退出前后喷泉和网格的位置、缩放、activeSelf 与基线一致，测试结束恢复原共享剪贴板。记录见 `artifacts/home-final-metadata.log` 和 `artifacts/home-final-native.log`。

最终模块 SHA256：`2C0FBA9DF6648647A3A98D3CE50CF1A9CE320CE9EE3D1235575660DF067249D3`；配套新宿主 SHA256：`4D98DFC9B51447053DF0D70E46FB96FD2F0162227B8E422CC6614598A5516A69`。模块已部署至游戏 `Mods/cache/home_editor.dll` 并登记本地 `manifest.json`，当前进程已热载验证。运行中的宿主仍为之前的版本；退出游戏后替换新宿主并重启，才能在宿主二进制中彻底去除旧家园代码。实机验收未执行真实家具放置/批量粘贴，也未强制切换用户场景。
