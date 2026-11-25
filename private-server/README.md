# 开放空间本地私服实验

当前版本 `local-sandbox-17`：补齐星云树徽章（MoonStone）页的列表与节点激活协议。沙盒打开页面时按真实配置补齐 8 页、198 个有效节点并持久保存；重新打开和重复点击不扣材料、不重复发奖。此前的 79 项 PlayerAbility 是另一套能力系统，不能替代截图中的徽章页。未能验证奖励池映射的整页/成就领奖请求明确返回失败，避免等待无回应或发错奖励。

此前版本 `local-sandbox-16`：完善账号密码与自动登录，兼容 SDK 两种令牌字段格式，自动登录使用数据库持久凭据并刷新令牌；错误密码、空输入和跨账号令牌明确拒绝。旧角色缺少本地 SDK 账号时，可离线创建绑定原 SDK UID 的恢复凭据，已有密码与角色归属保持不变，详见下方“本地账号恢复”。沙盒登录补齐 PlayerAbility 全部 79 个能力节点、389 项逐级奖励和互动工具“空手”（1001000），旧存档自动迁移，重新登录获取解锁状态，重复登录不累加道具。

## 客户端报错遮罩

客户端重定向插件 `0.3.2` 默认隐藏游戏内 `OutLog` 的错误堆栈、背景和反馈按钮，保留原生错误收集与日志保存。此功能仅影响提示界面，不修复“初始座椅ID不能为0”等底层玩法错误。`UserData/private-server.json` 可设置 `"HideErrorOverlay": false` 恢复显示。更新 DLL 或配置后须退出并重新启动游戏；无需重启服务器。界面补丁安装失败时会单独警告，不影响重定向功能。

## 本地账号恢复

[Recover-LocalAccounts.py](Recover-LocalAccounts.py) 默认只读检查账号数量；`--apply --output-dir 私密目录` 才会写入。应用前须正常关闭服务器。脚本先备份完整数据库，为没有 OFQuick 凭据的旧 SDK UID 创建 `local_游戏UID` 账号和 16 位随机密码，显式绑定原角色；用户名冲突时增加后缀，已有账号、密码和角色映射不变。报告列出所有 SDK 账号及其游戏 UID、昵称；尚无角色的 SDK 账号和历史探针身份会注明。

输出目录包含明文密码清单、JSON 和数据库备份，请存放在已忽略的 `private-server/backups/` 下。本次报告路径见交付消息，勿上传或转发。生成的恢复密码仅适用于本地服务器，不是原平台密码。进入游戏的 SDK“账号密码”入口，按清单输入账号和密码；游戏 UID 不等于登录账号。客户端若仍有失效的原平台自动登录缓存，请返回账号登录入口使用清单凭据。此脚本需要 Python 3（仅标准库）；运行 `python private-server/Recover-LocalAccounts.py` 可先查看只读统计。

此前版本 `local-sandbox-15`：修复好友申请收件列表、同意/拒绝及双向好友状态同步；沙盒模式下开放界面中的五个染色方案，登录时补齐旧存档的方案解锁状态，保留各方案已有配色，支持分别预览、保存和穿戴。

此前版本 `local-sandbox-14`：新角色自动生成“旅行者 + UID”的默认昵称，已有空昵称账号在下次登录时补齐并保存，已有非空昵称保持原样，游戏内改名仍然可用。此修复绕过新账号未弹出创建昵称框导致的入场卡点；不代表客户端界面资源问题已修复。

此前版本 `local-sandbox-13` 新增主线与额外剧情任务完成状态开放：648条主线与103条额外任务覆盖110个主线故事和24个额外故事入口，重复接取保持完成状态，不改玩家数据库或发奖。服务端验证通过，客户端播放及额外门槛仍需实机确认，详见 [剧情开放说明](STORY-UNLOCK.md)。此前版本包含多人互动动作邀请/接受/停止、篝火放置与频道同步、六秒无消耗沙盒钓鱼、鱼获入包与统计，并修复聊天事务造成的停服存档阻塞。详见 [篝火与钓鱼](CAMPFIRE-FISHING.md)、[多人互动](MULTIPLAYER-ACTIONS.md) 和 [聊天修复](CHAT-FIX.md)。首次搭建见 [SETUP.md](SETUP.md)，来源与参考项目见 [REFERENCES.md](REFERENCES.md)。

此前版本，修复登录未主动下发头像解锁列表导致头像仍锁定的问题；账号默认30级，已有低等级沙盒账号登录补至30级。包含31款手机壳及207个有效头像，支持更换、保存与手机壳场景同步；补齐染色助手初始化需要的64个色盘参数及初始状态，补齐时装「外观」页头衔、伞、生活工具、挂坠、头像框和载具的配置所有权与同步，接通真实染料颜色预览、保存/取消、方案解锁和存档恢复；沙盒登录解锁557件服装、314个配置表情/贴纸和127个公开动作；补齐四类1202条生活制造配方的即时产出、背包同步与图鉴计数，并开放51个有效卡池免费抽取、选池/定轨及持久化抽取历史。保留普通商品/礼包购买、本地下单发货、角色付费成就、全可显示角色、基础资源补足、每日免费礼包无限使用和部分图鉴领奖。未实现的玩法返回明确失败以避免一直等待。行为、限制、测试和存档备份见 [沙盒模式说明](SANDBOX-MODE.md)。

基于 [BanterSR/Lolo](https://github.com/BanterSR/Lolo) 的真实服务端，配合独立 MelonLoader HTTP 重定向插件。客户端插件源码与启动脚本在本仓库；Lolo、资源表、Go 工具链、数据库与编译结果为本地文件，由根 .gitignore 排除。

## 双击快速启动

双击仓库根目录的 [启动服务器.cmd](../启动服务器.cmd)，或本地已编译的 `private-server/OverField.ServerLauncher.exe`。首次使用 CMD 入口会自动编译启动器，使用 Windows 自带的 .NET Framework 4.x，无需另外安装 .NET SDK。

- **启动本地服务器**：读取已有配置，检查端口占用，打开服务端控制台并等待就绪。
- **启动服务器 + 联机桥接**：启动本地服务后接通加密桥接；已有服务会复用。公网联机仍需运行你自己的 FRP 映射。
- **正常关闭全部**：先关闭桥接，再向本项目服务端发送 Ctrl+C 并等待退出，让服务端完成存档；不会强制终止进程。

窗口每两秒更新状态。关闭启动器窗口会保留服务运行；操作期间请等待完成。首次搭建缺少程序或配置时会给出文件路径，按 [SETUP.md](SETUP.md) 准备。运行详情在独立控制台，启动失败时控制台会保留错误；关服完成后正常关闭控制台。

启动器源码在 `Launcher/Launcher.cs`，手动重编译运行 `Build-Launcher.ps1`。隔离流程验证运行 `Launcher/Test-Launcher.ps1`；它使用临时假服务验证重复启动、桥接启动和 Ctrl+C 退出，不读取真实存档。

## 游戏资源更新后

`Start-Server.ps1` 启动前会调用 `Sync-ClientVersion.ps1`，读取本机默认游戏目录的基础版本和缓存资源 manifest 版本，备份并更新本地 `Lolo/data/ClientVersion.json`。例如当前本机资源为 `2026-11-25-10-44-03`。已有服务器需要正常关服再启动，客户端连接配置改变后也要重启游戏。

其他安装目录可先运行 `powershell.exe -NoProfile -ExecutionPolicy Bypass -File ./private-server/Sync-ClientVersion.ps1 -GameRoot '你的游戏目录'`。脚本拒绝降低服务端已有版本要求，缓存 manifest 缺失时停止；本机没有安装客户端时保留原版本映射。它只同步调度版本，不会下载资源，也不会自动更新服务端玩法配置表。需要新增角色或活动时仍需匹配的服务端资源与实现。

本机游玩使用本机直连配置；好友客户端才需要公网隧道。`SkipHotUpdate` 仍要求本地资源完整，并不会跳过资源完整性检查。

好友联机提示版本过低时，先比较双方游戏目录中的 `version` 和 `launcher_Data/yoo/DefaultPackage/ManifestFiles/PackageManifest_DefaultPackage.version`。启动前的同步只读取服务器所在电脑的游戏版本，不会替好友更新资源；同一基础版本只对应一个调度资源版本。资源不匹配时，应关闭客户端重定向并通过官方渠道补齐更新，再恢复重定向、重启游戏；不要只修改版本文件文字。如果版本一致，继续核对好友实际连接的服务器调度结果。

不同渠道的程序构建号可能不同。已确认好友完整资源版本后，可在服务器电脑注册额外映射，再正常重启服务器。例如程序版本 `2026-10-21-14-06-13` 对应资源版本 `2026-11-25-10-25-56`：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\private-server\Add-ClientVersion.ps1 -BaseVersion '2026-10-21-14-06-13' -ResourceVersion '2026-11-25-10-25-56'
```

该脚本备份现有版本表，保留其他渠道的映射，并拒绝降低同一程序版本已有的资源要求。本机启动前同步会保留此额外映射。不要将本机 TapTap 构建号映射到好友的旧资源版本。注册映射只解决调度返回空版本的问题，不代表不同渠道的全部资源和玩法协议已经验证兼容。

## 启动与切换

在本仓库根目录打开 PowerShell：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\private-server\Start-Server.ps1
```

保持服务端窗口运行，在另一窗口检查：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\private-server\Test-Server.ps1
```

插件已预置在本机 `D:\TapTap\PC Games\176228\Mods\OverField.PrivateServer.dll`，配置位于该游戏目录的 `UserData\private-server.json`。安装时客户端正在运行；**退出并重新启动游戏后才加载插件**。查看 MelonLoader 日志中的 `[PrivateServer]`，确认 HTTP hooks 安装成功，以及域名重定向记录。只要有 hook 失败，日志会给出具体方法；若全部失败则关闭重定向。

重新安装到本机或其他已装 MelonLoader 的游戏目录：

```powershell
dotnet build .\private-server\ClientRedirect\ClientRedirect.csproj -c Release
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\private-server\Install-Client.ps1 -GameDirectory 'D:\TapTap\PC Games\176228'
```

恢复官方连接（退出游戏后执行，再重启）：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\private-server\Install-Client.ps1 -GameDirectory 'D:\TapTap\PC Games\176228' -Disable
```

`-StageWhileRunning` 可将配置和 DLL 预置供下次启动使用。脚本不关闭游戏，覆盖已有文件前备份。修改配置后需重启。

## 本地地址与实际作用

| 服务 | 地址 |
| --- | --- |
| Lolo HTTP / 调度 / SDK | `127.0.0.1:18080` |
| Lolo 游戏 TCP 网关 | `127.0.0.1:11000` |
| Lolo 日志 TCP | `127.0.0.1:12000` |
| 现有 Mod AI 控制接口 | `127.0.0.1:8080`（不属于 Lolo） |

私服使用 SQLite。HTTP/TCP 仅监听回环地址。PC 本地测试按上游说明关闭 `GateWay.CheckToken`；这只改变 Lolo 自身的令牌检查设置。配置文件为 `Lolo/config.json`，工作目录必须是 `Lolo`，启动脚本已处理。

插件仅匹配这三个完整域名，保留路径与查询参数：

- `dsp-prod-of.inutan.com`（含原 `18881` 调度端口）
- `sdkapi-sz.inutan.com`
- `sdkapi-of.inutan.com`

目标默认 `http://127.0.0.1:18080`，从游戏目录的配置读取。通过反射挂钩 `Il2Cpp.HttpUtils.Post/Get/SendPostRequest/SendGetRequest`、`Il2Cpp.WebRequestHelper.WebRequest_get/WebRequest_post` 与 `UnityWebRequest.set_url/SetUrl`，覆盖游戏与 SDK 的两套 HTTP 入口。另挂钩 `Il2CppInutan.NetworkSystem.SetConnectInfo/Connect`，将游戏 TCP 指向 `GatewayHost:GatewayPort`（默认 `127.0.0.1:11000`）；`SetLogConnectInfo/ConnectLog` 指向同一主机的 `LogPort`（默认 `12000`）。这些字段可写入游戏配置，缺省时使用本地值。此层只修改游戏明确的网络入口，不修改通用 Socket、hosts、系统代理或证书。

首轮实机发现仅改 HttpUtils 无法覆盖调度，游戏仍连接远端；0.2.0 已补上 WebRequestHelper 和上述 TCP 入口。SDK 如有绕开上述方法的原生调用，还需根据实际日志补充接入。

## 版本与限制

- 上游 Lolo 固定提交：`375423e8c0806bfe8804001ac46d8bc371843045`。
- LoloResource 固定提交：`ab66e3e044c5196cc65193190b7dff2fa9caae2d`。
- 上游目标客户端：1.1.12；程序版本 `2026-10-21-14-05-50`，资源版本 `2026-11-07-22-39-35`。
- 本机游戏根目录的版本文件与内置资源版本文件均为 `2026-10-21-14-05-50`。这验证了程序构建号，不能据此断言已下载的热更新资源完全匹配。
- 本仓库 libs 是较早生成的程序集；已另外检查实际安装目录的程序集，确认上述 HTTP 方法签名存在。
- **上游明确标注新版业务在重写，全部模块尚待实现。启动与调度测试通过，也不能证明完整登录、场景、家园或多人可用。** 第二轮实测已确认真实客户端通过本地 TCP 完成验证和玩家登录；角色进入世界等业务仍待验证，详见 VERIFICATION.md。
- 上游仍有访问官方 CDN 的资源转发和热更新地址，因此当前方案不等于完整离线游戏或独立资源镜像。

## 开发验证

```powershell
dotnet run --project .\private-server\ClientRedirect.Verify\ClientRedirect.Verify.csproj -c Release
```

验证包含域名精确匹配、编码后的查询参数保留、CDN 不重定向、非法目标地址拒绝等 18 项断言。它不模拟 IL2CPP 或完整游戏登录。`Test-Server.ps1` 检查根路由、真实 POST 分区调度、响应宣告的网关/日志 TCP 端口；协议与实机结果另见验证记录。

检查真实客户端 TCP 连接（避免把“进了官方场景”误判为私服成功）：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\private-server\Test-Client.ps1
```

本地服务端修复与回归用例见 [SERVER-CHANGES.md](SERVER-CHANGES.md)。重新构建含修复的 Lolo：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\private-server\Build-Server.ps1
```

仅运行全部 Go 测试可加 `-TestOnly`。此脚本会保留原二进制备份，拒绝替换运行中的服务。正常关服在服务端窗口按 Ctrl+C 并等待退出，以完成最新存档。

原工具链准备脚本或真实登录首包探针：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\private-server\Lolo\tools\local-setup.ps1
# 服务端启动后，另开窗口：
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\private-server\Lolo\tools\local-setup.ps1 -Probe
```

便携 Go 为 1.27.4，运行缓存全部位于 runtime-tools。详见本地 `Lolo/LOCAL-SETUP.md`，其中记录下载校验值和协议探针结果。上述本地脚本用于本机现成 checkout，不是独立分发安装器。

第三方源码及资源需遵守其各自许可证和使用条件，参考 [Lolo 上游说明](https://github.com/BanterSR/Lolo)。
