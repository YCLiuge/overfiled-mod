# ChatGuardModule 1.2.1

## 关闭定期统计日志

1.2.1 新增根级 `LogStats`，默认 true。在游戏工作目录的 `UserData/ChatGuard.json` 根对象加入 `"LogStats": false`，约 2 秒自动重读后停止每 10 秒的统计日志。聊天和技能拦截继续生效，加载提示、配置提示及配置错误警告仍保留。不修改 MelonLoader 或其他模块的日志。改回 true 可恢复统计输出。旧版 DLL 不支持此配置项，需要先热更新 DLL。

## 1.2.0：交替表情洪峰保护

针对同一玩家不断切换表情 ID 的现场反馈，本版不依赖内容相同来判定刷屏：

- 先读取 PlayerId 并检查额度/静默状态，超额消息不再读取 Text、Name、Expression。使用缓存的静态回调避免为每次拒绝创建闭包。
- 原本只有通过内容检查才扣额度；本版在读取内容前预扣额度，非法内容和重复消息也会消耗读取预算，防止无限尝试导致字符串转换及 GC 压力。
- 新增根级 `FloodMuteSeconds`，默认 5 秒，范围 0–300，设为 0 可关闭静默。玩家额度耗尽后静默 5 秒；静默中继续发送会续期，必须停发满 5 秒才恢复。静默仅按该 PlayerId 生效，不屏蔽全部聊天。持续快速发送的正常玩家也可能被静默。
- 旧配置缺少 `FloodMuteSeconds` 会自动采用默认 5，无需删除原配置；配置文件不会自动补写此字段。普通重复检测和既有玩家、全局限流仍然保留。
- 日志在 `totals:` 前新增 `window`、`chatRx/s`、`chatPass/s`、`spellRx/s`、`spellDrop/s`。它们是最近采样区间的平均速率，使用实际时间差，卡顿导致间隔增长时不会按固定十秒错误计算。累计字段仍从模块加载开始计数。
- `chatPass/s` 专指实时消息处理器获准执行的数量，不代表屏幕最终显示条数；`dropped` 包含历史裁剪，不能总用 received−dropped 精确推算实时放行数。

该优化减少了进入补丁之后的字符串读取工作，未改变前置收包、解码、IL2CPP detour 的开销。Received 在持续攻击期间依然会增加，它记录到达入口的数量，并不表示消息都被显示。

应急时可以暂时将 `Spell.BlockAllRemote=true`，抑制经过现有技能入口的全部网络单位施法；会影响正常网络单位技能。聊天可将 `PlayerBurst=1`、`PlayerPerSecond=0.2`、`FloodMuteSeconds=15` 调得更严格，代价是普通快速聊天也可能被静默。不要把根级 Enabled 设为 false，那会关闭全部保护。

本版新增交替表情延迟读取、静默续期、非法内容读取预算和采样窗口检查，1.2.0 共 55 项检查通过。


独立聊天与网络单位技能保护热更新模块，目标框架 .NET 6 / x64，使用已有宿主的 `ModLoader.ILogic`，模块 ID 保持 `chat_guard`。

## 产物与构建

- [项目](ChatGuardModule.csproj)
- [入口及 Harmony 补丁](Entry.cs)
- [聊天策略](GuardPolicy.cs)
- [技能策略](SpellGuardPolicy.cs)
- [DLL](../GameMods/Mods/ChatGuardModule.dll)
- [热更新清单](../GameMods/Mods/version.json)

从仓库根目录运行：

```powershell
$env:MSBuildEnableWorkloadResolver='false'
$env:DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE='true'
dotnet build ChatGuardModule/ChatGuardModule.csproj -c Release
```

依赖已有的 `GameMods/Mods/mod.dll` 提供真实 ILogic 契约，以及 `libs` 中的游戏、MelonLoader、Harmony、Unity、IL2CPP 引用。不复制游戏依赖。每次构建自动更新完整清单中的版本和哈希。

## 加载与热更新

通过现有 TCP 热更新服务分发 DLL 和更新后的完整清单。客户端宿主开启自动检查、可以连接服务时，会按 `chat_guard` 下载、校验、加载；后续版本或 DLL 哈希改变会触发更新。发布清单时保留其他已有模块条目。

开发调试可在宿主主线程调用：

```csharp
ModLoader.LogicLoader.Instance.LoadModuleFromFile(
    "chat_guard", @"实际绝对路径\ChatGuardModule.dll", 10);
```

相同 ID 再次加载会走宿主卸载再加载流程。这是 ILogic 模块，仅复制进游戏 Mods 目录不能替代宿主加载流程。

`OnUnload()` 撤销自身 Harmony 补丁并清空聊天和技能状态；`OnLoad()` 重新安装四个补丁。没有后台线程、计时器或长期持有的游戏对象缓存。IL2CPP detour 及卸载后的真实行为仍需游戏内验证。

## 聊天拦截位置

1. `ChatSystem.OnChatMsgNotice(ChatMsgNotice)`：实时消息处理前按 PlayerId 限流，所有频道共享玩家额度；无消息的状态通知透传。
2. `ChatSystem.ShowChatMessage(ChatChannelType, ChatMsgData, long, bool)`：检查正文及昵称的长度、控制字符、行数和 TMP 标签，不重复扣实时消息额度。经过此方法的历史、私聊也接受内容检查。
3. `ChatSystem.OnChatMsgRecordInitNotice(ChatMsgRecordInitNotice)`：单批历史只保留最后 N 条，默认 100。不会清空已有聊天模型。

重复过滤比较该玩家最近一条已放行消息的文本与表情 ID；交替发送仍受玩家和全局速率限制。纯表情可通过但不豁免频率限制。

## 技能拦截位置与边界

第四个补丁位于 `Il2Cpp.SpellSystem.ForceCastSpell(Unit, int, int, int, Unit, Vector3, SpellCustomParameter, bool cacheSpellData, out SpellData)`，精确选择九参数重载，避免在多个施法包装入口重复扣额度。

- 默认只处理 `caster.Property.IsNetUnit == true` 的网络单位；本地单位透传。
- 同一网络单位的所有技能共用额度，切换技能 ID 无法绕过；采用单位 native 指针作为短期键，不是账号级封禁，切换或重新生成单位可能获得新额度。
- 独立技能全局预算，不消耗聊天令牌。
- 被拒绝时跳过原方法，设置返回值 `false` 和输出 `SpellData=null`。
- 不改打断、销毁、回收、CD、Buff 等生命周期方法，也不改发包。
- `BlockAllRemote=true` 可直接拒绝经过此入口的全部网络单位施法；本地仍透传。
- 单位或属性无法识别、互操作异常时记录 `spellErrors` 并放行，避免身份判断错误直接破坏战斗。

`IsNetUnit` 是游戏的单位分类，不能等同于“其他玩家账号”。可能包含其他网络控制单位；实际分类需要现场确认。默认阈值也需要按游戏正常连招、被动技能和衍生技能频率调整，限流或全部屏蔽可能影响正常动画、技能效果与客户端战斗表现。

当前只有互操作方法元数据，未证实 native 调用链。没有定位到独立技能网络接收函数，因此这是**施法执行入口保护**，不能宣称覆盖所有技能消息、所有 TryCast/TempSpell 路径或在网络解码前拦截。

## 配置

首次加载在游戏进程当前工作目录下生成 `UserData/ChatGuard.json`。每 2 秒检查修改时间。非法配置保留上一份有效值；修正文件后重试。成功重读会重置聊天和技能额度。

旧版配置没有 `Spell` 字段也可加载，会自动采用技能默认值；不会为了补字段覆盖你的文件。需要自定义时手动加入 `Spell` 对象。

```json
{
  "Enabled": true,
  "PlayerBurst": 4,
  "PlayerPerSecond": 0.8,
  "GlobalBurst": 24,
  "GlobalPerSecond": 12,
  "RepeatSeconds": 3,
  "MaxTextLength": 512,
  "MaxNameLength": 64,
  "MaxLines": 8,
  "RejectRichText": true,
  "MaxHistoryBatch": 100,
  "Spell": {
    "Enabled": true,
    "BlockAllRemote": false,
    "UnitBurst": 12,
    "UnitPerSecond": 6,
    "GlobalBurst": 80,
    "GlobalPerSecond": 40
  }
}
```

根级 `Enabled=false` 关闭聊天及技能保护。`Spell.Enabled=false` 只关闭技能保护。`Burst` 为短时突发额度，`PerSecond` 为每秒恢复数量，使用单调时钟；超过额度的请求直接丢弃，不排队重放。

文本长度以 UTF-16 代码单元计数。默认拒绝正文及昵称中类似 `<size=...>`、`<sprite=...>` 的标签开头，正常使用富文本的消息也可能被过滤，可按需设置 `RejectRichText=false`。

聊天和技能各最多记录 4096 个身份状态，120 秒未活动的状态在后续请求到达时定期清理；表满时拒绝新身份，等旧状态过期。多人严重刷屏时全局预算可能连带拒绝正常聊天或施法，这是本地处理预算的取舍。

## 日志与现场验证

加载成功日志包含 `remote spell guard`。有拦截或错误时最多每 10 秒输出一次累计统计，不逐条记录恶意内容。`Entry.Diagnostics()` 也可读取：

- `active`：四个补丁安装是否完成。
- `received` / `dropped` / `invalid` / `errors`：聊天接收、丢弃及历史裁剪、显示内容拒绝和读取异常计数。
- `tracked`：聊天玩家状态数量。
- `spellReceived`：识别为网络单位、进入技能保护的施法次数。
- `spellDropped`：技能限流或全部屏蔽次数。
- `spellErrors`：身份或互操作读取异常次数，异常时放行。
- `spellTracked`：技能单位状态数量。

建议先检查本地施法、其他玩家正常施法；受控高频施法时观察 `spellDropped`；启用全部屏蔽后确认网络单位技能是否被抑制、本地仍正常；替换 DLL 后确认宿主先卸载再加载且没有重复补丁。

若刷技能时 `spellReceived` 始终为 0，需检查该技能是否走其他入口、单位是否被标记为 IsNetUnit，或游戏热修复是否绕过此方法。若 `spellErrors` 持续增加，应收集游戏版本和日志排查，而不是据此宣称保护有效。

这是客户端处理保护，不阻止服务器发包，无法消除补丁之前的网络收包与解码耗时，也不保护未安装的客户端。聊天部分未整体限制离线私聊批次数量，绕过 ShowChatMessage 的内容路径也未覆盖。不能保证解决所有卡死。

## 验证结果

```powershell
$env:MSBuildEnableWorkloadResolver='false'
dotnet run --project tools/ChatGuardVerification/ChatGuardVerification.csproj -c Release
```

[验证程序](../tools/ChatGuardVerification/Program.cs) 共 55 项检查通过，包括十万条聊天及十万次远端施法压力模拟、预算恢复、身份隔离、全局预算、状态上限与过期回收、异常文本、旧配置兼容、技能开关，以及真实游戏 DLL 的底层重载与 bool/out 契约、模块 ILogic 元数据和卸载调用。

Release 模块构建 0 警告、0 错误。测试工具有 .NET 6 生命周期提示 NETSDK1138，框架跟随游戏宿主。尚未在游戏进程验证 Harmony detour、真实热更新及攻击现场效果，测试不代替游戏内验收。
