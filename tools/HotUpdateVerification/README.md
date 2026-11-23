# Hot update verification

独立 net6.0 控制台验证，不依赖游戏运行时或测试框架，不下载 NuGet 包。项目直接链接仓库里的 `LogicLoader.cs`、`LogicUpdater.cs`、`ILogic.cs`，引用 `libs/Newtonsoft.Json.dll`；仅为 MelonLogger 和 UnityEngine.Time 提供 stub。

从仓库根目录运行：

```powershell
powershell -ExecutionPolicy Bypass -File tools/HotUpdateVerification/run.ps1
```

脚本先构建主程序，再构建引用主程序契约的 fixture DLL，然后运行验证。需要 .NET SDK 和 .NET 6 运行时。本机 SDK 工作负载自动探测在沙箱中可能访问宿主进程失败，因此脚本关闭工作负载解析/更新通知；不改变机器级配置。

验证范围：

1. 有效模块首次加载，坏 DLL 与构造失败保留原实例，不触发旧模块卸载。
2. 新 OnLoad 产生部分副作用再抛异常时，先清理新实例，再对原旧实例调用 OnLoad 恢复；断言完整调用顺序和最终活跃实例。
3. 新实例清理抛异常时不重新激活旧实例。
4. Tick 内卸载当前模块后，后续存活模块仍各执行一次。
5. 实际 loopback TCP 响应负长度、超过上限的长度、截断响应体均被拒绝。
6. 实际 TCP 连接不返回响应时，100ms 总传输期限触发取消（断言总耗时低于 1500ms）。
7. 缓存条目 enabled=false、坏 SHA256、缺失 DLL 被忽略；有效条目可加载；EnableCache=false 不读缓存。
8. 下载的新模块 OnLoad 失败后，原 DLL 缓存字节、清单字节、已提交版本和原实例均保留。
9. 单模块与批量更新共享互斥门，两种占用方向都不发起第二个 TCP 请求。
10. 下载完成后只有 Tick 才触发生命周期和缓存/版本提交，OnLoad 的线程 ID 与 Tick 调用线程相同；提交前互斥门仍占用。
11. EnableCache=false 时，第二次相同版本/hash 检查仅拉取清单，不重复下载 DLL 或重载实例。

此外覆盖旧 OnUnload 抛异常后既不执行新 OnLoad，也不再次执行旧 OnLoad，且不保留注册项。

控制台按测试项输出 PASS，共 14 个场景（TCP 异常拆为多个场景）。任一失败以非零退出码终止。服务端绑定动态端口，不占用生产服务端口，结束时关闭 socket 并等待服务任务。每次运行的缓存证据保留在输出目录 `verification-runs/<随机ID>`，不会写游戏目录或正式缓存。

局限：stub 不模拟 Unity、IL2CPP、MelonLoader 插件绑定和游戏线程调度；测试证明真实 C# 加载/更新路径在显式 Tick 泵下的行为，不证明模块自己的外部副作用可事务回滚或 ALC 能立即被 GC 回收。
