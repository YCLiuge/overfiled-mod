# 本机验证记录

日期：2026-11-23，Windows 本机《开放空间》，游戏目录 D:\TapTap\PC Games\176228。

## 已验证

- Lolo 固定提交 375423e8c0806bfe8804001ac46d8bc371843045，资源 ab66e3e044c5196cc65193190b7dff2fa9caae2d，Go 1.27.4 构建成功。
- ClientRedirect 0.2.0，.NET 6 Release 构建成功，0 警告 / 0 错误。
- RedirectPolicy 独立验证 18 项断言通过，涵盖精确域名匹配、原路径/编码查询保留、非法目标拒绝、避免误改 CDN 等。
- SQLite 初始化、配置表与场景资源加载成功。
- POST /dispatch/region_info 返回当前版本 2026-10-21-14-05-50_2026-11-07-22-39-35 和本地 TCP 地址。
- Test-Server 检查 HTTP 根路由与调度、网关11000及日志12000监听通过。
- 独立协议探针发出真正 protobuf VerifyLoginTokenReq，收到 StatusCode_OK 的 VerifyLoginTokenRsp；这不是 HTTP mock。
- **第二轮真实游戏验证**：MelonLoader Latest.log 17:38:35 确认 8 个 HTTP hooks 和 4 个 game/log gateway hooks 成功安装。
- 同一真实游戏日志 17:38:41 起记录 dsp-prod-of.inutan.com 调度请求重定向；17:40:38 游戏与日志网关分别改至127.0.0.1:11000/12000。
- Windows TCP 表显示真实 launcher 进程 PID10284 与127.0.0.1:11000建立连接，Test-Client 输出 PASS。
- Lolo 17:40:39 日志显示 AccountType_Windows verify success，继而 PlayerLogin 登录成功。服务端已为真实客户端创建本地玩家记录。

## 已纠正的问题

首版0.1.0只改HttpUtils与UnityWebRequest.SetUrl，SDK重定向有命中，调度未被改到本地。用户首轮报告已进入场景，系统连接表却显示游戏仍连官方网关，因此该次进场景不计为私服成功。

0.2.0补充游戏的WebRequestHelper.WebRequest_get/post覆盖BestHTTP入口，并将NetworkSystem游戏与日志的明确连接入口分别路由到本地。实际调度重定向和本地网关登录已按上述证据重新验证。

## 当前范围

真实客户端已完成本地TCP接入、令牌验证与玩家登录；**尚未验证角色进入世界、主数据加载、移动、场景、家园、战斗、多人与长期存档是否正常**。上游正在重写新版业务，不宣称完整可玩。服务端曾记录FashionID:0资源/数据警告，未因此退出；这类业务问题仍需后续实机定位。

此前托管后台作业 pwsh-7dd3f7b114e6434a 已结束，其历史输出已无法从作业管理器读取。2026-11-24修复版构建并启动后，当前托管作业为 pwsh-37c222da9a6c477f；核查 PID2464 仅监听127.0.0.1的18080、11000、12000。长期运行请用Start-Server.ps1开启前台服务；不是Windows常驻服务。插件与配置保存在游戏目录，下次游戏启动继续生效；恢复官方连接用Install-Client.ps1 -Disable后重启游戏。

## 2026-11-24 服务端修复验证

- Build-Server.ps1 完成补丁幂等检查、测试复制、`go test ./... -count=1` 和 Release 构建；所有包通过，包括新增存档、场景、登录回复和日志心跳测试。没有运行 race detector，不将并发行为测试等同于 race 检测。
- 存档测试使用临时 SQLite，覆盖最近变更、旧快照在途、真实SQL故障、解除故障后重试、队列排空和并发关闭、主循环panic后的保存。未修改真实玩家存档内容。
- 两处上游旧代码不满足当前 Go vet 的格式调用已做等效清理，保留全仓库测试的默认静态检查。
- 保存的 local-server.patch 对干净固定基线通过 `git apply --cached --check`（临时索引），对当前修复状态通过反向检查。Lolo及根仓库 `git diff --check` 通过，仅有Windows换行提示。
- 构建 SHA256：`A06E7D421E1B2281509CFCEA5938AEA031A015100C664FDCD85B6DE90F9E48D0`；版本 `local-fixes-1`，提交标记 `375423e-local-fixes`。旧二进制在backups中。
- 启动新版后 Test-Server.ps1 再次通过 HTTP根路由、POST调度与两项TCP监听检查；真实protobuf首包探针返回 StatusCode_OK / is_server_open=true。探针复用独立测试账号，不代表真实客户端已重新验证完整进场。
- 详细改动、可重现命令及of-ps参考边界见 [SERVER-CHANGES.md](SERVER-CHANGES.md)。截图中的座椅错误仍按用户要求搁置。

证据路径：游戏目录MelonLoader/Latest.log；Lolo/local-logs/probe.log及listeners.json；本次会话后台服务输出。配置与数据库、游戏日志不进入Git。为避免泄露凭据，插件只记录目标主机，不记录URL查询、SDK令牌、密码或请求体。
