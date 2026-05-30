# WPF / 上位机客户端接入指南

本文档面向 **WPF / 上位机 / 网站本地代理** 这类 `CLIENT` 角色客户端，覆盖两类能力：

- 控制面：HTTP 登录、设备列表、gRPC 注册、心跳、媒体订阅、EventStream 事件接收。
- 监控面：HTTP 管理订阅，客户端通过 UDP 接收服务端自产的高频监控数据 `0x07` / `0x08`。

当前系统不再提供 SSE 监控流。低频信息通过 HTTP 拉取，高频监控信息通过 UDP 推送。

---

## 1. 客户端角色

WPF / 上位机客户端应以 gRPC `CLIENT` 角色注册：

```csharp
var reg = await client.RegisterAsync(new RegisterRequest
{
  Token = string.Empty,
    Role = RegisterRequest.Types.EndpointType.Client,
    DeviceId = "wpf-monitor-01",
    RobotGeneration = 0,
    VrVersion = string.Empty
});
```

注册成功后得到：

- `SessionId`：后续 gRPC metadata、UDP HELLO/PING 签名密钥、监控订阅目标。
- `ClientIp` / `ClientPort`：用于显示和排障，不等价于 UDP endpoint。

`device_id` 当前主要用于展示，不能作为权限判断依据。监控订阅权限由现有 admin bearer token 控制。

说明：当前实现里，gRPC `Register` 还不会校验 `RegisterRequest.Token`。如果客户端只走 gRPC 信令链路，可以先 `Register`；如果客户端还要调用 HTTP 管理接口，则仍然需要先走 HTTP 登录拿 admin bearer token。

---

## 2. HTTP 登录与管理权限

客户端管理接口走 HTTP admin bearer token。

### POST /api/client/auth/login

请求体：

```json
{
  "username": "admin",
  "password": "Admin!20260523"
}
```

返回字段：

- `tokenType`
- `accessToken`
- `expiresUtc`
- `user.username`
- `user.role`

后续 HTTP 管理请求添加：

```http
Authorization: Bearer <accessToken>
```

---

## 3. 设备列表

当前提供三个 admin 设备列表接口：

- `GET /api/client/devices/robots?onlineOnly=true`
- `GET /api/client/devices/vrs?onlineOnly=true`
- `GET /api/client/devices/clients?onlineOnly=true`

返回项包含：

- `sessionId`
- `deviceId`
- `role`
- `robotGeneration`
- `vrVersion`
- `online`
- `pushConnected`
- `hasUdpEndpoint`
- `pairedSessionId`

媒体订阅时，通常从 robots 列表中取目标机器人的 `sessionId` 作为 `Subscribe.publisher_session_id`。

---

## 4. gRPC Metadata

`Register` 成功后，后续所有 gRPC 调用都必须携带 metadata：

```csharp
var headers = new Metadata
{
    { "session-id", reg.SessionId }
};
```

如果不带 `session-id`：

- `Ping` 无法刷新在线状态。
- `Subscribe` 无法绑定订阅者。
- `EventStream` 无法稳定绑定到客户端会话。

---

## 5. 推荐启动顺序

控制面与监控面完整启动顺序（推荐用于 WPF / 上位机客户端）：

1. HTTP `POST /api/client/auth/login` 获取 admin bearer token。
2. gRPC `Register(role=CLIENT)` 获取 `SessionId`。
3. 建立 gRPC metadata：`session-id = SessionId`。
4. 启动 gRPC `Ping` 心跳。
5. 打开 gRPC `EventStream`。
6. 向服务端 UDP 端口发送 `HELLO|SessionId|Timestamp|Signature`，建立 UDP endpoint。
7. 如需高频监控，按需调用 `0x07` 或 `0x08` 的 HTTP 监控订阅接口。
8. 按需 HTTP 拉取设备列表，选择机器人后调用 gRPC `Subscribe` 订阅媒体/配置相关信息。

```mermaid
sequenceDiagram
    participant WPF as WPF Client
    participant H as HTTP API
    participant G as gRPC Signaling
    participant U as UDP Server

    WPF->>H: POST /api/client/auth/login
    H-->>WPF: accessToken
    WPF->>G: Register(role=CLIENT)
    G-->>WPF: SessionId
    WPF->>G: EventStream(session-id metadata)
    loop every 5-10s
        WPF->>G: Ping(session-id metadata)
    end
    WPF->>U: HELLO|SessionId|Timestamp|Signature
    U-->>WPF: ACK
    WPF->>H: POST /api/client/monitor/subscriptions
    H-->>WPF: subscribed, prefix=0x07
    opt link monitor
      WPF->>H: POST /api/client/monitor/link-subscriptions
      H-->>WPF: subscribed, prefix=0x08
    end
    loop intervalMs
        U-->>WPF: 0x07 + JSON monitor envelope
    end
```

---

## 6. 心跳与事件流

### Ping

当前默认会话参数是：心跳建议周期 `3s`，在线超时窗口 `8s`。WPF 客户端建议每 `2s ~ 3s` 调一次 gRPC `Ping`，不要高于 `3s`。

```csharp
await client.PingAsync(new Heartbeat
{
    ClientTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
}, headers);
```

### EventStream

WPF 客户端应尽早建立 `EventStream`，用于控制面下行事件：

- 配对事件
- 系统指令
- 视频配置事件
- 音频配置事件

```csharp
using var call = client.EventStream(new EventSubscribe
{
    SessionId = reg.SessionId
}, headers);

await foreach (var evt in call.ResponseStream.ReadAllAsync(cancellationToken))
{
    switch (evt.PayloadCase)
    {
        case EventMessage.PayloadOneofCase.VideoConfig:
            break;
        case EventMessage.PayloadOneofCase.AudioConfig:
            break;
        case EventMessage.PayloadOneofCase.System:
            break;
    }
}
```

---

## 7. UDP endpoint 绑定

如果客户端要接收 UDP 数据，包括系统监控 `0x07`，必须先完成 UDP endpoint 绑定。

### HELLO

```text
HELLO|SessionId|Timestamp|Signature
```

### PING

```text
PING|SessionId|Timestamp|Signature
```

签名规则：

- `Key = SessionId`
- `Data = Type + SessionId + Timestamp`
- `Signature = HMACSHA256(Key, Data)`，转小写 hex

示例：

```csharp
static string SignUdpControl(string type, string sessionId, long timestampSeconds)
{
    var data = type + sessionId + timestampSeconds.ToString(CultureInfo.InvariantCulture);
    using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(sessionId));
    return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(data))).ToLowerInvariant();
}
```

客户端行为建议：

- gRPC 注册成功后立即发送 `HELLO`。
- 收到 `ACK` 表示服务端已绑定当前 UDP endpoint。
- 空闲时每 5 秒左右发送 `PING` 维持 NAT 映射。
- 超过 15 秒没有收到任意服务端 UDP 回包时，立即重发 `HELLO`。

服务端只用验签通过的 `HELLO/PING` 更新 UDP endpoint；普通数据包不会更新 endpoint。

---

## 8. 0x07 高频系统监控订阅

高频监控由 HTTP 控制订阅，UDP 数据面推送。订阅操作必须使用 admin bearer token，且目标 `subscriberSessionId` 必须已经存在并拥有 UDP endpoint。

### POST /api/client/monitor/subscriptions

```http
Authorization: Bearer <accessToken>
```

```json
{
  "subscriberSessionId": "client-session-id",
  "topics": ["udp_global", "signaling_rates", "online_summary", "runtime_tables"],
  "intervalMs": 1000
}
```

校验规则：

- admin bearer token 必须有效。
- `subscriberSessionId` 必须存在。
- 该 session 必须已有 UDP endpoint；否则返回 `409 Conflict`。
- `intervalMs` 范围为 `250..10000`，默认 `1000`。

支持 topic：

- `udp_global`：UDP 全局 rx/tx/txOk/txFail/retry/drop 快照。
- `signaling_rates`：gRPC/WS 信令收发速率和累计值。
- `online_summary`：服务端视角的注册数、在线数、按角色在线数、推送通道连通数。
- `runtime_tables`：后端运行时会话表、配对表、转发表、反馈路由等内部表数量快照。

说明：

- 这个 `POST` 是对当前 `subscriberSessionId` 的整条监控订阅做覆盖更新，不是局部增量 patch。
- `0x08` 链路监控已经拆到独立接口 `/api/client/monitor/link-subscriptions`。

其中 `udp_global` 里的 UDP 分类统计当前已包含：

- `0x01` video
- `0x02` pose
- `0x03` feedback
- `0x04` audio
- `0x05` telemetryLowRate
- `0x06` telemetryHighRate
- `0x07` system
- `0x08` system

成功响应：

```json
{
  "success": true,
  "message": "Subscribed",
  "publisherSessionId": "system:monitor",
  "subscriberSessionId": "client-session-id",
  "udpEndpoint": "192.168.1.20:52000",
  "prefixes": ["0x07"],
  "topics": ["udp_global", "signaling_rates", "online_summary", "runtime_tables"],
  "effectiveIntervalMs": 1000
}
```

其它接口：

- `GET /api/client/monitor/subscriptions`
- `DELETE /api/client/monitor/subscriptions/{subscriberSessionId}`

如果客户端还要接收 `0x08`，需要额外调用独立接口 `/api/client/monitor/link-subscriptions`。

---

## 9. UDP 0x07 监控包

服务端系统自产 UDP 数据从 `0x07` 开始；`0x05` 和 `0x06` 当前已经用于机器人 telemetry 转发，不属于 `0x07` 系统监控范围。

监控包格式：

```text
[0x07][UTF-8 JSON envelope]
```

JSON envelope 字段：

- `sequence`：针对该订阅目标递增的序号。
- `serverTimeUnixMs`：服务端发送时间。
- `publisherSessionId`：固定为 `system:monitor`。
- `targetSessionId`：订阅者 session。
- `prefix`：固定为 `0x07`。
- `topics`：本包携带的 topic 名称。
- `udp`：订阅 `udp_global` 时出现。
- `signaling`：订阅 `signaling_rates` 时出现。
- `online`：订阅 `online_summary` 时出现。
- `runtimeTables`：订阅 `runtime_tables` 时出现。

接收示例：

```csharp
var result = await udpClient.ReceiveAsync(cancellationToken);

if (result.Buffer.Length > 1 && result.Buffer[0] == 0x07)
{
    var json = Encoding.UTF8.GetString(result.Buffer, 1, result.Buffer.Length - 1);
    // 反序列化 JSON envelope，按 topics 分发到监控视图
}
```

`sequence` 可用于检测丢包或乱序。监控 UDP 包是高频可丢数据，不要求可靠重传。

---

## 10. UDP 0x08 链路监控包

`0x08` 使用独立的链路监控订阅接口，不与 `0x07` 共用订阅入口。

### POST /api/client/monitor/link-subscriptions

```http
Authorization: Bearer <accessToken>
```

```json
{
  "subscriberSessionId": "client-session-id",
  "linkId": "lk_6f7c6c5bf27d45db9b8d9dd6a5f7a0d7",
  "intervalMs": 1000
}
```

校验规则：

- admin bearer token 必须有效。
- `subscriberSessionId` 必须存在。
- 该 session 必须已有 UDP endpoint；否则返回 `409 Conflict`。
- `linkId` 必须提供且不能为空。
- `intervalMs` 范围为 `250..10000`，默认 `1000`。

说明：

- 一个客户端当前只允许订阅一条 `0x08` 链路。
- 新请求会直接把原订阅切换到本次传入的 `linkId`。

其它接口：

- `GET /api/client/monitor/link-subscriptions`
- `DELETE /api/client/monitor/link-subscriptions/{subscriberSessionId}`

链路监控包格式：

```text
[0x08][UTF-8 JSON envelope]
```

JSON envelope 字段：

- `sequence`：针对该订阅目标和 `0x08` 独立递增的序号。
- `serverTimeUnixMs`：服务端发送时间。
- `publisherSessionId`：固定为 `system:link-monitor`。
- `targetSessionId`：订阅者 session。
- `prefix`：固定为 `0x08`。
- `topics`：固定为 `[
  "udp_link_metrics"
]`。
- `linksUpdatedUtc`：链路统计服务最近一次 tick 时间。
- `activeOnly`：当前固定为 `true`。
- `subscribedLinkId`：本次 `0x08` 实际按哪个 `linkId` 过滤。
- `links`：链路数组。

链路对象字段：

- `linkId`
- `originSessionId`
- `sourceNodeId`
- `targetNodeId`
- `direction`：`ingress` 或 `egress`
- `mediaKind`：`video`、`pose`、`audio`、`telemetry_low_rate`、`telemetry_high_rate`、`feedback`、`system_monitor`、`topology_monitor`
- `active`
- `firstSeenUtc`
- `lastSeenUtc`
- `received`
- `routeMatched`
- `routeMiss`
- `forwardPlanned`
- `queueEnqueued`
- `queueDropped`
- `sendAttempt`
- `sendSuccess`
- `sendFail`
- `retry`
- `failureReasons`

其中 `received`、`routeMatched`、`routeMiss`、`forwardPlanned`、`queueEnqueued`、`queueDropped`、`sendAttempt`、`sendSuccess`、`sendFail`、`retry` 的结构一致：

- `perSecond.packets`
- `perSecond.bytes`
- `totals.packets`
- `totals.bytes`

`failureReasons` 包含：

- `noRoute`
- `noTarget`
- `queueFull`
- `noBufferSpace`
- `hostUnreachable`
- `networkUnreachable`
- `timedOut`
- `socketError`
- `unknown`

接收示例：

```csharp
var result = await udpClient.ReceiveAsync(cancellationToken);

if (result.Buffer.Length > 1)
{
  if (result.Buffer[0] == 0x07)
  {
    var json07 = Encoding.UTF8.GetString(result.Buffer, 1, result.Buffer.Length - 1);
    // 反序列化 0x07 envelope
  }
  else if (result.Buffer[0] == 0x08)
  {
    var json08 = Encoding.UTF8.GetString(result.Buffer, 1, result.Buffer.Length - 1);
    // 反序列化 0x08 envelope，按 links 渲染链路图与统计面板
  }
}
```

推荐接入流程：

1. 先调用 `GET /api/monitor/udp/links?activeOnly=true` 获取当前活跃链路。
2. 让用户或前端逻辑选择关注的 `linkId`。
3. 调用 `POST /api/client/monitor/link-subscriptions` 时携带 `linkId`。
4. `0x08` 收到后，以 `subscribedLinkId` 和 `links` 渲染局部链路视图，而不是假设服务端会返回全量拓扑。

---

## 11. 低频监控 HTTP 拉取

低频页面、调试工具或启动阶段可继续使用 HTTP 拉取：

- `GET /api/monitor/udp/stats`
- `GET /api/monitor/udp/links`
- `GET /api/monitor/system/stats`
- `GET /api/monitor/sessions`
- `GET /api/monitor/sessions/{sessionId}`
- `GET /api/system/config`

其中 `/api/monitor/udp/stats` 默认仅在 Development 环境启用；非 Development 需要配置 `Monitoring:Enabled=true`。

`/api/monitor/udp/links` 同样受相同监控开关控制，支持查询参数：

- `activeOnly=true|false`

返回字段：

- `updatedUtc`
- `activeOnly`
- `items`

`items` 中保留 `0x08` 的基础链路字段，并额外补充便于展示的：

- `origin`
- `source`
- `target`

其中默认展示名规则为：

- 普通 session 节点默认使用 `deviceId` 作为 `displayName`
- `system:udp` 默认显示为 `UDP 转发服务`
- `system:monitor` 默认显示为 `系统监控服务`
- `system:link-monitor` 默认显示为 `链路监控服务`

---

## 12. 媒体订阅

WPF 客户端不需要配对，也可以直接订阅某个机器人发布者。

```csharp
await client.SubscribeAsync(new SubscribeRequest
{
    Op = SubscribeRequest.Types.Operation.Subscribe,
    PublisherSessionId = robotSessionId,
    SubVideo = false,
    SubPose = true,
  SubAudio = true,
  SubTelemetryLowRate = true,
  SubTelemetryHighRate = false
}, headers);
```

取消订阅：

```csharp
await client.SubscribeAsync(new SubscribeRequest
{
    Op = SubscribeRequest.Types.Operation.Unsubscribe,
    PublisherSessionId = robotSessionId,
    SubVideo = false,
    SubPose = true,
  SubAudio = true,
  SubTelemetryLowRate = true,
  SubTelemetryHighRate = false
}, headers);
```

字段含义：

- `SubTelemetryLowRate = true`：订阅 `0x05`
- `SubTelemetryHighRate = true`：订阅 `0x06`

媒体订阅控制的是机器人/VR/客户端之间的媒体发布者与订阅者关系；`0x07` 系统监控发布者固定为 `system:monitor`，`0x08` 链路监控发布者固定为 `system:link-monitor`，都由 HTTP admin 接口控制。

---

## 13. 最小启动骨架

```csharp
var httpClient = new HttpClient { BaseAddress = new Uri(httpBaseAddress) };
var login = await httpClient.PostAsJsonAsync("/api/client/auth/login", new
{
    username = "admin",
    password = "Admin!20260523"
}, cancellationToken);

var auth = await login.Content.ReadFromJsonAsync<ClientLoginResult>(cancellationToken);
httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth!.AccessToken);

var channel = GrpcChannel.ForAddress(grpcAddress);
var client = new Signaling.SignalingClient(channel);

var reg = await client.RegisterAsync(new RegisterRequest
{
    Token = auth.AccessToken,
    Role = RegisterRequest.Types.EndpointType.Client,
    DeviceId = Environment.MachineName,
    RobotGeneration = 0,
    VrVersion = string.Empty
});

var headers = new Metadata { { "session-id", reg.SessionId } };

_ = Task.Run(() => StartHeartbeatLoopAsync(client, headers, cancellationToken));
_ = Task.Run(() => StartEventLoopAsync(client, reg.SessionId, headers, cancellationToken));
_ = Task.Run(() => StartUdpReceiveLoopAsync(reg.SessionId, cancellationToken));

await SendUdpHelloAsync(reg.SessionId, cancellationToken);

await httpClient.PostAsJsonAsync("/api/client/monitor/subscriptions", new
{
    subscriberSessionId = reg.SessionId,
  topics = new[] { "udp_global", "signaling_rates", "online_summary", "runtime_tables" },
    intervalMs = 1000
}, cancellationToken);
```

---

## 13. 本地模块划分建议

- `ClientAuthApi`
  - 负责 HTTP 登录和 bearer token 管理。
- `GrpcSessionClient`
  - 负责 `Register / Ping / Subscribe / EventStream`。
- `UdpEndpointClient`
  - 负责 UDP `HELLO/PING`、ACK/PONG/DACK、`0x07` 接收。
- `MonitorSubscriptionApi`
  - 负责 HTTP 创建/删除 `0x07` 系统监控订阅与 `0x08` 链路监控订阅。
- `RobotDirectoryService`
  - 负责 HTTP 获取机器人/VR/客户端列表。
- `EventDispatchService`
  - 负责把 EventStream 和 UDP 监控包分发到 ViewModel。

---

## 14. 第一版建议避免的事情

- 不调用 `Pair` 参与机器人/VR 配对流程。
- 不调用 `ListUnpaired` 作为设备目录。
- 不调用 `GetP2pInfo` 建立 P2P 路径。
- 不把高频监控塞进 gRPC `EventStream`。
- 不依赖 `device_id` 做权限判断。

WPF 客户端的稳定边界是：以 `CLIENT` 会话接入，HTTP 负责管理权限与低频查询，gRPC 负责控制面事件，UDP 负责高频可丢监控数据。