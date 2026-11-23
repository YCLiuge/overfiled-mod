# TCP 文件服务器

此程序为客户端提供文件下载，默认监听 `0.0.0.0:10086`，与客户端默认端口一致。当前实现限定 Windows，使用 Windows 文件句柄 API 校验实际文件路径。构建需要支持 .NET 6 的 .NET SDK；运行需要 Windows x64 的 .NET 6 Runtime。

## 构建与启动

在仓库根目录打开 PowerShell：

```powershell
dotnet build .\TcpFileServer\TcpFileServer.csproj -c Release
```

推荐将需要分发的文件放入仓库根目录下的 `GameMods\Mods`，创建目录并启动服务器：

```powershell
New-Item -ItemType Directory -Force .\GameMods\Mods
dotnet .\TcpFileServer\bin\Release\net6.0\win-x64\TcpFileServer.dll 10086 .\GameMods\Mods
```

第二个参数是服务根目录，相对路径以启动进程时的工作目录为基准。客户端请求的文件名或相对路径以此目录为基准，例如请求 `example.dll` 对应 `GameMods\Mods\example.dll`。

程序不会自动创建服务根目录；目录不存在时会报错退出。**仅将可公开分发的文件放进服务根目录及其子目录。** 当前 TCP 协议没有身份认证或加密，能够连接此端口的客户端可以按相对路径下载根目录内的文件。

## 参数与退出

```text
TcpFileServer.exe [端口=10086] [根目录=程序所在目录\Mods] [最大连接数=64] [超时秒数=30]
```

所有参数均为位置参数。例如将连接上限设为 32、超时设为 15 秒：

```powershell
dotnet .\TcpFileServer\bin\Release\net6.0\win-x64\TcpFileServer.dll 10086 .\GameMods\Mods 32 15
```

端口允许范围是 1–65535，最大连接数是 1–4096，超时秒数是 1–3600。请求头与完整文件名共享一次读取期限；传输文件时，每块异步读取和写入共享一次期限，正常的大文件传输没有整文件总时长限制。超过连接上限的额外连接会被关闭。

在服务器控制台按 **Ctrl+C**，或输入 `stop` 并回车，即可停止监听、取消活跃传输并等待连接关闭。已运行的旧实例必须先退出，再手动启动新编译的二进制，才能加载此次更新；单独重新编译不会更新正在运行的进程。端口已占用时，新实例会报错退出。

## 文件与协议约束

请求格式保持为 `[4 字节大端文件名字节数][UTF-8 文件名]`；响应保持为 `[1 字节状态][4 字节大端数据长度][数据]`。状态 `0` 表示成功、`1` 表示不存在、`2` 表示错误。

文件名 UTF-8 长度允许 1–4096 字节，文件大小不能超过 `Int32.MaxValue` 字节。服务器使用固定缓冲区流式发送文件。服务根目录、文件路径及各级祖先目录均不能是重解析点（例如目录联接或符号链接）；绝对路径、设备路径、NTFS 备用数据流以及越出根目录的路径会被拒绝。

## 复现验证

先完成上面的 Release 构建，然后在仓库根目录运行：

```powershell
python .\TcpFileServer\verify_tcp.py
```

或者在 `TcpFileServer` 目录内运行：

```powershell
python verify_tcp.py
```

脚本需要 Python 3，使用标准库，无需安装第三方依赖。它在 `TcpFileServer` 内创建临时测试数据，启动短生命周期服务器，通过真实 TCP 验证协议、路径越界与重解析点、流式文件内容、异步读写超时、连接上限及优雅退出，完成后退出测试服务器并清理临时数据。若已有其他程序监听 10086，脚本会验证默认端口冲突退出，并明确跳过默认端口成功绑定检查，不会停止已有程序。

当前项目保留 `net6.0` 以兼容已有部署；新 SDK 构建可能提示 NETSDK1138，因为 .NET 6 已停止支持。
