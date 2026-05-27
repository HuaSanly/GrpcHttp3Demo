# UDP 信令与鉴权协议 (UDP Signaling & Authentication)

## 概述

为了解决 UDP 通信中的 NAT 穿透、连接迁移（IP/端口变更）以及身份伪造问题，本协议定义了基于 HMAC-SHA256 的安全握手与心跳机制。

**核心原则**：
1.  **SessionId 不明文传输**: `SessionId` 仅在 gRPC (TLS) 通道中下发，作为共享密钥 (Shared Secret)。
2.  **签名验证**: 所有 UDP 控制包 (HELLO, PING) 必须携带签名。

> 注意：本文档中的 `PING` 指 **UDP 控制包**（用于 NAT 保活/更新 UDP 端点）。
> gRPC 也有一个 `Ping(Heartbeat)` RPC（用于会话心跳/在线判定），两者不是同一个东西。
>
> gRPC 的 `session-id` header/心跳要求请看：`Readme/PROTOCOL_GRPC_SIGNALING.md`。

## 1. 协议格式

所有 UDP 控制消息均为 **UTF-8 文本格式**，字段之间用 `|` 分隔。

### 1.1 握手包 (HELLO)
**用途**: 建立初始 UDP 连接，绑定 IP/端口。
**触发**: 客户端 gRPC 注册成功后立即发送；或长时间未收到 PONG/ACK 时重发。
```
HELLO|SessionId|Timestamp|Signature
```

- **SessionId**: gRPC 注册返回的会话 ID（同时作为签名密钥，不再传 DeviceId）。
- **Timestamp**: 当前 Unix 时间戳 (秒，Int64)。
- **Signature**: 签名字符串 (Hex 格式)。

### 1.2 心跳包 (PING)
**用途**: 维持 NAT 映射，检测连接存活，触发 IP 变更更新。

```text
PING|SessionId|Timestamp|Signature
```

- 与 HELLO 完全一致，只是前缀不同，用于保活与 IP/端口更新。

> 重要约定（端点映射更新）：
> - 服务端仅在收到**签名校验通过**的 `HELLO` / `PING` 时，才会更新 `SessionId -> UDP IPEndPoint` 的映射。
> - 视频/位姿/反馈等**数据包不携带签名/会话信息**，因此**不参与端点映射更新**（否则会有误绑定/伪造风险）。

---
## 2. 签名算法 (HMAC-SHA256)

### 输入参数
- **Key**: `SessionId` (字符串，由 gRPC `RegisterResponse` 下发)
- **Data**: `Type + SessionId + Timestamp` 拼接字符串，其中 Type 为 `HELLO` 或 `PING`

### 计算步骤
1. 拼接明文 `Data = Type + SessionId + Timestamp`（示例：`HELLOsecret-1231700000000`）。
2. `Key = SessionId`，两者均转 UTF-8 字节。
3. 使用 HMAC-SHA256 计算：`hash = HMAC_SHA256(Key, Data)`。
4. 将 `hash` 转为十六进制小写字符串，得到 `Signature`。

### 示例
- **SessionId**: `secret-123`
- **Timestamp**: `1700000000`
- **Data**: `HELLOsecret-1231700000000`

## 3. 服务器响应
### 3.1 握手确认 (ACK)
```text
ACK
```
收到此包表示本次 `HELLO` 鉴权通过，且服务端已将该 `SessionId` 与 `remoteIp:remotePort` 绑定（或更新）。

### 3.2 心跳确认 (PONG)
```text
PONG
```
收到此包表示本次 `PING` 鉴权通过，且服务端仍可看到该 UDP 端点。

### 3.3 数据活性确认 (DACK, 可选)

```text
DACK
```

这是一个**可选**的轻量活性回包：当服务端持续收到视频/位姿/反馈等 UDP 数据包时，服务端可以对发送端做**限频回包**，用于让客户端感知“UDP 仍通畅/仍可达”。

- 建议限频：对同一个 UDP 发送端（同一 `remoteIp:remotePort`）**每 5 秒最多回一次**。
- 语义：仅表示“服务端近期收到了你的数据包”，不表示对端（例如 VR）一定收到了转发后的媒体包。
- 注意：`DACK` **不参与端点映射更新**，端点映射仍只由 `HELLO/PING` 更新。

### 3.4 端点映射过期与救援（服务端行为）

为避免 NAT 超时或网络切换导致服务端保留“陈旧 UDP 端点映射”，服务端会对 **UDP 控制面活跃时间** 做过期判定：

- **判定依据**：只看验签通过的 `HELLO/PING`（数据包不参与映射更新，也不参与控制面活跃刷新）。
- **默认阈值**：如果某会话的 UDP 端点在过去 **15 秒**没有收到有效 `HELLO/PING`，视为映射过期。
- **过期处理**：
    - 清理该会话的 `SessionId -> UDP IPEndPoint` 映射与相关转发表（避免继续向旧端点转发）。
    - 如果该会话的 push 通道可用（WS 或 gRPC streaming），服务端会下发系统指令 `SystemCommand.REQUEST_UDP_HELLO`，提示客户端立即重发 UDP `HELLO` 来重建映射。

对应配置项：
- `MediaServer:UdpControlTimeoutSeconds`（默认 `15`）
- `MediaServer:UdpRescueCooldownSeconds`（默认 `10`，避免频繁催促）
- `MediaServer:UdpMaxRescues`（默认 `3`，单会话最大救援次数）

---

## 4. 客户端行为规范

### 4.1 启动流程
1.  通过 gRPC 调用 `Register`，获取 `SessionId`。
2.  构造 `HELLO` 包并计算签名。
3.  向服务器 UDP 端口发送 `HELLO`。
    - 服务端 UDP 端口由配置 `MediaServer:UdpPort` 指定（默认 `7778`）。
4.  等待 `ACK`。
    - 如果 1秒内未收到，重试发送。
    - 重试 5 次仍失败，报错（可能 UDP 被防火墙拦截）。

### 4.2 运行流程
1.  发送端可以在“有持续媒体/反馈数据发送”时不额外发送 `PING`；`PING` 主要用于空闲期保活与端点迁移检测。
2.  推荐定时器策略（客户端侧）：
    - 若过去 **5秒** 内没有发送任何 UDP 包（视频/位姿/反馈/控制），则发送一次 `PING`。
    - 若一直有 UDP 数据在发送，则可以不发 `PING`（允许同时发，但不强制）。
3.  客户端监听 UDP 回包（`ACK` / `PONG` / `DACK` 以及其它来自服务端的 UDP 数据）。
    - 任意回包都可用于刷新客户端侧“UDP 活跃时间”。
4.  **断线重连/端点迁移处理**：
    - 如果超过 **15秒** 未收到任何服务端 UDP 回包（例如 `ACK/PONG/DACK` 或其它 UDP 数据）。
    - 认为 UDP 通路断开或端点已变化。
    - 立即重发 `HELLO`（使用最新时间戳重新签名）以便服务端更新端点映射。

> 说明：在网络拥塞时，控制包也可能丢失；因此客户端应允许重试与指数退避，并优先保证 `HELLO/PING` 能被发出（例如提高控制包发送优先级）。

### 4.3 时间同步
- 签名包含时间戳，服务器会校验时间偏差（例如 +/- 30秒）。
- 客户端应确保本地时间相对准确（NTP 同步），或者使用 gRPC `Heartbeat` 获取服务器时间来校准。

---

## 5. 协议复用 (Multiplexing)

由于 UDP 端口 (5002) 同时承载信令、视频和位姿数据，接收端通过数据包的 **第一个字节 (Magic Byte)** 进行区分：

| **Video** | `0x01` | `1` | 视频数据，直接转发 |
| **Pose** | `0x02` | `2` | 位姿数据，直接转发 |

1. 读取 Packet[0]。
2. 若为 `'H'` 或 `'P'`，按本协议解析文本并校验签名。
3. 若为 `0x01` 或 `0x02`，且来源 IP 已通过鉴权，则按流媒体逻辑转发。
4. 其他情况丢弃。
