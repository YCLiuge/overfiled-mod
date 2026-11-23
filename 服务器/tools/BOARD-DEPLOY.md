# BOARD 服务器部署（1.0.1-board）

此目录为独立服务器发布，保留聊天与 GAME_*，新增/确认独立 BOARD -> BOARDMSG，未使用普通聊天作为 fallback。

## 部署文件

将本目录以下三个文件一并上传到服务器实际启动目录：

- Chat.Server.dll
- Chat.Server.deps.json
- Chat.Server.runtimeconfig.json

Chat.Server.pdb 是可选调试符号。build-manifest.json 提供文件 SHA256，board-verification.json 提供本地真实 TCP 验证结果。

这是 .NET 9 framework-dependent 可移植发布（UseAppHost=false），Windows/Linux 均使用 `dotnet Chat.Server.dll` 启动；目标服务器须安装 .NET 9 Runtime。没有打包运行时。旧 Chat.Server.exe 不属于此发布；服务启动命令应指向本次 DLL 的绝对路径，不要继续启动其他目录的旧 Debug/旧 exe。

示例（由服务器管理员在维护窗口执行；本次任务未连接或重启公网服务）：

```sh
dotnet /实际部署目录/Chat.Server.dll --host 0.0.0.0 --port 9000
```

沿用生产服务原有 host/port/频道/心跳等参数。如果由 systemd、面板或 Windows 服务管理，更新其 ExecStart/程序参数到上述 DLL。替换服务器需要该管理员另行操作；上传完成后仅上传文件不会使运行中的旧进程自动更新。

启动日志应显示 `构建 1.0.1-board` 和 `支持 BOARD -> BOARDMSG`。可在部署后用两个专用测试连接执行 HELLO，再发送 `BOARD {"channel":"general","text":"probe","id":2}`，发送者得到 OK，另一端收到 BOARDMSG；未握手会得到 not_handshaken 而非 unknown_command。

## 协议与边界

BOARD 字段为 channel 与非空字符串 text，正文按原样转发；发送者身份 fromId/from 由服务器生成，频道名采用服务器规范名称。只广播给同频道其他成员，发送者收到 OK delivered，无 MSG 聊天转换。要求握手且恰好订阅一个频道。

错误包括 not_handshaken、bad_channel、single_channel_required、not_in_channel、bad_text、board_msg_too_large（超过 24000 UTF-8 字节）、board_rate_limited（每连接共享 GAME/BOARD 每秒 128 条及 1 MiB）、msg_too_large（转发后含 JSON 转义/身份字段超过 max-line）。上述业务错误保留连接；入站单行超过 max-line 返回 line_too_long 并断开。服务器只路由正文，不执行象棋规则。

## 重建与验证

在仓库根目录执行：

```powershell
pwsh -NoProfile -File .\服务器\tools\publish-board-server.ps1
python .\服务器\tools\board-integration.py .\artifacts\ChessServer-Board\Chat.Server.dll --out .\artifacts\ChessServer-Board\board-verification.json
```

验证脚本仅启动自己的 127.0.0.1 临时端口进程，并在结束后关闭它。不会使用公网服务器或正在运行的游戏。
