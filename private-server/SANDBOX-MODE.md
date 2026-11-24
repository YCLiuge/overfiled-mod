# 本地沙盒模式（local-sandbox-9）

## sandbox9头像锁定修复与账号30级（2026-11-24）

修复sandbox8登录只授予服务端头像所有权、未初始化客户端头像缓存的问题。客户端native证据：UI_Player_ViewCtrl.OnItemRendererHead（RVA 0x104F9C0）读取PlayerModel.Heads（offset 0xE0），以HeadConfigure.ID判断Contains，再将取反结果传给Set_Lock；PlayerSystem.OnUnlockHeadList（0x199ECC0）读取Rsp.heads并调用UpdateHead。Head配置全部207行ID=HeadID，不是ID映射问题。另确认PlayerSystem.OnInit注册全局UnlockHeadListRsp事件（1530/0x5FA），因此PacketId=0主动推送具有接收路径；它不依赖单次请求回调。登录在AllPackNotice后主动发送PacketId=0的UnlockHeadListRsp，沿用显式请求响应；不依赖客户端另行请求才填充独立解锁缓存。同时补齐背包中的头像所有权和BaseItem.Num=1。

账号基本资料默认等级从1改30，已有沙盒账号每次登录不足30级自动补至30级；不降低更高等级，不重置经验、选用头像、手机壳或其他数据。落盘错误恢复缓存且终止登录，避免客户端收到未保存的等级。

增加测试：207头像全部恰好一次出现在登录库存且数量1、Inventory包类型；主动头像解锁Rsp的PacketId0与protobuf序列化；新账号默认30级，旧1/29级补至30、30/40级保持，经验和选用样式保留，独立SQLite读落盘且重复应用不写库。

全量`go test ./... -count=1`通过。04:15:41正常停服，确认退出存档完成；备份`backups/sandbox9-install-20261124-041557/`（db/config/sandbox8旧程序），04:16:05启动local-sandbox-9。安装SHA256：`009184D87DA9797712CD007FBE5BB786ACF55F3F110C14864785F3E0AD57FE7B`。HTTP、TCP11000/12000及VerifyLoginTokenRsp OK通过。04:16:29真实玩家1000001登录成功，随后以SQLite URI mode=ro查询确认level=30、head=41101、phone_case=0，未代操作更换头像。客户端锁图标消失及点击换头像仍待画面确认。

## sandbox8手机壳与头像（2026-11-24）

沙盒登录补齐Phone/Item正式关联的31款手机壳（7000–7030，物品标签42），使用原有普通背包所有权和登录PackNotice下发；头像沿用207个Head配置且有对应Item的有效头像及UnlockHeadList。Item中的孤立头像41204无Head配置，不伪造资源。31款手机壳中11款（7020–7030）AssetPath为空，仍保留正式所有权，客户端对应模型显示尚待实机确认。重复登录不重复发实例，不改原有头像或选用样式。

接通ChangeHeadReq及ChangePhoneCaseReq路由、真实所有权验证、原PacketId响应、数据库持久化与失败回滚；重复选择不重写数据库。新增OFGameBasic.PhoneCase（默认0保留原协议默认），由启动时AutoMigrate添加字段；PlayerMainData、场景加入及场景外观更新下发保存的手机壳。头像保存到原有Head字段，沿用登录和好友资料读取。

全量`go test ./... -count=1`通过。测试覆盖31款手机壳/207头像真实资源和所有权、独立Phone配置遍历、库存protobuf、JSON与重复解锁、更换路由与原PacketId、真实SQLite落盘、重复选择、空/非法/未拥有选择、SQL强制失败后缓存与落盘回滚、场景同步及加入场景时的手机壳protobuf。

04:02:28旧服正常退出并确认退出存档完成。离线备份`backups/sandbox8-install-20261124-040244/`包含db、config及sandbox7旧程序；安装SHA256：`A83FFE43986E3BE4C13FA5946268DB923402ADA4BAE74918D99711901DBC6361`。源码累计补丁反向检查通过，独立回归测试同步至server-tests。04:02:52启动日志确认local-sandbox-8；HTTP根页面、region_info、TCP11000/12000及VerifyLoginTokenRsp OK通过。真实玩家需重新登录触发缺失所有权补齐，未代操作客户端选择样式。


## sandbox7染色界面初始化修复

用户实机反馈选择染料后只显示服装和染料图标，染色控件不显示。上一版ColorantSelect返回空Params及IsDye=true；本版按照对照实现的协议形状，返回16组Vector4（64个double）作为本地色盘扰动参数、PictureId=1、Uvy=0、IsDye=false。半径参数始终为正，避免客户端计算除零；这些是本地界面初始化参数，不声称官方概率。染色预览、保存/取消和已存颜色处理规则保留。

2026-11-24 03:47:33正常停服并确认退出存档完成，离线备份到`backups/sandbox7-install-20261124-034751/`（db、config及sandbox6旧程序）。全量`go test ./... -count=1`通过，新增ColorantSelect protobuf实际编码/解码检查64个参数、分量范围、初始状态和原PacketId。源码累计补丁已重新导出、反向检查通过，独立测试已同步。

安装程序SHA256：`5633AE47EE9621E78F77ED989639461750A6DF2E730D4998FE22A8F8FBED8816`。启动日志确认local-sandbox-7；HTTP、region_info、TCP11000/12000及VerifyLoginTokenRsp OK通过，真实玩家1000001于03:48:21重新登录。只读客户端原生调用扫描确认SetSwirlTexture调用GenSwirlNoiseTexture，SetSwirlParam接收IList<double>，PanelReady设置染色按钮显示；进一步原生字节确认SetSwirlParam在RVA0x1E25B8C比较参数数量64，不等时记录错误；RVA0x1E25EA9比较16组循环边界。IsDye布尔分支尚未反出，也未发现对应异常栈；false初始态来自对照实现。客户端按钮实际显示仍须重开染色助手验证。

## 时装外观页补齐

补齐头衔、伞、捕虫网、伐木斧、装水瓶、挖矿锤、采集手套、钓鱼竿、挂坠、头像框、载具及铲子。头衔/伞/挂坠/框以真实Item基础所有权下发；工具和载具由Weapon配置关联Item，发真实WeaponInstance。资源中的载具为武器类型21，物品标签2，不是空的物品标签52。保留已有武器实例、穿戴和染色，缺少的ItemID仅新增一件；补齐有效头像供UnlockHeadList查询。CharacterAppearance沿用非零字段更新语义（proto3无presence），0保持原字段；PlayerAppearance沿用完整替换语义，0可清空挂坠/框。

配置清单：253个头衔、240把伞、38个挂坠、62个头像框；工具/载具共76件（手套8、斧9、锤9、捕虫网9、水瓶9、钓竿10、载具19、铲3），另补齐207个有效头像。无Head配置的孤立头像41204不授予。角色外观支持已拥有工具的实例ID及对应物品ID；错误类别、未拥有和空请求失败，不改变穿戴。场景外观更新同时发送当前头像框。240条正式伞配置中37条AssetFullName为空，仍保留其所有权；资源路径为空不足以证明它们是孤立物品，客户端实际模型显示尚待验证。

## 染色流程

接入70种DyeStuff染料（69种固定配置RGB、1种随机染料），登录补足染料库存。沙盒免费选择染料、生成或指定RGB预览、保存/取消、解锁额外方案，并保存到原服装DyeSchemes；未保存预览与染料选择通过Player JSON持久化，登录主数据返回UnSaveOutfitDyeScheme。保存时只替换当前部位颜色，保留其它部位及其它方案，并发送更新后的真实衣柜记录。空输入、未拥有服装、未解锁方案和非法颜色/索引失败。

资源未包含官方染色曲线、完整概率和方案/部位上限。沙盒以真实色板生成颜色，指定RGB范围0..255，随机染料在真实色板取色；方案索引0..9、部位0..15是本地安全界限，不声称官方规则。ColorantSelect提供客户端需要的本地64个色盘扰动参数，和最终RGB色板规则区分；未还原官方分布和概率。严格模式拒绝本轮未还原的消耗/概率规则。

## sandbox6部署与验证（2026-11-24 03:36，Asia/Shanghai）

- `Build-Server.ps1 -Stage` 全量 `go test ./... -count=1` 成功；外观数量/关联、库存wire、真实场景穿戴同步、染色预览/保存/取消/方案切换/JSON恢复及失败原子性均通过。随机染料401另测32次生成均属于真实69色配置表，不要求随机结果两次不同。
- 初次全测发现伞资源路径过滤导致只授予203条，修正后保留240条正式配置，全量重测成功。染料独立复核为70条，固定色69条，避免早期统计错误。
- 累计源码补丁 `server-patches/local-server.patch` 已重新导出并通过反向检查；独立回归测试同步到 `server-tests/`。
- 旧服03:36:10正常Ctrl+C退出，日志确认 `game退出存档完成`。离线备份 `backups/sandbox6-install-20261124-033627/`，含整个db目录、配置及sandbox5旧程序；未直接修改玩家数据库。
- 安装程序SHA256：`26BCD0E9A34962EE6C8DCF1D5D503FA9F964E10910D84AE1786490E278C9A8F6`。启动日志确认 `ServerVersion:local-sandbox-6`、客户端1.1.12。
- HTTP根页面、region_info、TCP11000/12000通过；`local-setup.ps1 -Probe`返回VerifyLoginTokenRsp OK。真实玩家1000001于03:36:38登录成功。尚未通过客户端逐件确认外观模型与染色UI曲线显示，服务端/协议测试不等同客户端视觉验收。

以下为local-sandbox-5功能及部署记录。

## 服装、动作、生活制造与全卡池

- 沙盒登录补齐557件实际服装、314个ChatEmotion配置表情/贴纸、127个Expression分组关联的公开动作。沿用真实衣柜记录、默认染色和动作ID；已有穿戴、染色和其他表情记录保留，重复登录不重复生成。只有物品表记录而无ChatEmotion配置的5个孤立表情ID不自动授予。
- 补齐穿戴预设空字段、无效角色和预设索引的安全检查。登录使用原背包/表情通知及AchieveActionUnLockNotice下发真实所有权。
- 生活制造接通烹饪94、制造445、手工141、缝纫522，共1202条当前可显示配方。沙盒按请求立即产出单件、免材料/时间/概率门槛，真实更新普通背包或服装衣柜。响应给增量奖励、PackNotice给绝对余额；失败不提交库存和制造记录。
- 四类生活等级与技能开放到配置最大值，制造次数随Player JSON存档。成就/图鉴进度只按真实Achieve条件与配方计数映射，不伪造已领取；完整成就奖励领取不在本轮实现范围。
- 全部51个有效卡池（包括过期和自选池）开放，列表不按活动时间隐藏，沙盒免费抽取；有限次数池展示0避免客户端用完锁定，真实计数仍存档。选池和定轨校验真实资源范围，未知ID和无效操作明确失败。
- 抽卡先完整构建独立的背包、角色和抽卡状态，全部发货成功后才提交。服装重复沿用原实例，重复角色按RepeatItemID转换1枚对应代币（资源未定义官方转换数量，此为沙盒规则），不额外编造兑换货币。抽卡历史随Player JSON持久化并支持分页。
- 严格模式尚未实现的正式制造门槛、抽卡支付和官方保底规则明确拒绝，不扣款；沙盒保留基于现有概率配置的可用抽取流程。以上完成服务端验证，游戏内显示和按钮交互仍需实机复验。

完整`go test ./... -count=1`通过。新增覆盖真实557件服装/314个配置表情/127个动作、1202条制造配方、51卡池和395种抽取奖励，包含穿戴、路由包序号、失败原子回滚、重复获得、溢出、技能/图鉴计数及JSON持久化。新增测试已同步到独立server-tests目录；累计源码补丁已通过反向应用检查，固定上游HEAD可重放。

部署记录：2026-11-24 03:00:04旧服务正常退出并确认“game退出存档完成”；数据库、配置及旧程序离线备份到`backups/sandbox5-install-20261124-030027/`。新程序SHA256：`B854ABB1EC919859E48A1C558717FA542019783237EA0AE44212CF4A262B6A3D`，03:00:35启动并报告ServerVersion local-sandbox-5。HTTP18080、TCP11000/12000及合成账号协议认证通过；真实玩家1000001于03:00:40重新登录成功。未代操作玩家的制造/抽取按钮。

以下为local-sandbox-4历史记录。

## 02:38 购买流程完善

- ShopBuy统一走原子购买事务：先验证商品池、价格、发货、溢出与购买次数，再一次性提交背包和商店状态。失败回复原PacketId的BAD_REQ，失败前不扣款、不发货、不初始化玩家购买状态。
- 沙盒普通商品/礼包免扣款、免余额和限购门槛；真实购买次数仍存档。有限购的普通商品向客户端显示BuyTimes=0，避免按钮因次数用尽而禁用；充值无限购和角色权益保留实际计数。
- ShopInfo每格仅返回一次，顺序稳定，PoolIndex来自实际配置；按玩家购买历史选择首购池/普通池。首购赠品纳入发货，跨首购边界批量购买仅一次首购奖励，成功立即切换后续商品池。
- CreatePayOrder仅支持沙盒自购且GoodsID非空的可支持商品：生成唯一sandbox订单号，成功后发送CreatePayOrderRsp、背包绝对余额PackNotice、同订单号PaySendGoodsNotice及增量奖励。不调用支付供应商或外部回调；严格模式、无效池和跨玩家赠送明确拒绝。ResultStr未伪造SDK签名；客户端SDK界面能否完全关闭仍需实机点击验证。
- 角色付费成就（CharacterBP）验证已拥有角色和真实商店映射后解锁IsUnlockPayment，并发送CharacterBpBuyNotice。严格模式按价格扣款且拒绝重复解锁；沙盒重复解锁幂等、不扣款。
- 当前支持基础数量类商品；真实目录测试724个格子、18类物品通过。月卡/普通通行证/活动礼包权益、武器/时装/头像/表情等特殊发货事务尚未实现，明确失败，不把配置中的部分货币当成权益购买成功。
- 全部go test ./... -count=1通过；新增路由整合测试、严格余额与限购、失败无副作用、各类溢出、首购赠送、JSON持久化、角色权益和真实目录测试。源码补丁与新增测试已保存，可在固定上游HEAD重放。

旧服务02:38:28日志确认“game退出存档完成”，离线备份旧程序、完整db及配置到 `backups/sandbox4-install-20261124-023845/`。新版本SHA256：`46C7BE759167D0D84BD3E3E68324E08E2A66093BFF54AFEEB6B69FE649388D72`，02:38:54启动。HTTP18080、TCP11000/12000及合成账号VerifyLoginTokenRsp通过，真实玩家1000001于02:39:03重新登录成功。未代操作真实玩家的商城按钮。

以下为local-sandbox-3历史记录。

## 02:19 防转圈修复

旧版本运行日志明确记录：02:05:32 `TreasureIllustrationRewardReq`(4525)、02:08:21 `DailyTaskExchangeReq`(2602)均无路由。原代码仅打印错误然后丢弃请求，客户端一直等待。

- 4525新增沙盒处理：Book（枚举0合法）、Utensil、Shovel按资源表CollectionID完整奖励链发奖，PackNotice发绝对余额，Rsp发增量；领取记录随玩家JSON保存，重复领取成功但不再次发奖，4523查询返回实际已领列表。
- Book/Utensil有效配置各10项，Shovel3项。铲子1007003完整奖励102×30及5070001×1，未漏掉第二奖励池。
- TreasureMap/Furniture的领奖含义尚未确认，明确返回BAD_REQ；严格模式也明确拒绝尚未实现的资格判断。
- 已认证、会话匹配的其他未注册请求，按Req/Rsp名称配对且校验status为StatusCode枚举后，回复原PacketId的BAD_REQ。通知、未知消息、缺少对应Rsp或StatusCode字段的消息不伪造回复。DailyTaskExchange功能本身尚未实现，当前返回失败而非成功。
- 新增路由测试、模型原子失败、重复领取、sonic JSON重载后幂等、类型0、全部有效三类资源、后续奖励池缺失/随机/不支持类型/库存溢出的测试，完整go test ./...通过。

已正常发送Ctrl+C，旧服务02:18:26日志确认“game退出存档完成”，退出后离线备份旧程序、完整db及配置到 `backups/sandbox3-install-20261124-021903/`。安装新版本SHA256 `D3282579655B70367E40FC3651463E8174C33343B1F1D1928C646E471A65C7BA`。可用 `powershell.exe -NoProfile -ExecutionPolicy Bypass -File ./private-server/Stop-Server.ps1`正常停止：脚本校验exe路径及控制台进程归属，拒绝混有无关进程的控制台，绝不强杀；切换前仍应核对存档完成日志。

新服务PID12320，02:19:16启动；HTTP、两条TCP监听及合成账号协议认证通过，02:19:41真实玩家1000001登录成功。尚未代操作客户端领取按钮，按钮交互结果需游戏内复验。

以下为local-sandbox-2基础功能与历史部署记录。

2026-11-24：按用户“不要限制，能跑通流程，解锁所有角色”的要求新增。

## 已实现

- `config.json` 中 `Game.Sandbox=true`：每次登录补齐资源表中全部可显示角色；当前表共28位，保留已有角色等级、队伍、装备及外观。
- 为缺失角色走原有角色初始化，生成默认武器与外观；重复登录不重复生成装备。
- 2591种支持的基础背包资源（金币、抽卡货币、材料、经验书、礼包、食物、配方、家具等）登录时补到至少99,999,999，已有更大数量不降低。
- ItemUseReq(1959) 已注册处理及回复。沙盒下固定奖励礼包不检查库存、不扣除源礼包；奖励实际加入背包，并发送绝对数量PackNotice及增量ItemUseRsp。
- 每日免费礼包6020010完整展开GiftBag→RewardPool→RewardItemPool：190×2、101×1000、1501×2；6010040发放190×2、101×1000。
- 数量仍存档以维持客户端/服务端一致；沙盒免除库存门槛，而非只播放领取动画。
- 关闭 `Game.Sandbox` 后使用严格模式：要求库存且扣除礼包；已授予角色与资源不会自动删除。

## 范围

不是所有玩法均已实现。随机、自选、特殊动画/副本礼包、额外成本、装备奖励礼包目前回复BAD_REQ；不会伪造成功或扣除物品。其他未注册玩法如DailyTaskExchangeReq仍需单独实现。原上游功能解锁列表本已返回全表。

没有修改真实玩家数据库，新增开放由新版本首次登录时执行。配置修改前备份：`backups/sandbox-20261124-015204/config.json`。

## 构建与切换

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File ./private-server/Build-Server.ps1 -Stage
```

`-Stage` 通过完整测试后仅写入 `Lolo/bin/Lolo_windows_amd64.next.exe`，不替换运行中的程序。标准不带 `-Stage` 的构建仍拒绝覆盖运行文件。

新版本版本标识：`local-sandbox-2`。

构建SHA256：`C0E359CC1AEB8BFA4F83BCD369B303634D2033E5E504CBE3F25449BD44C201D6`。

已于01:55完成切换并运行新版本（PID3744）。用户退出游戏后，核验旧服务控制台成员并发送正常Ctrl+C；日志确认“game退出存档完成”，随后进程退出。离线备份旧程序、完整db目录和配置到 `backups/sandbox-install-20261124-015542/`，再安装next程序并启动。HTTP18080、TCP11000/12000均通过检查，合成测试账号VerifyLoginTokenRsp返回OK。真实玩家重新登录后再验证角色页与礼包；本轮未替用户操作游戏界面。

## 验证

- 全部 `go test ./... -count=1` 通过，随后成功编译并核对version输出。
- 真实资源测试：28位角色全部开放、2591条基础资源补足，二次应用序列化结果相同；装备不重复。
- handler通过真实路由测试：每个请求恰好一次回复，保留packetId；成功同步背包，失败不发变更；沙盒配置实际选择零库存入口。
- 礼包测试：两种每日礼包、多次使用、零库存/无背包的沙盒使用、库存不足、零数量、未知/非礼包、自选、随机、后段缺失配置、缺Item、额外成本、特殊及装备奖励、余额/聚合溢出；失败库存不变。
- Sonic对Go1.27提示回退encoding/json，不影响本轮通过结果；未进行CGO race detector。

可复现源码统一保存在 `server-patches/local-server.patch`，测试保存在 `server-tests/`。补丁已验证可用于固定HEAD干净索引且可从当前工作区反向校验；未改动内层git索引或原资源。
