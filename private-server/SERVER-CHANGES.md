# 本地服务端修复 local-fixes-1

以下为已保留的基础修复；当前版本 `local-sandbox-9` 在登录时主动发送UnlockHeadListRsp以填充客户端Heads缓存，修复头像仍锁定；补齐头像库存Num=1；账号默认30级，旧沙盒账号登录不足30级则补至30级，不降级、不重置经验。包含31款手机壳和207个有效头像的更换、持久化及手机壳场景同步；同时保留上一版修复：染色助手选择染料后缺失色盘参数与初始状态错误；在此基础上补齐时装外观页全量头衔、伞、工具、挂坠、头像框与载具解锁，以及染色预览、保存/取消、方案解锁/穿戴切换和持久化，并保留沙盒全服装/公开动作/配置表情解锁、四类生活制造与真实图鉴计数、全部有效卡池免费抽取，以及既有沙盒角色资源、礼包处理、部分图鉴领奖、商城购买与本地下单发货、角色付费成就解锁及缺失路由的失败回复，详见 [沙盒模式说明](SANDBOX-MODE.md)。

基线为 BanterSR/Lolo 375423e8c0806bfe8804001ac46d8bc371843045；客户端和资源版本保持原配置。

## 修复内容

- PlayerLogin 在访问数据库之前登记统一响应，数据库加载或反序列化失败时也回复失败，避免客户端一直等待；重连成功才执行重连后通知。
- PlayerMainData 失败时不继续发送入场通知；成功时将数据库已有的 Sex、HideValue、AccountType 写入响应。此项只修复字段映射，不推测历史账号缺失的渠道值。
- PlayerSceneRecord 对缺失 Data 的请求返回 BAD_REQ，避免空指针使游戏主循环停止。
- ChangeSceneChannel 在离开原房间前检查目的地，响应填写实际场景、房间号和好友目标；无效目标不移动玩家。
- 日志服务心跳保留客户端时间，同时返回独立的服务器毫秒时间。
- 正常关闭游戏主循环时强制保存所有内存玩家的最新快照，等待写入完成；失败尝试三次并向关闭调用方返回错误。关服不再受五分钟自动保存周期限制。
- 异步写库线程只向结果通道报告结果，游戏主循环更新保存状态；队列关闭与投递互斥，关闭时排空队列。数据库写入失败不推进成功保存时间。
- 玩家存档或数据库关闭失败时进程以非零状态退出，避免误报正常关服。

强制终止进程、任务管理器结束任务、会话清理、断电不会执行正常关服流程，仍可能丢失上次保存之后的变化。正常关服请在服务端前台窗口按 Ctrl+C，等待退出。不要在仍运行时复制 SQLite 文件作为一致性备份。

## 可复现构建

本机已有 Lolo、Resource、便携 Go 和配置时，在仓库根目录运行：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\private-server\Build-Server.ps1 -TestOnly
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\private-server\Build-Server.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\private-server\Start-Server.ps1
```

Build-Server 固定检查上游提交，使用 server-patches/local-server.patch 修复源码，将 server-tests 下的测试复制到本地 checkout，运行 go test ./... 后才构建。已经应用补丁时不会重复应用；有冲突时中止，不覆盖用户源码。构建前备份原二进制到 backups。运行中的二进制不替换。源码回退点同样在本地 backups，补丁和测试位于未被忽略的目录，以便纳入本仓库版本管理。

自动化用例覆盖：缺失玩家的重连单次响应；空场景记录后仍可处理心跳；当前场景、另一房间、好友传送、缺失好友、非法房间；日志心跳的客户端时间回显和服务器时间窗口。存档用例使用临时 SQLite 文件，不读取或修改真实玩家数据库。

## of-ps 参考边界

参考仓库：https://github.com/byzp/of-ps ，检查提交 fa2142b8521e802beb23fc6ed538205b9f4f1513。比较了场景切换响应、主数据和心跳处理；修复依据 Lolo 自己的 protobuf、数据模型及预期行为独立编写，没有移植 Python 实现或资源。

of-ps 是 AGPL-3.0，Lolo 是 Apache-2.0；直接复用代码时应另行处理其许可证义务。of-ps 的程序/资源版本为 2026-07，当前 Lolo 使用 2026-10/11，不能把它的旧资源表或整份协议响应直接混入本地服务。参考 clone 已由根 .gitignore 排除。

这些验证支持上述局部行为，不代表完整实机进场、家园、战斗、多人和长期存档均可用。截图中的座椅报错按用户要求暂不处理。
