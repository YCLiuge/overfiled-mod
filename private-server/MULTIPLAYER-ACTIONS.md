# 多人动作及角色乌（local-sandbox-11）

## 角色乌

String.json 将角色 202004 标识为“乌”。Character.json 提供该角色的技能、默认武器与服装配置，但 IsShow=false，原全角色沙盒因而漏掉此角色。

沙盒登录初始化现在明确允许角色202004，沿用正常角色初始化配置。已有角色、装备及存档保持；重复登录不重复发放装备。普通角色获取入口与其他隐藏资源行继续遵循原表规则。

模型回归测试检查解锁、武器、服装、技能、角色响应，以及JSON存档恢复后再次初始化的一致性；客户端模型加载仍需游戏内确认。

## 多人动作协议证据

多人配置来自Expression.json的ExpressionGroupInfo.MultiplayerID和MultiplayerExpression，不能按Action.NewActionType判断。

客户端原生证据：

- MultiplayerActionSystem.OnSendMultipleActionNotice 比较EndTime和DateTimeUtils.GetCurrentTimestampBySeconds，邀请截止使用Unix秒。
- INetworkHelper.ReqMultipleActionAccept只填actionId/fromPlayerId，status默认为NONE；服务端不能将NONE当拒绝。
- OnSendMultipleActionCompleteNotice遍历ActionPlayerIds，依据FromPlayerId将发起者置于首位并按资源配置分配动画；完成通知必须包含发起者。
- 完成通知ActionId=0会调用StopLooping(FromPlayerId)，用于清理多人动作；普通动作0本身不具备这条清理语义。

邀请窗口30秒为本服策略，非声称来自官方资源表。

原生反汇编保存在ClientRedirect.Verify/multiple-action-evidence-utf8.txt和multiple-action-stop-evidence.txt。

## 本次支持范围

资源中实际有10个可发起动作，均为双人：1060击掌、10000拥抱1、10003拥抱2、10010握手1、10013握手2、10020搓脸1、10023搓脸2、10030转圈、11000独轮马交互、10043拥抱3。未被ExpressionGroup引用的内部多人配置不作为可发起入口开放。

完成组保留参与者关系，参与者停止、换动作、加入另一组或离场后，向场景发送原生多人结束通知；完成组不会在原邀请截止时间被错误取消。三人流程仅用合成配置验证人数处理，当前资源没有可用三人动作。

## 同步修复私聊数据库自锁

旧服读取私聊记录时，事务已占用 SQLite 的唯一连接，内部却调用全局连接池查询，导致游戏主循环和正常关服一起等待。已将事务内部查询改为使用同一个 tx，并明确按双方 UID 查找会话，避免不同会话混用。新增单连接真实 SQLite 回归测试覆盖读取、写入、未读状态、反向 UID、会话隔离和失败回滚。

部署时通过只读进程栈确认此根因；核验旧进程连接池结构后，临时将其连接上限由1调为2，触发隔离 SDK 诊断请求，使等待继续执行。未强杀旧服。2026-11-24 18:44:23 日志确认“game退出存档完成”，退出后对旧程序和整个 db 目录离线备份，再安装新版。该临时运行状态不写入配置，新版继续使用原单连接配置。

## 部署验证结果

2026-11-24 已部署并启动 local-sandbox-11，程序 SHA256 为 1750A8891413960065E934B1677E4CB984E86253455A57B766BEABB31ACF8AE6。包含私聊修复后的 go test ./... -count=1 全部通过；本机 region_info 返回 HTTP200，游戏 TCP VerifyLoginTokenRsp 返回 StatusCode_OK、is_server_open=true；公网 TCP 桥的 http/game/log 三条协议探测均通过。

离线备份目录：backups/pre-sandbox11-20261124-184625，含旧程序与整个 db 目录。客户端插件无需更新。

## 验收边界

自动测试只能确认服务端状态、协议包和资源关联，不能替代两个真实客户端的动画与站位验证。更新生效后，两人进入同一场景频道，发起多人动作并让另一人点击接受，检查双方与旁观者是否看到动画；再验证换动作、取消和离场清理。角色乌需重新登录后检查角色列表及切换。
