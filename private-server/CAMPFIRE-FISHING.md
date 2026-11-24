# 篝火与钓鱼（local-sandbox-12）

## 修复目标

原服务未路由 PlaceCampFireReq、RemoveCampFireReq、FishingReq，因此互动篝火放置和钓鱼请求缺少服务端处理。

篝火按场景频道保存，提供放置、回收、同频道通知和后来进入者的场景快照。所有权与成员校验在场景频道循环中执行，避免并发修改场景状态。原生 CampsiteSystem 初始化使用旧 BonFires 列表，实时通知使用新版 CampFire；两种快照由同一状态投影。

钓鱼采用客户端真实 FishingReq/Rsp、FishingEventResult、FishingResultNotice 协议。客户端响应消费 SetEventsInfo、SetSingleRewardInfoCache 并启动事件动画，结果通知结束动画。服务端主循环计时结算，不阻塞请求处理，也不使用后台 goroutine 修改玩家背包。

## 沙盒规则

鱼池、鱼竿、鱼饵盒、鱼品种与长度范围使用当前资源验证。当前鱼池表的池引用未展开掉落权重，因此鱼种选择使用明确的沙盒策略，不能视为官方掉落分布。钓鱼时长暂用6秒，沙盒不扣鱼竿耐久、鱼饵和精力。收获计入背包，重复开始、重复结算和取消不能重复发奖；取消、切场景、换频道和离线取消未完成钓鱼。

事件计划采用真实开始与中段事件（100/index2、240/index1），由 FishingResultNotice 驱动收获完成；不使用会显示耐久扣除或额外鱼获的300事件。

## 部署与验证记录

2026-11-24 已部署 local-sandbox-12。全量 `go test ./... -count=1` 通过；本机 region_info 返回200、TCP VerifyLoginTokenRsp=OK；加密桥接 HTTP/game/log 三项探针均通过。旧服正常退出，日志确认“game退出存档完成”后才备份并更新。

离线备份目录：`backups/pre-sandbox12-20261124-221615`（旧可执行文件和完整 db 目录）。新版 SHA256：`8A468BA4F114F5C900275967DBFEA8AB7B74D2B5166A4AC4FA9CD8BD3819D9DF`。

## 游戏内验证

重新登录后，在互动道具中选篝火放置，检查本人、同频道朋友和后来进入者的显示，再测试回收、重新放置、换频道和离场。钓鱼需选择持有的鱼竿，在钓鱼点开始，等待收获提示并核对背包；测试中途取消、连续钓鱼和换频道时的清理。

自动验证能覆盖协议、状态、背包和存档逻辑；真实客户端的动画、站位和地图位置仍需要游戏内检查。
