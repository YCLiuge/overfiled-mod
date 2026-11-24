# OverField.TcpBridge

独立 Go 工具，无第三方依赖。公网映射 `relay.example.com:50505 -> 本机 127.0.0.1:10086` 必须配置为 **原始 TCP 转发**。隧道握手与传输不是 HTTP、HTTPS、WebSocket 或 TLS；`http` 仅为加密请求中的路由名，允许内部 HTTP 应用通过隧道。

## 配置格式

使用 JSON，字段名如下。Key 必须是 32 字节对应的 **64 个 hex 字符**，两端相同。示例中的占位值需要替换成随机密钥；不要把真实 Key 写入日志或提交版本库。

服务端 `server.example.json`：

```json
{
  "Mode": "server",
  "Listen": "127.0.0.1:10086",
  "Key": "REPLACE_WITH_64_HEX_CHARACTERS"
}
```

客户端 `client.example.json`：

```json
{
  "Mode": "client",
  "Remote": "relay.example.com:50505",
  "Key": "REPLACE_WITH_THE_SAME_64_HEX_CHARACTERS"
}
```

`Listen` 默认且仅允许 `127.0.0.1:10086`；`Remote` 默认为 `relay.example.com:50505`，可在测试或迁移时指定其他 TCP host:port。`Mode` 必填，为 `server` 或 `client`。未知字段、非法 JSON、无效 Key 会直接拒绝，错误信息不回显配置内容或密钥。可用 PowerShell 生成随机密钥：

```powershell
$rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
$keyBytes = New-Object byte[] 32
$rng.GetBytes($keyBytes)
$rng.Dispose()
-join ($keyBytes | ForEach-Object { $_.ToString('x2') })
```

固定映射，不接受任意目标地址：

| route | client loopback listener | server backend |
|---|---|---|
| http | 127.0.0.1:18081 | 127.0.0.1:18080 |
| game | 127.0.0.1:11001 | 127.0.0.1:11000 |
| log | 127.0.0.1:12001 | 127.0.0.1:12000 |

一个本地 TCP 连接对应一个公网 TCP 连接和一个 route。三个 listener 共用同一个公网入口。客户端与服务端都不提供 HTTP CONNECT、SOCKS、任意端口代理或路由配置。

## 运行

```powershell
.\bin\OverField.TcpBridge.exe -config .\server.example.json
.\bin\OverField.TcpBridge.exe -config .\client.example.json
.\bin\OverField.TcpBridge.exe -config .\client.example.json -probe
```

需要先填入真实 Key。server 和 client 分别在相应机器运行；客户端的三个本地端口应空闲。Ctrl+C 关闭 listener 和活动连接。

`-probe` 不启动 listener，依次为 http/game/log 分别建立连接、认证并检查服务端后端是否可建立 TCP 连接；后端成功连接后立即关闭，不转发应用数据。三路全成功返回退出码 0，任一路失败返回非零。probe 检查 TCP 可连接性，不验证后端应用的业务健康。probe 使用 `Remote`，即使 Mode 为 server 也如此。

## 协议与资源限制

1. server 发送 `OFTCP001`、32 字节随机挑战、4 字节随机 server nonce prefix。
2. client 发送 32 字节随机 salt、4 字节随机 client nonce prefix。双方用预共享 Key 对协议域、方向标识、挑战与 salt 做 HMAC-SHA256，得到两个独立的 AES-256-GCM 密钥。
3. client 的首个认证加密记录包含请求类型、probe 标志及 route。server 验证认证标签、类型、长度和 route 白名单之后才 dial 固定 backend；成功或失败通过加密状态记录返回。旧握手无法在新随机挑战下通过认证。
4. 每方向 nonce 是该方向 4 字节随机 prefix 加 64 位严格递增计数器；计数器耗尽时拒绝继续发送/接收。方向独立密钥且每连接有新挑战和 salt，避免跨方向或跨连接复用 nonce。记录头是 4 字节大端密文长度，长度也包含于 GCM AAD；重排、篡改、重放记录会认证失败。
5. 单个数据负载最多 32 KiB，超长或不合法记录在分配负载内存前拒绝。握手读写有 8 秒总 deadline，dial 超时 5 秒；认证后正常长连接不设置空闲超时。
6. 使用认证加密的 EOF 记录保持 TCP 半关闭；意外断开视为截断错误。双向复制任一方向报错便关闭两端；正常半关闭给予另一方向最多 30 秒排空，随后 deadline 终止，避免半关闭后永久挂住。进程退出关闭所有活动连接。

密钥错误与认证错误不回显 Key。公网可以观察 TCP 流量大小和连接时间，不会看到 route 或应用内容的明文；本协议不隐藏流量元数据，也不提供 TLS PKI 或前向保密。

## 构建与测试

在此目录执行，所有生成文件与独立 GOCACHE 都位于本目录：

```powershell
$go = 'C:/Users/86156/Desktop/工具/mod/private-server/runtime-tools/go/bin/go.exe'
$env:GOCACHE = "$PWD\.gocache"
New-Item -ItemType Directory -Force .gotmp | Out-Null
$env:GOTMPDIR = "$PWD\.gotmp"
$env:GOTOOLCHAIN = 'local'
& $go test -v -timeout 45s ./...
& $go vet ./...
& $go build -trimpath -o bin/OverField.TcpBridge.exe .
```

测试只创建临时 listener，结束自动关闭，不启动持久服务。覆盖三路 loopback listener、跨记录大负载双向 echo 与半关闭、HTTP 请求转发、错误密钥/未知路由不 dial、密文篡改/限长/截断、挑战重放拒绝、方向密钥隔离，以及 probe 成功与后端失败。
