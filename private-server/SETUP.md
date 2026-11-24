# 从源码搭建

目标客户端为 PC 1.1.12。以下命令在 Windows PowerShell、仓库根目录执行，需要 Git、.NET SDK 和对应的游戏本地依赖。第三方来源见 [REFERENCES.md](REFERENCES.md)。这些步骤面向新工作区；已有服务器请保留原配置和存档，不要重新初始化。

## 获取固定上游与工具链

```powershell
git clone https://github.com/BanterSR/Lolo.git ./private-server/Lolo
git -C ./private-server/Lolo checkout 375423e8c0806bfe8804001ac46d8bc371843045
git clone https://github.com/BanterSR/LoloResource.git ./private-server/Lolo/Resource
git -C ./private-server/Lolo/Resource checkout ab66e3e044c5196cc65193190b7dff2fa9caae2d
```

Lolo 的 data 目录来自同一上游提交。不要把本机生成的数据库复制到新仓库。

从 https://go.dev/dl/ 获取 Windows amd64 Go 1.27.4 ZIP，将其 `go` 目录解压到 `private-server/runtime-tools/go`。此次验证的 ZIP SHA256 为 `178ab98950ac69ab25069307dc6ade79f3816e414cce95b4da166a115cde1e7e`；解压前自行核对下载源和校验值。创建缓存目录并构建：

```powershell
New-Item -ItemType Directory -Force ./private-server/runtime-tools/tmp | Out-Null
powershell.exe -NoProfile -ExecutionPolicy Bypass -File ./private-server/Build-Server.ps1
```

构建脚本检查固定提交，应用补丁、安装回归用例，运行全量 Go 测试后生成服务端。不会替换正在运行的程序。

## 首次配置与启动

在 Lolo 工作目录使用上游 `config gen` 命令生成配置，具体参数以该版本帮助为准：

```powershell
Push-Location ./private-server/Lolo
try { ./bin/Lolo_windows_amd64.exe config gen } finally { Pop-Location }
```

在生成的本地配置中设置 HTTP 回环端口18080、游戏 TCP 回环端口11000、日志 TCP 回环端口12000，HTTP TLS 关闭；SQLite 使用本地数据库。PC 本地接入按上游提示关闭 `GateWay.CheckToken`；需要本项目沙盒规则时启用 `Game.Sandbox`。启动前检查配置，尤其不要使用生产数据库或真实支付凭据。

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File ./private-server/Start-Server.ps1
# 另一个窗口：
powershell.exe -NoProfile -ExecutionPolicy Bypass -File ./private-server/Test-Server.ps1
```

## 客户端与朋友联机

按根目录 libs 说明准备本地程序集，再构建 ClientRedirect 并使用 Install-Client 安装。客户端需自行安装 MelonLoader，且拥有兼容版本的完整本地资源。

本地连接不需要公网桥接。朋友联机见 [SINGLE-TUNNEL.md](SINGLE-TUNNEL.md)；其中公网地址是原部署示例，部署者应改为自己的 TCP 映射。密钥在本地生成，含密钥的配置和安装包只交给参与者，不要提交 Git。

## 更新与存档

正常更新先构建 `Build-Server.ps1 -Stage`，再运行 Stop-Server 并确认退出存档完成，备份完整数据库目录（含 WAL/SHM）和旧程序后替换。禁止直接替换运行中的程序或覆盖在线数据库。现有本机检查记录中的路径、备份目录和测试账号是历史记录，不是仓库提供的资源。
