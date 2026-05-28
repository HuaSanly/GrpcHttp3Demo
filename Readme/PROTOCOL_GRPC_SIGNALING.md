# gRPC 信令协议 (Signaling over gRPC)

## 目标

- 提供 **会话注册**、**配对**、**订阅**、**心跳**、**事件推送** 等信令能力。
- 通过 `SessionId` 标识客户端身份，并作为 UDP 鉴权/握手的共享密钥来源。

> 注意：本文的 gRPC `Ping(Heartbeat)` 是 **会话心跳 RPC**。
> UDP 侧也有一个文本控制包 `PING|SessionId|Timestamp|Signature`，用于 NAT 保活/更新 UDP 端点，两者不是同一个东西。

---

## 1. 会话头（必须携带）

客户端调用 `Register` 成功后会拿到 `RegisterResponse.SessionId`。

从这一刻起，客户端对服务器的 **后续所有 gRPC 调用**（至少包括 `Ping`/`Pair`/`Subscribe`/`ListUnpaired`/`EventStream`）都必须携带 gRPC metadata/header：

- Header key: `session-id`
- Header value: `RegisterResponse.SessionId`

如果不带该 header：
- 服务端无法把请求绑定到一个会话
- `Ping` 不会刷新心跳时间，服务端后台清理任务会把会话判定为超时并清理

### grpc-dotnet C# 示例

```csharp
using Grpc.Core;
using GrpcHttp3Demo.Protos;

// 1) Register
var reg = await client.RegisterAsync(new RegisterRequest
{
    // token/role/device_id...
    // robot_generation: 仅机器人填写，VR 传 0
});
var sessionId = reg.SessionId;

// 2) Build metadata once
var headers = new Metadata { { "session-id", sessionId } };

// 3) gRPC heartbeat
await client.PingAsync(new Heartbeat
{
    ClientTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
}, headers);

// 4) Other unary calls
await client.PairAsync(new PairRequest { /* ... */ }, headers);
await client.SubscribeAsync(new SubscribeRequest { /* ... */ }, headers);
await client.ListUnpairedAsync(new ListUnpairedRequest(), headers);

// 5) Server-streaming
using var call = client.EventStream(new EventSubscribe { SessionId = sessionId }, headers);
await foreach (var evt in call.ResponseStream.ReadAllAsync())
{
    // handle evt
}
```

---

## 2. RPC 列表与语义

定义见 [Protos/signaling.proto](../Protos/signaling.proto)。

### 2.1 Register
- 输入：`RegisterRequest`（token/role/device_id/robot_generation）
- 输出：`RegisterResponse`（success/message/session_id/client_ip/client_port）

说明：
- `session_id` 是服务端生成的唯一会话标识。
- `client_ip/client_port` 是服务端在 **gRPC 连接层**观测到的远端地址，仅用于展示/日志排障；不能当作 UDP 映射端口使用。
- `robot_generation` 仅机器人侧使用，用于区分机器人代际；当前约定 `1=第一代`、`2=第二代`，VR 传 `0`。
- `role` 当前支持 `ROBOT`、`VR`、`CLIENT`。其中 `CLIENT` 适用于 WPF/网站代理/只订阅不配对的上位客户端。

### 2.2 Ping (Heartbeat)
- 用途：刷新会话心跳、让服务端保持会话在线。
- 服务端清理线程默认阈值是 30 秒（见 `SessionCleanupService`），因此 Ping 间隔建议显著小于 30 秒。

#### 字段格式

- `Heartbeat.client_time`：`int64`，**Unix 时间戳（毫秒，UTC）**
    - 参考：`DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()`
    - 单位是 **毫秒**，不是秒
- `HeartbeatAck.server_time`：`int64`，**Unix 时间戳（毫秒，UTC）**

用途建议：
- 客户端可以用 `server_time - client_time` 粗略估计时钟偏移（仅作诊断，不建议当作严格同步）。

### 2.3 Pair
- 用途：建立/解除配对关系（1:1 默认转发依据）。

### 2.4 Subscribe
- 用途：订阅某个发布者（用于额外转发，例如监管/旁路）。

对于 `CLIENT` 角色：
- 可以只使用 `Register -> Ping -> Subscribe -> EventStream` 这一条最小链路。
- 不要求调用 `Pair`。
- 如果只做控制面订阅/事件接收，可不参与 UDP HELLO。

### 2.5 EventStream
- 用途：服务器向客户端推送事件（配对事件、系统指令等）。

配对成功事件说明：
- 服务端在配对完成后，会向双方都发送 `PairEvent(op=ACCEPT)`。
- 事件中通过 `peer` 统一携带对端信息：`session_id/device_id/role/robot_generation`。
- 建议客户端统一按 `peer.role` 和 `peer.robot_generation` 分支，不再为机器人端和 VR 端拆分两套成功消息格式。

---

## 3. 建议的最小客户端流程

1. `Register` 获取 `SessionId`
2. 对所有后续 gRPC 调用附加 `session-id` metadata
3. 如需参与 UDP 数据面，再发送 UDP `HELLO` 完成 UDP 端点绑定（详见 `PROTOCOL_UDP_SIGNALING.md`）
4. 周期性调用 gRPC `Ping` 保持会话在线
5. 按需调用 `Pair/Subscribe`，并监听 `EventStream`

### 3.1 WPF / CLIENT 角色最小流程

如果客户端只是一个订阅者（例如 WPF 上位机、网站侧本地代理），推荐最小流程：

1. `Register(role=CLIENT)`
2. 持续 `Ping`
3. 建立 `EventStream`
4. 对目标机器人调用 `Subscribe(publisher_session_id=..., sub_video/sub_pose/sub_audio/sub_telemetry_low_rate/sub_telemetry_high_rate)`

这类客户端通常不需要：
- `Pair`
- `GetP2pInfo`
- UDP `HELLO`
