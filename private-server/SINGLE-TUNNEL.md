# 单插件、单 TCP 隧道联机

已使用现有映射 `relay.example.com:50505 -> 本机127.0.0.1:10086`。

朋友端只需要 MelonLoader 插件和配置，无需独立桥接程序。`OverField-Friend-Client.zip` 包含插件、配置、可选安装脚本和说明。使用兼容的游戏1.1.12和完整本地内置资源。

## 朋友安装

退出游戏，解压安装包，运行 Install.cmd 并输入实际游戏目录。也可以手动将 DLL 放入 Mods、JSON 配置放入 UserData。正常启动游戏，插件自动启动加密隧道。配置包含访问密钥，只发给参与联机的朋友。

本机主机已经安装同版本插件，但直接连接本地私服，无需绕公网。朋友包启用 TunnelEnabled，自动监听回环18081/11001/12001，连接公网50505。关闭游戏时关闭所有插件监听和活动连接。

## 主机启动

保留原有 FRP 客户端运行。两个终端分别执行：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\private-server\Start-Server.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\private-server\Start-PublicBridge.ps1
```

Lolo监听回环18080、11000、12000；接入服务只监听回环10086，不改防火墙和路由器。

## 传输和资源

公网链路是自定义 challenge + HMAC派生方向密钥 + AES-256-GCM记录的原始TCP协议，不使用HTTP或HTTPS。每个客户端连接按固定route转发到HTTP、游戏或日志后端，没有任意目标代理。HTTP只在两端各自的本地回环接口存在。

0.3.1修复：SkipHotUpdate=true 保留原生HostPlayMode初始化，优先读取已下载缓存中的真实manifest；不再切换到只读取旧内置包的OfflinePlayMode。验证初始化成功、Host模式、本地manifest版本存在，并生成不启动下载的资源计划，确认缺失资源数为0后，调用原生OnHotUpdateEnd，继续HotFixCS、shader预热及登录流程。缺少本地资源时记录SkipHotUpdate stopped，不假装加载成功，不回退官方更新。登录仍要求dispatch.currentVersion的补丁部分与真实CurrentPackageVersion完全一致。此功能跳过的是官方资源更新，不等于整个游戏绝不会出现任何其他官方接口请求。

## 2026-11-24 验证记录

- Go桥接9项顶层测试、go vet、Windows构建通过。
- C#插件Release构建通过，0警告/0错误。
- 0.3.1：185项重定向/本地缓存资源策略断言、25项真实客户端方法签名检查通过。
- 15项C#/Go协议兼容和生命周期检查通过：大数据分帧、HTTP转发、错误密钥、报文篡改、长度限制、半关闭及退出清理。
- 经本机10086和实际公网relay.example.com:50505的认证及三路后端连接探测均通过。
- 官方更新hook的方法元数据与原生调用流程已核对；尚未在实际游戏中确认0.3.1缓存流程完整登录，以及双人同场景互相可见。

构建朋友包：`powershell.exe -NoProfile -ExecutionPolicy Bypass -File ./private-server/Build-FriendPackage.ps1 -TunnelRemote '你的TCP映射域名:端口'`。文中的 `relay.example.com:50505` 是占位示例，部署时替换为自己的地址。生成密钥配置和发行包已加入gitignore。服务端配置的密钥与朋友插件配置必须一致，不要随意重新生成。
