# 本地引用程序集

此目录用于放置编译 Mod 所需的本地依赖。DLL 不纳入版本控制，请从与你的游戏版本匹配的游戏安装目录和 MelonLoader 环境中准备。

主要依赖包括：

- 游戏生成的 `Assembly-CSharp.dll`、`Assembly-CSharp-firstpass.dll`。
- `UnityEngine*.dll`、`Unity.TextMeshPro.dll` 等 Unity 程序集。
- `MelonLoader.dll`、`0Harmony.dll`、`Il2CppInterop.Common.dll`、`Il2CppInterop.Runtime.dll`。
- `Il2Cppmscorlib.dll`、`Il2Cppootii.dll` 等 Il2Cpp 引用程序集。
- `Newtonsoft.Json.dll`。
- 本仓库 `服务器/Chat.Client` 项目生成的 `Chat.Client.dll`。

具体文件以各 `.csproj` 中的 `Reference` / `HintPath` 为准。Il2Cpp 与游戏程序集通常需要先由对应版本的 MelonLoader 初始化生成；请使用本机实际安装版本，避免混用不同游戏版本的程序集。

在仓库根目录生成聊天客户端：

```powershell
dotnet build .\服务器\Chat.Client\Chat.Client.csproj -c Release
Copy-Item .\服务器\Chat.Client\bin\Release\netstandard2.0\Chat.Client.dll .\libs\Chat.Client.dll
```
