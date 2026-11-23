# overfiled-mod

《开放空间》mod基于 C# / .NET 和 MelonLoader 的游戏 Mod 工程，包含模块热更新、家园编辑、本地家园、频道聊天、象棋、你画我猜及性能优化等功能。

## 项目结构

- `mod/`：Mod 入口、模块加载器、热更新及共享工具；解决方案入口为 `mod/mod.sln`。
- `Logic/`：主逻辑、游戏命令、相机轨道与 AI 控制接口。
- `HomeEditorModule/`、`LocalHomeModule/`：家园编辑与本地家园功能。
- `ChatGuardModule/`、`ChineseChessModule/`、`DrawGuessModule/`、`GameOptimizerModule/`：聊天保护、象棋、你画我猜与性能优化模块。
- `服务器/`：频道聊天服务器、客户端库、共享协议及测试程序。
- `TcpFileServer/`：模块文件分发服务器。
- `VersionManifestGenerator/`、`build/`：热更新清单生成器与共享构建配置。
- `tools/`：验证程序、录制脚本及资源导出工具源码。

各模块的功能与命令见对应目录中的 README；AI 控制接口见 [AIControlServer.md](AIControlServer.md)。

## 构建准备

1. 安装支持目标框架的 .NET SDK。Mod 项目目标为 .NET 6，聊天服务器目标为 .NET 9；部分验证工具运行时也需要 .NET 6。
2. 按 [本地依赖说明](libs/README.md) 准备游戏、Unity、MelonLoader 和 Il2Cpp 的引用程序集。
3. 从仓库根目录构建聊天客户端，将其 DLL 放入本地 `libs/`，供模块引用：

   ```powershell
   dotnet build .\服务器\Chat.Client\Chat.Client.csproj -c Release
   Copy-Item .\服务器\Chat.Client\bin\Release\netstandard2.0\Chat.Client.dll .\libs\Chat.Client.dll
   ```

4. 打开 `mod/mod.sln`，或按项目构建，例如：

   ```powershell
   dotnet build .\Logic\Logic.csproj -c Release
   dotnet build .\LocalHomeModule\LocalHomeModule.csproj -c Release
   dotnet build .\DrawGuessModule\DrawGuessModule.csproj -c Release
   ```

Mod 及功能模块默认输出到 `GameMods/Mods/`，构建过程会生成热更新版本清单。服务器的独立构建与部署见 [服务器说明](服务器/README.md)；版本清单规则见 [生成器说明](VersionManifestGenerator/README.md)。

## 本地配置

DeepSeek 功能从环境变量 `DEEPSEEK_API_KEY` 或游戏目录的 `Mods/deepseek_config.json` 读取凭据。配置格式：

```json
{
  "ApiKey": "YOUR_API_KEY",
  "BaseUrl": "https://api.deepseek.com/chat/completions",
  "Model": "deepseek-v4-flash"
}
```

实际凭据与本地配置不应提交到仓库。

## 仓库内容

仓库保留源码、项目配置、文档和验证脚本。游戏依赖 DLL、编译输出、导出的模型/音乐/图片、历史归档、临时实验、IDE 缓存、运行日志及本地密钥配置由 `.gitignore` 排除。`server/` 是生成的服务器部署目录，服务器源码位于 `服务器/`。

资源导出和实机验证工具可能使用本机游戏路径；运行前请按各工具说明设置对应路径与参数。
