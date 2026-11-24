# 来源与参考项目

本目录是 overfiled-mod 的本地私服集成，包含原创客户端接入、桥接、集成脚本，以及在下列上游基础上的服务端增量补丁。并非所有游戏功能均已实现。

| 项目 | 地址 | 使用方式 |
| --- | --- | --- |
| Lolo | https://github.com/BanterSR/Lolo | 实际服务端基础；固定提交 `375423e8c0806bfe8804001ac46d8bc371843045`，修改以 `server-patches/local-server.patch` 发布 |
| LoloResource | https://github.com/BanterSR/LoloResource | 服务端配置与资源；固定提交 `ab66e3e044c5196cc65193190b7dff2fa9caae2d`，不随本仓库打包 |
| of-ps | https://github.com/byzp/of-ps | 开发时参考的相关实现；实际运行服务端基于 Lolo |
| MelonLoader | https://github.com/LavaGang/MelonLoader | 客户端插件加载环境，需自行安装 |

感谢各项目作者与贡献者。请查看各上游仓库的许可证、版权声明及使用说明；本仓库的署名不改变第三方源码和资源的许可条件。游戏、Unity 程序集、客户端资源及相关商标属于相应权利人。本仓库不分发游戏程序集、完整资源、玩家存档、隧道密钥或含密钥的朋友安装包。

服务端补丁文件中的原始上下文及被修改代码来自 Lolo；新增实现与测试用于本项目的沙盒流程与兼容性验证。Go 和 .NET 依赖见各项目的 go.mod / csproj，其许可仍由相应上游决定。
