# 游戏本机指令接口

Logic 模块从 1.0.2 开始自动启动 AIControlServer，默认地址为 `http://127.0.0.1:8080/api/`。接口复用原有宿主和 Logic 指令处理函数，在 Unity 主线程执行。仅监听本机 IPv4，不提供浏览器跨站访问。

## 在已启动的游戏中启用

1. 保持 TcpFileServer 运行，根目录指向本项目的 `GameMods\Mods`，端口为 `10086`。需要启动时在项目根目录运行：

   ```powershell
   powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\HotUpdateModule\Start-Server.ps1
   ```

2. 等当前构建完成，在游戏聊天框发送小写 `update`，加载新的 `logic_main v1.0.6`。不需要为了更新 Logic 重启游戏。
3. 游戏日志应出现 `[AI_API] 服务已启动: http://127.0.0.1:8080/api/`。原来的 `api` 游戏指令仍可用，重复启动不会创建第二个服务。
4. 在项目根目录执行健康检查：

   ```powershell
   powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Invoke-GameCommand.ps1 -Action Health
   ```

   `moduleVersion` 应为 `1.0.6`。`mainThreadActive: true` 表示最近三秒内游戏主线程驱动过接口。只有进入能操作角色、玩家信息已初始化的场景后才能执行玩家指令。

## 本机调用脚本

[Invoke-GameCommand.ps1](<./Invoke-GameCommand.ps1>) 默认输出完整 JSON，等待排队操作完成；如果结果还没完成，会给出回执查询方式。示例：

```powershell
# 查询玩家、位置、当前场景、已加载模块和调试开关。
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Invoke-GameCommand.ps1 -Action State

# 查询所有支持的指令名、目标和简要用法。
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Invoke-GameCommand.ps1 -Action Commands

# 切换碰撞箱和准星探测显示。重复执行会再次切换。
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Invoke-GameCommand.ps1 -Command "box" -RequestId "box-test-001"

# 指定现有宿主指令。
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Invoke-GameCommand.ps1 -Command "tp 10 2 30" -Target host

# 查询已有请求；相同 ID 可防止重复分发同一条指令。
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Invoke-GameCommand.ps1 -Action Receipt -ReceiptId "box-test-001"
```

脚本支持 `-Action Command|Health|State|Commands|History|Receipt`，`-Target auto|logic|host`，`-RequestId`，`-BaseUri` 和 `-TimeoutSeconds`。默认目标 `auto` 根据指令名选择 Logic 或宿主，只执行一条处理链。需要再次执行开关操作时使用新的请求 ID。

## mj 相机轨道编辑器（1.0.6）

`mj` 或 `mj start` 打开编辑器；`mj stop` 关闭并恢复原相机控制器和鼠标状态。普通关闭保留内存中的轨道，重新打开可以继续编辑；模块热卸载会释放这些内存数据，重要轨道请先通过面板保存。存档位置仍为游戏 `UserData/mod储存数据/orbit_data.json`，兼容旧档。

`mj play` 播放相机轨道，跳过不足两个点的轨道；没有可播放轨道时返回明确错误。`mj pause` 暂停相机轨道。`mj status` 在游戏日志输出状态。`F10` 退出、`Tab` 切换相机接管、数字键 `1` 显隐轨道、`2` 切换看向目标，原有快捷键继续可用，输入框编辑时不会触发数字快捷键。

`GET /api/state` 的 `data.result.orbit` 包含 `active`、`cameraControl`、`cameraPlaying`、`personPlaying`、`recording`、轨道数量、点数和进度，以及已分配的可视对象/材质/列表行数量。速度曲线倍率为零表示保持当前位置，状态中的 `pausedBySpeedCurve` 会标记这种情况。

```powershell
.\Invoke-GameCommand.ps1 -Command "mj"
.\Invoke-GameCommand.ps1 -Action State
.\Invoke-GameCommand.ps1 -Command "mj stop"
```

曲线节点、轨道标记、材质和点列表行采用复用方式。读档在替换现有轨道前校验全部数据；保存使用同目录临时文件和原子替换。相机和人物基础速度会随档案保存。上限为 128 条相机轨道、每轨道 4096 个位置点、每条速度曲线 256 个节点（含端点）。

存档验证：`dotnet run --project tools/OrbitVerification/OrbitVerification.csproj`。游戏内回归脚本见 [verify-live.ps1](<./tools/OrbitVerification/verify-live.ps1>)；仅用于新加载、未开启且没有内存轨道的测试会话，会创建并清理临时测试轨道，不写入存档。

## 环绕拍摄与干净画面（1.0.6）

拍摄镜头独立于编辑器中的相机/人物轨道，不修改它们，也不写入存档。先用 `mj` 开启编辑器，再准备镜头：

| 指令 | 作用 |
| --- | --- |
| `mj shot [秒数 半径 高度 环绕角度]` | 以当前玩家位置为中心，准备暂停在起点的环绕镜头；默认 `16 4 2 100`，自动隐藏画面 UI，初始 FOV 为 45° |
| `mj play` / `mj pause` | 播放/暂停已准备的镜头；到终点自动停住，再次播放从头开始。没有拍摄镜头时沿用原轨道播放行为 |
| `mj seek 0.5` | 定位到总时长的 50%；暂停时可用来检查构图，播放时可定位，定位到 1 会停止 |
| `mj fov 50` | 修改垂直视野角，允许 20–100° |
| `mj clean on` / `mj clean off` | 临时隐藏/恢复当时开启的游戏 Canvas、编辑器、轨道标记与鼠标；重复执行保持相同状态 |
| `mj shot clear` | 清除临时镜头，恢复之前的相机父节点、局部变换、接管模式、控制器与 FOV，并显示原 UI |
| `mj stop` | 结束编辑器，恢复游戏控制、HUD、相机层级与视野角；热卸载也执行恢复 |

参数允许：时长 2–120 秒、半径 1–50 米、高度 0.5–20 米、环绕角度 -360–360°（绝对值至少 1°）。负角度反向环绕，小数点使用 `.`。镜头使用独立的实际时间与平滑起止速度，播放一次后保持终点；不做场景碰撞避让，拍摄前可以用 `mj seek` 预览。拍摄镜头存在时，`F9` 播放/暂停，`F10` 退出，其余编辑快捷键暂不处理。

`data.result.orbit.cinematic` 提供 `prepared`、`playing`、`duration`、`elapsed`、`progress`、`radius`、`height`、`sweepDegrees`、`cleanFrame`、`hiddenCanvases`、`fieldOfView` 和实际 `cameraPosition`。`orbit.cameraPlaying` 同时反映普通轨道和临时镜头的播放状态。

项目提供 [Film-Orbit.ps1](<./tools/Film-Orbit.ps1>) 自动配合本机接口拍摄：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Invoke-GameCommand.ps1 -Command "mj"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Invoke-GameCommand.ps1 -Command "mj shot 16 3.2 1.8 140"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\Film-Orbit.ps1 -Output .\artifacts\orbit-film.mp4
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Invoke-GameCommand.ps1 -Command "mj stop"
```

录屏器只捕获游戏客户区。脚本会将游戏切到前台，等 FFmpeg 捕获首帧后才开始镜头，结束或失败时尝试暂停；编辑器保持开启，完成后请执行 `mj stop` 恢复操作。`-CheckOnly` 仅读取准备状态。已有输出需要显式 `-Overwrite`。输出为无声 H.264 MP4、yuv420p、30fps，总时长为剩余镜头时长加 2 秒；奇数尺寸会在右侧/底部裁去最多一个像素。录屏期间保持游戏窗口可见且位置不变，避免其他窗口覆盖。

[Record-GameVideo.ps1](<./tools/Record-GameVideo.ps1>) 也可独立执行 `-Mode Inspect|Preview|Record`，`-BringToFront` 显式激活游戏，`-Mode Preview -Output .\artifacts\preview.png` 截取构图。两个脚本自动查找本机 FFmpeg，也可传 `-FfmpegPath`；同时保留录屏日志、进度与拍摄回执 JSON。验证程序现包含 10 组存档测试和 9 组镜头数学测试，游戏内脚本另验证拍摄准备、定位、参数拒绝、暂停、UI 与 FOV 恢复。

## HTTP 接口

所有响应均为 UTF-8 JSON，包含 `code`、`msg`、`data`、`sessionId`。`code` 与 HTTP 状态码一致。`sessionId` 在服务重新启动或 Logic 热重载后改变。

| 方法 | 路径 | 用途 |
| --- | --- | --- |
| GET | `/api/health` | 服务版本、会话、队列长度和主线程活跃状态，不依赖游戏线程应答 |
| GET | `/api/commands` | 支持的指令列表、目标和用法 |
| POST | `/api/command` | 提交游戏指令，返回执行回执 |
| GET | `/api/commands/{id}` | 查询请求回执 |
| GET | `/api/state` | 在主线程读取玩家状态和模块信息 |
| GET | `/api/history` | 在主线程读取当前聊天频道最后 20 条消息 |
| POST | `/api/chat` | 保留的聊天发送接口，字段为 `context`，最多保留 50 个 UTF-16 字符 |

### 提交指令

```http
POST /api/command HTTP/1.1
Host: 127.0.0.1:8080
Content-Type: application/json; charset=utf-8

{"id":"box-test-001","command":"box","target":"auto","waitMs":1500}
```

字段说明：

- `command`：必填，1 到 2048 字符的单行字符串，拒绝换行、制表符等控制字符。
- `target`：可选，默认 `auto`；也可指定 `logic` 或 `host`。
- `id`：可选，1 到 80 位字母、数字、下划线或短横线；省略时由服务生成。建议调用端显式生成并保留。
- `waitMs`：可选，0 到 5000 的整数，默认 1500。设为 0 可立即获得排队回执。

处理完成的响应示意：

```json
{
  "code": 200,
  "msg": "success",
  "sessionId": "每次服务启动生成的标识",
  "data": {
    "id": "box-test-001",
    "kind": "command",
    "status": "completed",
    "target": "auto",
    "command": "box",
    "result": {
      "dispatched": true,
      "target": "logic",
      "command": "box",
      "message": "指令处理函数已返回；异步操作请继续检查状态或日志",
      "state": { "collisionDebug": true, "raycastDebug": true }
    },
    "error": null
  }
}
```

`completed` 表示指令处理函数已经返回。`up`、`update`、协程、资源加载等异步动作仍需检查游戏日志或后续状态；原有部分处理函数会吞掉错误或忽略参数，接口不会将其解释成游戏效果已经达成。常用 `tp`、`r`、`f`、`rt`、`get`、动作与技能参数有额外校验；其他指令沿用原项目语义。

输入指令直接分发，不会把指令文本发送到聊天频道。现有处理函数自身仍可能发消息，例如宿主的 `update` 会调用 `OK()`，`chat` 指令和 `/api/chat` 会发送聊天内容。宿主目标沿用原有 `TipMsgCore.command` 的完整行为：活跃的五子棋、炸弹等小游戏也可能接收同一文本。`state` 中的 `hostCommandState` 显示当前宿主状态；建议退出这些小游戏后再发送宿主工具指令。

### 排队与重试

- HTTP 202 表示还在排队或处理中，此时 `data.id` 是查询凭据。后续用 `GET /api/commands/{id}` 查询，不能因为等不到响应就用新 ID 重发开关指令。
- 查询回执成功的 HTTP 状态为 200，执行状态看 `data.status`：`queued`、`running`、`completed`、`failed`、`expired`、`cancelled`。
- 同一会话、回执保留期间，同 ID、同内容只分发一次；同 ID、不同内容返回 409。回执最多保留 512 条，并在后续请求时清理超过约五分钟的已完成记录；容量达到上限也会提前移除最旧的已完成记录。
- 排队超过 15 秒仍未执行的请求会变为 `expired`，不会在恢复游戏后补执行。已开始执行的处理函数不能被 HTTP 超时中断。
- 队列最多 128 条，每帧最多处理 16 条且在约 5 ms 后停止领取新任务；单个原有指令自身耗时不受抢占。
- 服务停止或模块卸载会取消尚未执行的请求。热重载会关闭旧连接并启动新会话；连接断开后的执行结果应结合日志确认。
- `GET /api/state` 成功时，快照在 `data.result`。它或历史读取超过默认等待时间时也会返回 202 和可查询回执。`GET /api/history` 立即完成时保留原有 `data: []` 的成功结构。

错误状态包括 400（格式或字段错误）、403（非本机或浏览器 Origin）、404（路由或回执不存在）、405（方法错误）、408（排队过期）、409（ID 冲突）、413（请求体超过 16 KiB）、415（非 JSON 请求体）、422（主线程处理异常）、429（队列或回执容量不足）、503（停止服务）。主线程请求失败的具体原因在 `data.error`。

## 代码与验证

- [AIControlServer.cs](<./Logic/管理器/AIControlServer.cs>)：游戏状态读取、原有指令路由、生命周期入口。
- [AIControlHttpServer.cs](<./Logic/管理器/AIControlHttpServer.cs>)：HTTP 路由、每请求回执、主线程队列、幂等与过期处理。
- [Entry.cs](<./Logic/Entry.cs>)：在每帧开头驱动 API，在卸载时先停止 API。
- [HTTP 集成验证脚本](<./tools/AIControlVerification/run.ps1>)：使用真实传输源码、独立回调验证 HTTP，不调用游戏或向游戏聊天发送消息。

重新构建会自动更新共享输出目录中的 [Logic.dll](<./GameMods/Mods/Logic.dll>) 和 [version.json](<./GameMods/Mods/version.json>)，仍排除 [mod.dll](<./GameMods/Mods/mod.dll>)：

```powershell
$env:DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE = 'true'
$env:MSBuildEnableWorkloadResolver = 'false'
dotnet build .\Logic\Logic.csproj -c Release -p:Platform=x64
```
