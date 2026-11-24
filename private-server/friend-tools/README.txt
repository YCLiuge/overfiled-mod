开放空间：单插件 TCP 隧道联机

朋友端仅安装插件，不必额外启动桥接程序。
插件自动连接 relay.example.com:50505，采用自定义认证加密TCP，不使用HTTP/HTTPS传输协议。
HTTP只用于两端本地回环接口，隧道内是加密数据。

1. 准备兼容游戏1.1.12和已安装MelonLoader的游戏目录。
2. 完全退出游戏，双击Install.cmd，输入包含launcher.exe的游戏目录。
   或手动将OverField.PrivateServer.dll放到游戏Mods目录、private-server.json放到UserData目录。
   安装脚本会备份旧插件和旧连接配置。
3. 正常启动游戏。插件自动启动内置隧道，无需另外启动任何朋友端程序。
4. 使用自己的账号，与朋友进入相同场景和频道。

默认SkipHotUpdate=true：保留HostPlayMode原生初始化，优先使用已下载缓存中的真实资源清单，避免退回旧内置包。验证包初始化成功、清单版本存在且资源无缺失后，进入原生完成和登录流程，跳过官方资源版本检查和更新下载。
本地包不完整或manifest版本缺失会在MelonLoader日志报告SkipHotUpdate stopped，不会假装完成。
此模式不会自动下载缺失资源；需要已有与私服要求匹配的完整缓存资源。客户端仍保留真实资源版本校验。

插件占用回环端口18081/11001/12001，请勿同时运行旧的独立桥接客户端。
主机需保持Lolo、10086接入服务和原有FRP客户端运行。
如果连接失败，查看MelonLoader日志中的[PrivateServer]。
配置包含此私服的访问密钥，仅发给参与联机的朋友。
