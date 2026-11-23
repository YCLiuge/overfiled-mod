# 频道聊天 TCP 服务器 + 客户端库（C# / .NET）

一个可直接运行的 TCP 聊天服务：客户端连上来、加入某个**频道**，之后发的消息由服务器**转发给同一频道里的其他用户**（不回显给自己）。
客户端封装成了一个**零第三方依赖的 .NET Standard 2.0 类库（Chat.Client.dll）**，可以直接丢进你的 .NET Framework / .NET / Unity 工程。

```
客户端 A ─┐
客户端 B ─┼─ TCP ─→ Chat.Server ──→ 按频道分组转发（排除发送者本人）
客户端 C ─┘              │
                         ├─ 心跳 PING/PONG：静默超时自动踢掉死连接
                         └─ LIST：频道 + 在线成员快照
```

## BOARD 服务器独立发布

象棋及频道画架使用专用 `BOARD` → `BOARDMSG` 协议，不经过普通聊天 `MSG`。连接需完成 HELLO 且恰好加入一个频道，服务器填写真实 `fromId` / `from`，转发给同频道其他成员。正文最多 24000 UTF-8 字节；速率及出站行长均校验。

从仓库根目录执行 `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\服务器\tools\publish-board-server.ps1`，生成 `artifacts/ChessServer-Board`。按项目以 Release 发布，禁止依赖目录里残留的旧 Debug 文件。部署三个文件 `Chat.Server.dll`、`Chat.Server.deps.json`、`Chat.Server.runtimeconfig.json`，目标机安装 .NET 9 Runtime，用 `dotnet Chat.Server.dll` 启动。日志显示构建 `1.0.1-board` 与 `支持 BOARD -> BOARDMSG`。

完整部署及复验步骤见 [BOARD 部署说明](tools/BOARD-DEPLOY.md)，原有编译产物的本地 TCP 对比证据见 [board-baseline.json](tools/board-baseline.json)。这次对比中旧 Debug 返回 `unknown_command`，旧 Release 已接受 BOARD；仅编辑源码或上传文件不会更新运行中的旧服务器进程。

## 你画我猜房间扩展

现有频道聊天之外，服务器还提供隔离的小游戏房间。完整启动步骤和游戏操作见 [DrawGuessModule 使用说明](../DrawGuessModule/README.md)。从仓库根目录运行：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\DrawGuessModule\Start-Server.ps1 -Kind Game
```

该脚本与服务器直接启动时默认均使用 TCP 9000。房间协议为 `GAME_CREATE`、`GAME_JOIN`、`GAME_LEAVE`、`GAME_SEND`，客户端对应 `CreateGameAsync`、`JoinGameAsync`、`LeaveGameAsync`、`SendGameAsync`，状态通过 `GameRoomChanged` 和 `GameMessageReceived` 事件传递。

小游戏房间按 `(连接唯一频道, 房间名)` 隔离，两部分均沿用聊天频道的大小写不敏感规则；不同频道可创建同名房间。`GAME_CREATE`、`GAME_JOIN`、`GAME_SEND` 要求连接恰好加入一个频道，否则返回 `single_channel_required`。频道只取连接的实际订阅，不读取正文中的 `channel`；房间快照与 `GameRoomInfo.Channel` 会返回所属频道。使用自定义频道前先离开默认 `general`，再加入目标频道。

离开房间所属聊天频道时自动移除小游戏成员，房主离开则关闭该频道内的房间；同名的其他频道房间不受影响。`GAME_LEAVE` 根据连接已有的房间成员记录清理，不要求此时仍恰好一个频道；如果聊天 `LEAVE` 已完成清理，重复 `GAME_LEAVE` 会返回 `not_in_game`。DrawGuessModule 自动跟随游戏当前频道，切换时退出旧房间。

连接身份由服务器签发 SessionId，房间具有独立 InstanceId。每房最多 16 人，普通玩家只能给房主发消息，房主可私发或广播；房主离开即关闭房间。每连接游戏消息预算为每秒 128 条和 1 MiB，正文最多 32768 UTF-8 字节。服务器只负责身份与转发，题目、回合、作画校验和得分由创建房间的游戏进程管理。

## 目录结构

| 路径 | 类型 | 说明 |
| --- | --- | --- |
| `Chat.Client/` | **类库（要的 DLL）** | `ChatClient.cs`（客户端主体）、`Models.cs`（消息/事件模型）。默认目标 `netstandard2.0`，带中文 XML 注释文档 |
| `Chat.Protocol/` | **共享源码目录（不是项目）** | `Wire.cs`（命令常量与编解码）、`Json.cs`（自写的轻量 JSON）、`LineReader.cs`（行分帧）、`ChatException.cs`。服务器与客户端各自把它**编进自己的程序集**，所以客户端 DLL 单文件零依赖 |
| `Chat.Server/` | 可执行程序 | 服务器（net9.0），启动后监听 TCP 端口 |
| `Chat.Demo/` | 可执行程序 | 命令行示例客户端（`Chat.Demo.exe`）+ 19 项端到端自检。引用 `Chat.Client`，可编到 net9.0 或 .NET Framework 4.8 |
| `tools/smoke-test.ps1` | 脚本 | 一键冒烟测试：起服务器 → 跑两个运行时的自检 → 关服务器 → 打印日志 |
| `Chat.sln` | 解决方案 | VS / Rider 直接打开 |

## 快速开始

```powershell
# 终端 A：启动服务器（默认监听 0.0.0.0:9000）
dotnet run --project Chat.Server

# 终端 B：以 alice 登录，默认进 general 频道
dotnet run --project Chat.Demo -- --nick alice

# 终端 C：以 bob 登录
dotnet run --project Chat.Demo -- --nick bob
```

两个客户端都连上后，在 B 里敲一行文字回车，C 会立刻看到；C 敲的也会出现在 B 里。用 `/join room1`、`/switch room1` 换频道后，不同频道的两个人互相收不到消息。

一键自检（19 项断言 × 两个运行时）：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\smoke-test.ps1
```

> 交互式客户端命令：`/join <频道>`、`/leave [频道]`、`/switch <频道>`、`/list`、`/who`、`/ping`、`/quit`，其它输入一律作为消息发到当前频道。

## 把客户端当 DLL 用

### 生成 DLL

```powershell
dotnet build Chat.Client\Chat.Client.csproj -c Release
```

产物（两个文件一起拷走）：

```
Chat.Client\bin\Release\netstandard2.0\Chat.Client.dll     ← 客户端库
Chat.Client\bin\Release\netstandard2.0\Chat.Client.xml     ← 中文 XML 注释（IDE 提示用，可不带）
```

### 兼容性

| 目标框架 | 命令 | 谁能引用 |
| --- | --- | --- |
| **netstandard2.0**（默认） | `dotnet build Chat.Client` | .NET Framework 4.6.2+、.NET Core 2.0+、.NET 5/6/7/8/9、Mono、Unity（API 兼容级别 .NET Standard 2.0） |
| net48 | `dotnet build Chat.Client -p:ChatTfms=net48` | 仅 .NET Framework 4.8 |
| net9.0 | `dotnet build Chat.Client -p:ChatTfms=net9.0` | 仅 .NET 9（本机调试用） |
| 都要 | `dotnet build Chat.Client "-p:ChatTfms=netstandard2.0;net48"` | — |

* **零第三方依赖**：不需要 `System.Text.Json`、`System.Memory`、`System.Buffers`、`System.Threading.Tasks.Extensions`，也没有程序集绑定重定向的麻烦。协议里的 JSON 由库内自带的轻量解析器处理（见 `Chat.Protocol/Json.cs`）。
* **单文件**：协议层源码直接编进 `Chat.Client.dll`，不需要额外带上 `Chat.Protocol.dll`。
* **AnyCPU**，不依赖具体平台。
* 库只使用 .NET Standard 2.0 的 API（没有 `Span<T>`、`ValueTask`、`IAsyncDisposable`、`System.Text.Json`），因此老工程也能直接用。

### 在你的工程里引用

```powershell
# 方式 1：项目引用（同一个解决方案里最省事）
dotnet add 你的项目.csproj reference Chat.Client\Chat.Client.csproj

# 方式 2：直接引用编译好的 DLL
dotnet add 你的项目.csproj reference ..\lib\Chat.Client.dll
```

在 Visual Studio 里则是「添加引用 → 浏览 → 选 `Chat.Client.dll`」，或者在 `.csproj` 里手写：

```xml
<ItemGroup>
  <Reference Include="Chat.Client">
    <HintPath>lib\Chat.Client.dll</HintPath>
  </Reference>
</ItemGroup>
```

不想用 DLL 也可以只把 `Chat.Client\ChatClient.cs`、`Chat.Client\Models.cs` 和 `Chat.Protocol\` 下的四个 `.cs` 拷进你的工程——它们全都不依赖任何第三方包。

### 最小可用代码

```csharp
using System;
using Chat.Client;
using Chat.Protocol;

using (ChatClient client = new ChatClient())
{
    // 收消息 / 频道事件 / 断开通知（回调在后台线程上触发）
    client.MessageReceived += m => Console.WriteLine(m);          // <alice> #room1: 大家好
    client.ChannelEvent  += e => Console.WriteLine(e);             // bob join #room1
    client.Disconnected  += reason => Console.WriteLine("连接结束: " + reason);

    // 1) 连接 + 握手（会自动加入服务器默认频道）
    await client.ConnectAsync("127.0.0.1", 9000, "alice");
    Console.WriteLine($"昵称 {client.Nick}，频道 {string.Join(",", client.Channels)}，服务器 {client.Server}");

    // 2) 加频道
    var members = await client.JoinAsync("room1");

    // 3) 发消息，返回实际收到这条消息的人数（不含自己）
    int delivered = await client.SendAsync("room1", "大家好");
    Console.WriteLine($"已投递给 {delivered} 人");

    // 4) 频道列表 / 探活 / 离开 / 退出
    foreach (ChannelInfo c in await client.ListAsync())
        Console.WriteLine(c);                                      // #room1 (3 人): alice, bob, carol
    await client.PingAsync();                                      // 返回 "PONG"
    await client.LeaveAsync("room1");
    await client.QuitAsync("bye");
}
```

**API 一览**

| 成员 | 说明 |
| --- | --- |
| `ConnectAsync(host, port, nick, ct)` | 建连 + `HELLO` 握手；昵称重复抛 `ChatException("nick_in_use")` |
| `JoinAsync` / `LeaveAsync(channel, ct)` | 加入 / 离开频道，返回成员昵称列表 |
| `SendAsync(channel, text, ct)` | 发消息到频道，返回投递人数（**不含自己**） |
| `ListAsync(ct)` | 返回 `ChannelInfo[]`（频道名、人数、成员列表） |
| `PingAsync(ct)` | 应用层探活 |
| `QuitAsync(reason, ct)` / `Dispose()` | 优雅退出（`Dispose` 也会尽量先发 `QUIT`） |
| `Nick` / `Channels` / `Server` / `IsConnected` | 当前昵称、所在频道、服务器参数、连接状态 |
| `Server.MaxLineBytes` / `Server.MaxTextChars` | 服务器的单次传输上限 / 正文上限（发送前本地自检用） |
| `ChannelInfo.MembersTruncated` / `LastListTruncated` | 列表是否被单次传输上限裁剪过（`MemberCount` 仍是真实人数） |
| `MessageReceived` / `ChannelEvent` / `Disconnected` | 事件回调 |
| `RequestTimeout` / `ConnectTimeout` | 应答超时（默认 10s）、建连超时（默认 5s） |

要点：

* 全部是 `async` 方法；失败抛 `ChatException`（`ex.Code` 是错误码），超时抛 `TimeoutException`。
* **会自动应答服务器心跳**（收到裸 `PING` 回裸 `PONG`），所以长时间空闲也不会被踢。
* 事件回调在后台接收线程上触发，更新 UI 记得 `Dispatcher.Invoke`。
* 一个实例 = 一条连接 = 一个用户；要开多个用户就 new 多个实例。
* 为了兼容 .NET Framework，库采用 `IDisposable`（不支持 `await using`）；`using (...) { ... }` 即可。

## 协议

TCP 之上的 **UTF-8 文本行**协议：每行一条消息，**以 `\n` 分帧**，格式是「命令 + 可选 JSON 负载」。命令大写，负载必须写在一行里。任何请求都可以带一个 `"id"`，服务器会在应答里原样回显，方便请求/应答配对。

```
JOIN {"channel":"general","id":2}\n
```

### 客户端 → 服务器

| 命令 | 负载示例 | 说明 |
| --- | --- | --- |
| `HELLO` | `{"nick":"alice"}` | **必须的第一条命令**。成功后握手完成，并自动加入默认频道 |
| `JOIN` | `{"channel":"general"}` | 加入频道，`OK` 返回成员列表 |
| `LEAVE` | `{"channel":"general"}` | 离开频道 |
| `MSG` | `{"channel":"general","text":"你好"}` | 发消息；只转发给**同频道的其他人**，`OK` 返回实际投递人数 |
| `LIST` | 无 | 频道 + 成员 + 在线人数快照 |
| `PING` | `{"id":9}` | 应用层探活，服务器回 `PONG` |
| `PONG` | 无 | 应答服务器心跳（**必须回**，否则会被判死连接） |
| `QUIT` | `{"reason":"bye"}` | 正常退出 |

### 服务器 → 客户端

| 命令 | 负载 | 说明 |
| --- | --- | --- |
| `WELCOME` | `{"server":"dsh-chat","version":"1.0","heartbeat":15,"timeout":45,"handshakeTimeout":10,"defaultChannel":"general","maxLine":262144,"maxText":50000}` | 连接建立后服务器发的第一行，`maxLine`/`maxText` 是单次传输与正文上限 |
| `OK` | `{"id":2,"channel":"general","members":["alice","bob"]}` | 请求成功，回显 `id` |
| `ERR` | `{"id":5,"code":"not_in_channel","msg":"你不在频道 #other 中，请先 JOIN"}` | 请求失败 |
| `MSG` | `{"channel":"general","from":"alice","text":"你好","ts":"2026-10-08T11:30:00.1234567+00:00"}` | 别人发到频道的消息 |
| `EVENT` | `{"type":"join","channel":"general","nick":"carol","ts":"…"}` | 频道内有人加入/离开（`type` 为 `join` / `leave`） |
| `PING` | 无 | 服务器心跳，客户端需回裸 `PONG` |
| `BYE` | `{"reason":"timeout"}` | 服务器即将断开：`bye` / `timeout` / `handshake_timeout` / `server_shutdown` |

### 错误码

| code | 触发场景 |
| --- | --- |
| `bad_request` | 命令后跟的 JSON 解析失败 |
| `unknown_command` | 不认识命令 |
| `not_handshaken` | 还没 `HELLO` 就想 `JOIN`/`MSG`/`LIST` |
| `already_handshaken` | 重复 `HELLO` |
| `bad_nick` | 昵称为空、超过 32 字符、含空白或控制字符 |
| `nick_in_use` | 昵称已被别的连接占用 |
| `bad_channel` | 频道名为空、超长或含非法字符（只允许字母数字 `_ - . #`） |
| `bad_text` | 消息为空，或超过服务器的正文上限（默认 50000 字符） |
| `not_in_channel` | 往自己没加入的频道发言 |
| `msg_too_large` | 这条消息**转发出去**后会超过单次传输上限（转发时要补上 `from`/`ts`，比发进来的行更长）；只拒绝这一条，**连接保留** |
| `reply_too_large` | 应答本身超过单次传输上限（正常情况下列表会被裁剪，这是兜底） |
| `line_too_long` | 发来的单行超过单次传输上限（回错误后**直接断开**该连接） |
| `internal_error` | 服务器内部异常 |

### 一次完整会话

```
S: WELCOME {"server":"dsh-chat","version":"1.0","heartbeat":15,"timeout":45,"handshakeTimeout":10,"defaultChannel":"general"}
C: HELLO {"nick":"alice","id":1}
S: OK {"id":1,"nick":"alice","server":"dsh-chat","version":"1.0","channels":["general"]}
C: JOIN {"channel":"room1","id":2}
S: OK {"id":2,"channel":"room1","members":["alice","bob"]}
S: EVENT {"type":"join","channel":"room1","nick":"alice","ts":"…"}      ← 广播给 bob
C: MSG {"channel":"room1","text":"大家好","id":3}
S: OK {"id":3,"channel":"room1","delivered":1}                          ← alice 自己拿到回执
S: MSG {"channel":"room1","from":"alice","text":"大家好","ts":"…"}       ← 只发给 bob，不回显给 alice
```

### 不用本库，裸 Socket 也能对接

协议就是「一行一条」，任何语言的 TCP 客户端都能接：

```csharp
using System.Net.Sockets;
using System.Text;
using System.Text.Json;   // .NET Framework 上需要加 System.Text.Json 这个 NuGet 包

using TcpClient tcp = new TcpClient();
await tcp.ConnectAsync("127.0.0.1", 9000);
NetworkStream stream = tcp.GetStream();
StreamReader reader = new StreamReader(stream, Encoding.UTF8);
StreamWriter writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };

Console.WriteLine(await reader.ReadLineAsync());          // WELCOME {...}
await writer.WriteLineAsync("HELLO {\"nick\":\"alice\",\"id\":1}");
await writer.WriteLineAsync("MSG {\"channel\":\"general\",\"text\":\"大家好\",\"id\":2}");

while (await reader.ReadLineAsync() is { } line)
{
    int space = line.IndexOf(' ');
    string command = space < 0 ? line : line.Substring(0, space);

    switch (command)
    {
        case "PING":                                      // 服务器心跳：必须回 PONG
            await writer.WriteLineAsync("PONG");
            break;

        case "MSG":                                       // 别的用户发来的消息
            using (JsonDocument doc = JsonDocument.Parse(line.Substring(space + 1)))
            {
                JsonElement root = doc.RootElement;
                Console.WriteLine("<" + root.GetProperty("from").GetString() + "> " +
                                  root.GetProperty("text").GetString());
            }
            break;

        case "BYE":                                       // 服务器要断开连接了
            Console.WriteLine("服务器断开: " + line);
            return;
    }
}
```

## 服务器参数

```
dotnet run --project Chat.Server -- [选项]
```

| 选项 | 默认值 | 说明 |
| --- | --- | --- |
| `--host <地址>` | `0.0.0.0` | 监听地址（全部网卡）；本机测试可用 `127.0.0.1` |
| `--port <端口>` | `9000` | 监听端口 |
| `--channel <频道名>` | `general` | 握手后自动加入的频道，传空字符串则不自动加入 |
| `--heartbeat <秒>` | `15` | 服务器发 `PING` 的间隔，`0` = 关闭心跳 |
| `--timeout <秒>` | `45` | 静默超过这么久就判定为死连接（必须大于 `--heartbeat`） |
| `--handshake-timeout <秒>` | `10` | 必须在这么久内完成 `HELLO`，否则断开 |
| `--max-line <字节>` | `262144` | **单次传输（一行）的字节上限**，允许 256 ~ 1048576。入站超限：回 `ERR line_too_long` 后**直接断开**；出站超限：服务器绝不会发出这样的行（见下方「单次传输上限」） |
| `--max-text <字符>` | `50000` | 单条消息正文的字符上限，允许 1 ~ 65536 |
| `--queue <条>` | `64` | 单连接发送队列容量（条数），满 = 慢客户端，断开。一条最坏占 `--max-line` 字节，要算内存账 |
| `--verbose` | 关 | 打印调试日志 |
| `--help` | — | 打印帮助 |

监听 `0.0.0.0` 时局域网内的其它机器可以连；Windows 首次运行会弹防火墙授权，允许后才能被外部访问。

## 单次传输上限

`--max-line` 是**一条 TCP 报文行**的字节上限（既不是连接总量，也不是速率限制），服务器在两个方向上都严格按它执行：

| 方向 | 超限时的行为 |
| --- | --- |
| 入站（客户端 → 服务器） | `LineReader` 在**累积字节数超过上限的瞬间**就停止缓冲并抛错：回一条 `ERR {"code":"line_too_long"}`，然后**直接断开**该连接。不会「先收完再看」，所以对端无法用一条超长行把服务器内存撑大。 |
| 出站（服务器 → 客户端） | 服务器**永远不会发出超过上限的行**。三种处理：① 转发的消息加上 `from`/`ts` 后变长超限 → 只拒绝这一条并回 `ERR msg_too_large`，发送者与接收方都不受影响；② 请求的应答超限 → 降级为 `ERR reply_too_large`；③ 列表类字段（成员列表、`LIST` 的频道表）按预算裁剪，并用 `membersTruncated` / `truncated` 标记。 |

**为什么出站也要卡**：转发出去的行比发进来的行更长（要多带 `from`、`ts` 两个字段，约多 40~100 字节）。如果只卡入站，任何客户端只要发一条「刚好不超过上限」的消息，接收方就会收到一条自己解析不了的长行、被自己的读取器判成 `line_too_long` 而断开——等于同频道里任何人都能踢掉其他人。现在这条路径被堵住了，`Chat.Demo --smoke-limits` 里有专门的回归断言。

客户端侧（`Chat.Client.dll`）配合做的事：

* 握手时从 `WELCOME` 拿到上限：`client.Server.MaxLineBytes` / `client.Server.MaxTextChars`；
* 发送前在**本地**先量一遍，超限立刻抛 `ChatException`（`line_too_long` / `bad_text`），请求根本不发出去——既不会等 10 秒超时，也不会被服务器断开；
* 收到被裁剪的列表时，`ChannelInfo.MembersTruncated` 和 `ChatClient.LastListTruncated` 会告诉你「这只是列表的前一部分」，而 `MemberCount` 仍是真实人数。

配置建议：

* **两个上限要配套调**。单行上限管的是「一整行的字节数」，正文上限管的是「字符数」，一个中文字符占 3 字节、emoji 占 4 字节，所以正文上限写得再大，只要单行上限不够，消息照样发不出去。
* 只调大 `--max-text` 而不动 `--max-line`：客户端本地就会拦住并报 `line_too_long`（请求根本不发出去）。
* 只调大 `--max-line` 而不动 `--max-text`：消息仍受正文上限约束，不会出问题。
* 成员列表被裁剪是因为上限太小（例如只有 512 字节还想要完整成员表），把 `--max-line` 调大即可。

### 默认值就是「能发 50000 字符的长消息」

这三个默认值是配套的，**不加任何参数就能发长消息**：

| 参数 | 默认值 | 为什么 |
| --- | --- | --- |
| `--max-text` | `50000` | 单条消息最多 5 万字符 |
| `--max-line` | `262144` | 50000 个中文 = UTF-8 约 150 KB，加上转发头部（频道名/昵称/时间戳）留到 256 KB；纯 ASCII 只占 50 KB |
| `--queue` | `64` | 队列容量的单位是「条数」，一条最坏占 `--max-line` 字节：64 × 256 KB ≈ 16 MB，就是单个慢客户端的缓冲上限 |

内存账（按默认值算）：每个连接的行缓冲峰值 ≈ 2 × 256 KB，出站队列 ≤ 64 × 256 KB；100 个并发连接的粗略上限是几十 MB。

跑在内存很紧的机器上、或者只想传短消息，可以把上限压回小值：

```powershell
dotnet run --project Chat.Server -- --max-line 8192 --max-text 2000 --queue 1024
```

客户端**不需要改**：握手时会从 `WELCOME` 里读走 `maxLine`，并把自己读取器的上限同步过去（否则几万字符的长消息会被客户端自己的默认上限误判成超长行、白白断开连接）。

实测（`--smoke-large`，跑在默认配置上，net9.0 与 .NET Framework 4.8 都过）：50000 字符 ASCII（50 KB）与 50000 个中文（150 KB）都能完整转发，收发双方连接保持正常；超过 50000 字符时本地报 `bad_text`，连接不受影响。

专项自检：

```powershell
# 起一个上限很小的服务器，然后跑上限专项断言（14 项）
dotnet run --project Chat.Server -- --max-line 512 --max-text 600 --heartbeat 30 --timeout 60
dotnet run --project Chat.Demo   -- --smoke-limits
```

## 服务器实现要点

* **一连接两循环**：每个连接一条读循环（`LineReader` 按 `\n` 切分，带单行字节上限）+ 一条写循环；跨线程广播不会写出半行交错的数据。
* **有界发送队列 + 慢客户端保护**：广播时只是把行投进对方队列（`System.Threading.Channels`）。某个客户端读得慢导致队列写满时，直接断开它，绝不拖住其他人的转发。
* **频道与昵称用 `ConcurrentDictionary`**：`ChatHub` 管频道表和昵称占用表，广播只遍历同频道成员并跳过发送者本人。
* **心跳与半开连接**：服务器定期发裸 `PING`，超过 `timeout` 没收到任何数据（含 `PONG`）就发 `BYE {"reason":"timeout"}`，**先等写循环把 `BYE` 发完再关套接字**，客户端能明确知道断开原因。
* **握手限时**：连上不发 `HELLO` 的连接会在 `--handshake-timeout` 后被回收。
* **广播加入/离开事件**：进入、退出频道（含断线）都会给同频道其他人推 `EVENT`。
* **优雅关闭**：Ctrl+C 时向所有客户端发 `BYE {"reason":"server_shutdown"}` 再退出。
* **空频道自动回收**：最后一个成员离开后房间对象被移除。
* **协议零依赖**：JSON 是自写的轻量实现（对象/数组/字符串/整数/布尔/null，含 `\uXXXX` 转义与 32 层嵌套上限），服务器和客户端共用同一份源码，不会出现两端解析行为不一致。
* **单次传输上限双向生效**：入站超限即断开，出站永远不会发出超限的行（`TransferBudget` 统一做字节预算），保证「谁也不能把别人挤下线」。

## 构建与验证

```powershell
# 服务器（net9.0）
dotnet build Chat.Server\Chat.Server.csproj

# 客户端库：默认 netstandard2.0；也可指定别的目标框架
dotnet build Chat.Client\Chat.Client.csproj
dotnet build Chat.Client\Chat.Client.csproj -p:ChatTfms=net48

# 示例 + 自检
dotnet build Chat.Demo\Chat.Demo.csproj                 # net9.0
dotnet build Chat.Demo\Chat.Demo.csproj -p:ChatTfms=net48
```

`tools/smoke-test.ps1` 会依次做两件事，每个断言都在 net9.0 与 .NET Framework 4.8 上各跑一遍，全部通过时退出码为 0：

1. **默认配置**（只改心跳为 1s / 死连接 3s，其余全默认）→ 19 项端到端断言 + 6 项大消息断言；
2. **小上限配置**（`--max-line 512 --max-text 600`）→ 14 项单次传输上限专项断言。

已通过的项：

**常规套件（19 项）

1. 三个客户端 TCP 连接 + `HELLO` 握手、自动加入默认频道、`WELCOME` 参数下发
2. 重复昵称被拒（`nick_in_use`）
3. 同频道转发（投递人数、内容、来源、频道都正确）
4. 发送者本人**不会**收到自己的消息回显
5. 跨频道隔离（别的频道收不到）
6. 含引号、反斜杠、换行、制表、控制字符、中文、emoji 的消息**原样往返**（验证自写 JSON 的转义）
7. 未加入频道发言被拒（`not_in_channel`）、空消息（`bad_text`）、非法频道名（`bad_channel`）
8. `LIST` 频道与成员快照（嵌套数组/对象）正确
9. `PING`/`PONG` 探活
10. 静默 5 秒（超过服务器 `timeout`）后连接仍存活 ⇒ 客户端自动回 `PONG` 生效
11. 断线/退出时同频道其他人收到 `EVENT leave`，昵称被释放可重新登录
12. 不响应心跳的僵尸连接被服务器超时判定并断开（收到 `BYE timeout` 后套接字关闭）

**单次传输上限专项（14 项，用 `--max-line 512 --max-text 600` 的小上限服务器）

1. 上限随 `WELCOME` 下发（`maxLine` / `maxText` 正确）
2. 正文超过 `maxText` 时客户端**本地**直接报 `bad_text`，耗时 < 500ms、连接保持可用
3. 发来的整行超过 `maxLine` → 服务器回 `ERR line_too_long` 后**直接断开**该连接
4. **转发出去会超限的消息被拒（`msg_too_large`）**——这是关键的回归项
5. 被拒的消息没有转发给接收方
6. 发送者本人没有被断开（`PING`/`PONG` 仍然正常）
7. 接收方也**没有**因为别人的超限消息被挤下线
8. 没超限的消息照常转发（上限只挡超限的）
9. 成员列表超过预算时被裁剪（`membersTruncated=true`）
10. `LIST` 的成员列表同样被裁剪，且人数是真实值
11. 裁剪过程没有把任何连接挤下线

**大消息专项（6 项，跑在默认配置上，验证默认值确实能扛 5 万字符）

1. 服务器下发 `maxText=50000` / `maxLine=262144`
2. 50000 字符（约 50 KB）的消息**完整转发**，内容一字不差
3. 50000 个中文（UTF-8 约 150 KB）也能完整转发
4. 收发两端连接都还活着（接收方没有被 150 KB 的长行误判成超长行）
5. 超过 50000 字符时客户端本地报 `bad_text`，请求不发出去
6. 被拒之后连接仍然可用

另外用原生 TCP socket（模拟「别的语言写的客户端」）验证过：`\uXXXX` 转义、转义引号/反斜杠/`\b\f\n\r\t`、超长行（`line_too_long`）、坏 JSON（`bad_request`）、未知命令（`unknown_command`）、半行后直接断开（服务器不崩）。

## 常见问题

**Q：`dotnet build Chat.Client` 报 `NU1301 无法加载源 https://api.nuget.org/...`？**
netstandard2.0 目标需要一个隐式的 `NETStandard.Library` 包，第一次构建必须能访问 NuGet（有网即可）。完全离线的机器可以改成只编 .NET Framework 版本，它不需要任何包：

```powershell
dotnet build Chat.Client\Chat.Client.csproj -p:ChatTfms=net48
```

**Q：`dotnet build Chat.sln` 报「生成失败」但错误数是 0？**
这台机器的 .NET SDK 9.0.308 安装不完整（缺少 `Sdks\Microsoft.NET.SDK.WorkloadAutoImportPropsLocator` 等 workload 定位器目录），**解决方案级**构建在评估工作负载时必然失败，与本项目代码无关。按项目构建/运行（上面那些命令）完全正常；要彻底解决就修一下 SDK 安装。

**Q：成员列表 / 频道列表为什么只显示了一部分？**
被单次传输上限裁剪了（服务器宁可少列几项，也不发出超过 `--max-line` 的行，否则客户端会被自己的读取器判成超长行而断开）。标志位：协议里是 `membersTruncated` / `truncated`，库里有 `ChannelInfo.MembersTruncated` 和 `ChatClient.LastListTruncated`；`MemberCount` 始终是真实人数。默认的 `--max-line 262144` 已经能放下上千个成员，基本不会遇到裁剪；只有把它调到很小（例如 512）才会触发。

**Q：想让「单次传输上限」更严格/更宽松？**
`--max-line 8192`（更严，回到原来的默认值）或 `--max-line 1048576`（最宽，等于协议硬上限）都行，允许范围 256 ~ 1048576。注意它只管**一行的字节数**，不管「一个连接总共能传多少」或「每秒能传多少」——要防止刷屏得另外加限流（`ChatTcpServer` 里每个连接已有独立计数的位置可以挂）。

**Q：客户端发超长消息会怎样？**
`SendAsync` 会在本地先量一遍，直接抛 `ChatException`（`line_too_long` 或 `bad_text`），**请求不会发出去，连接也不会断**。如果用原生 socket 手写协议发超长行，服务器会回 `ERR line_too_long` 然后断开你。

**Q：想改成「把消息也回显给发送者」或「发给频道外所有人」？**
改 `Chat.Server/ChatTcpServer.cs` 的 `HandleMessage` 里的 `room.Broadcast(line, session.Id)`：第二个参数改成 `null` 就会连发送者一起发。

**Q：想做私聊 / 持久化历史 / 鉴权？**
协议留了扩展位：加一条 `PRIVMSG {"to":"bob","text":"…"}`（`ChatHub` 里已有昵称 → 会话的映射表 `_nicks`，查表直投即可）；历史消息在 `HandleMessage` 里落库；鉴权在 `HandleHello` 里校验 `token` 字段。

**Q：`MSG` 支持中文 / emoji 吗？**
支持，全链路 UTF-8（含 `\uXXXX` 转义）。Windows 控制台如果显示乱码，程序启动时已经尝试把输出编码设为 UTF-8。
