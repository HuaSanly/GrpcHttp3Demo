# UDP 系统监控协议 `0x07`

本文档描述服务端自产的 UDP 系统监控数据 `0x07` 的订阅方式、包格式、topic 列表和字段定义，供客户端对接使用。

说明：`0x07` 和 `0x08` 现已拆分为两套独立订阅入口。本文档只描述 `0x07`；`0x08` 详见 `PROTOCOL_UDP_LINK_MONITOR_0X08.md`。

## 1. 作用范围

`0x07` 只承载后端系统监控信息，不承载业务媒体或业务遥测数据。

边界说明：

- `0x07` 是服务端自产数据。
- 机器人业务遥测不走 `0x07`。
- `0x05` 是机器人低频状态快照。
- `0x06` 是机器人高频遥测预留通道。
- 如果客户端需要机器人遥测，应通过媒体订阅接口订阅 `0x05` / `0x06`，而不是订阅 `0x07`。

## 2. 订阅方式

`0x07` 使用 HTTP 管理接口创建订阅，使用 UDP 接收数据。

### 2.1 创建订阅

接口：`POST /api/client/monitor/subscriptions`

鉴权：`Authorization: Bearer <admin_token>`

请求体示例：

```json
{
  "subscriberSessionId": "client-session-id",
  "topics": ["udp_global", "signaling_rates", "online_summary", "runtime_tables"],
  "intervalMs": 1000
}
```

说明：

- `subscriberSessionId`：订阅者自己的 gRPC `SessionId`。
- `topics`：订阅的系统监控 topic 列表。
- `intervalMs`：推送间隔，范围 `250..10000`，默认 `1000`。

服务端校验规则：

- bearer token 必须有效。
- `subscriberSessionId` 必须存在。
- 订阅者必须已经有 UDP endpoint。

### 2.2 查询订阅

接口：`GET /api/client/monitor/subscriptions`

### 2.3 取消订阅

接口：`DELETE /api/client/monitor/subscriptions/{subscriberSessionId}`

## 3. UDP 包格式

`0x07` 包格式如下：

```text
[0x07][UTF-8 JSON envelope]
```

也就是：

- 第 1 个字节固定为 `0x07`
- 后续字节是 UTF-8 编码的 JSON 文本

## 4. 顶层 JSON envelope

顶层字段如下：

- `sequence`：针对当前订阅目标单独递增的序号。
- `serverTimeUnixMs`：服务端发送时间，Unix 毫秒时间戳。
- `publisherSessionId`：固定为 `system:monitor`。
- `targetSessionId`：当前订阅目标的 `SessionId`。
- `prefix`：固定为字符串 `0x07`。
- `topics`：当前包实际携带的 topic 名称数组。
- `udp`：订阅 `udp_global` 时出现。
- `signaling`：订阅 `signaling_rates` 时出现。
- `online`：订阅 `online_summary` 时出现。
- `runtimeTables`：订阅 `runtime_tables` 时出现。

示例：

```json
{
  "sequence": 1024,
  "serverTimeUnixMs": 1748486400123,
  "publisherSessionId": "system:monitor",
  "targetSessionId": "f33d7d35-0f37-4d35-8b60-6a6d04d0f199",
  "prefix": "0x07",
  "topics": ["udp_global", "online_summary"],
  "udp": {
    "updatedUtc": "2026-05-29T08:00:00Z",
    "rx": {
      "pps": 1200,
      "bps": 524288,
      "perSecond": {
        "control": 10,
        "video": 800,
        "pose": 120,
        "audio": 200,
        "feedback": 50,
        "telemetryLowRate": 12,
        "telemetryHighRate": 4,
        "system": 20,
        "unknown": 0
      },
      "totals": {
        "packets": 123456,
        "bytes": 987654321,
        "control": 300,
        "video": 80000,
        "pose": 12000,
        "audio": 20000,
        "feedback": 8000,
        "telemetryLowRate": 320,
        "telemetryHighRate": 64,
        "system": 1156,
        "unknown": 0
      }
    }
  },
  "online": {
    "timeoutSeconds": 8,
    "registered": 6,
    "online": 5,
    "onlineByRole": {
      "robot": 2,
      "vr": 1,
      "client": 2,
      "unknown": 0
    },
    "pushConnectedOnline": 5
  }
}
```

## 5. Topic 列表

当前支持的 topic：

- `udp_global`
- `signaling_rates`
- `online_summary`
- `runtime_tables`

`topics` 字段表示当前包实际包含了哪些对象。客户端应以 `topics` 为准做解析，不要假设所有对象始终同时存在。

## 6. `udp_global` 字段定义

`udp_global` 表示 UDP 全局收发、发送结果、重试和转发队列丢弃统计。

顶层字段：

- `updatedUtc`
- `rx`
- `tx`
- `txOk`
- `txFail`
- `txRetry`
- `forwarding`

### 6.1 `rx`

- `pps`：最近一秒接收包数。
- `bps`：最近一秒接收字节数。
- `perSecond.control`
- `perSecond.video`
- `perSecond.pose`
- `perSecond.audio`
- `perSecond.feedback`
- `perSecond.telemetryLowRate`
- `perSecond.telemetryHighRate`
- `perSecond.system`
- `perSecond.unknown`
- `totals.packets`
- `totals.bytes`
- `totals.control`
- `totals.video`
- `totals.pose`
- `totals.audio`
- `totals.feedback`
- `totals.telemetryLowRate`
- `totals.telemetryHighRate`
- `totals.system`
- `totals.unknown`

### 6.2 `tx`

结构与 `rx` 对应，表示“尝试发送”的统计：

- `pps`
- `bps`
- `perSecond.control`
- `perSecond.video`
- `perSecond.pose`
- `perSecond.audio`
- `perSecond.feedback`
- `perSecond.telemetryLowRate`
- `perSecond.telemetryHighRate`
- `perSecond.system`
- `perSecond.unknown`
- `totals.packets`
- `totals.bytes`
- `totals.control`
- `totals.video`
- `totals.pose`
- `totals.audio`
- `totals.feedback`
- `totals.telemetryLowRate`
- `totals.telemetryHighRate`
- `totals.system`
- `totals.unknown`

### 6.3 `txOk`

结构与 `tx` 对应，表示“实际发送成功”的统计。

### 6.4 `txFail`

结构与 `tx` 基本对应，额外包含 `noBuffer`：

- `pps`
- `bps`
- `perSecond.control`
- `perSecond.video`
- `perSecond.pose`
- `perSecond.audio`
- `perSecond.feedback`
- `perSecond.telemetryLowRate`
- `perSecond.telemetryHighRate`
- `perSecond.system`
- `perSecond.unknown`
- `perSecond.noBuffer`
- `totals.packets`
- `totals.bytes`
- `totals.control`
- `totals.video`
- `totals.pose`
- `totals.audio`
- `totals.feedback`
- `totals.telemetryLowRate`
- `totals.telemetryHighRate`
- `totals.system`
- `totals.unknown`
- `totals.noBuffer`

### 6.5 `txRetry`

- `pps`
- `totals.count`

### 6.6 `forwarding.queueDrop`

表示有界转发队列满导致的丢弃统计：

- `pps`
- `bps`
- `perSecond.video`
- `perSecond.pose`
- `perSecond.audio`
- `perSecond.feedback`
- `perSecond.telemetryLowRate`
- `perSecond.telemetryHighRate`
- `perSecond.system`
- `perSecond.unknown`
- `totals.packets`
- `totals.bytes`
- `totals.video`
- `totals.pose`
- `totals.audio`
- `totals.feedback`
- `totals.telemetryLowRate`
- `totals.telemetryHighRate`
- `totals.system`
- `totals.unknown`

## 7. `signaling_rates` 字段定义

`signaling_rates` 表示 gRPC / WebSocket 信令流量速率和累计值。

顶层字段：

- `updatedUtc`
- `inboundPps`
- `outboundPps`
- `totals`

### 7.1 `inboundPps`

- `register`
- `ping`
- `pair`
- `subscribe`
- `listUnpaired`
- `eventStream`
- `wsBadProto`
- `wsUnsupported`
- `wsNoSession`

### 7.2 `outboundPps`

- `registerResponse`
- `pingAck`
- `pairResponse`
- `subscribeResponse`
- `listUnpairedResponse`
- `wsError`
- `events`
- `pairEvents`
- `systemCommands`

### 7.3 `totals`

- `totals.inbound.register`
- `totals.inbound.ping`
- `totals.inbound.pair`
- `totals.inbound.subscribe`
- `totals.inbound.listUnpaired`
- `totals.inbound.eventStream`
- `totals.inbound.wsBadProto`
- `totals.inbound.wsUnsupported`
- `totals.inbound.wsNoSession`
- `totals.outbound.registerResponse`
- `totals.outbound.pingAck`
- `totals.outbound.pairResponse`
- `totals.outbound.subscribeResponse`
- `totals.outbound.listUnpairedResponse`
- `totals.outbound.wsError`
- `totals.outbound.events`
- `totals.outbound.pairEvents`
- `totals.outbound.systemCommands`

## 8. `online_summary` 字段定义

`online_summary` 表示服务端当前在线视角统计。

- `timeoutSeconds`：当前在线判定超时时间。
- `registered`：当前已注册 session 总数。
- `online`：当前在线 session 总数。
- `onlineByRole.robot`
- `onlineByRole.vr`
- `onlineByRole.client`
- `onlineByRole.unknown`
- `pushConnectedOnline`：在线 session 中 push 通道已连接数量。

## 9. `runtime_tables` 字段定义

`runtime_tables` 表示服务端内存运行时表数量快照。

- `sessions`
- `endpointIndex`
- `sessionEndpointIndex`
- `pairings`
- `publishersWithSubscriptionDetails`
- `publishersWithSubscriptions`
- `identityIndex`
- `forwardingTable`
- `poseForwardingTable`
- `audioForwardingTable`
- `telemetryLowRateForwardingTable`
- `telemetryHighRateForwardingTable`
- `sourceRouteTable`
- `systemMonitorTargets`
- `feedbackRoute`
- `p2pSharedKeys`

这些字段主要用于观察服务端内部运行时结构规模，不应直接当作业务语义字段使用。

## 10. 客户端解析建议

- 先判断首字节是否为 `0x07`。
- 再解析后续 UTF-8 JSON 文本。
- 以 `topics` 数组为准判断当前包包含哪些对象。
- 不要假设所有 topic 每次都同时存在。
- `sequence` 可用于检测丢包或乱序。
- `0x07` 是高频可丢监控数据，不保证可靠重传。

## 11. 兼容性约定

- 后续新增 topic 时，服务端会在 `topics` 中显式声明。
- 客户端应忽略未识别字段和未识别 topic，避免因为协议扩展导致解析失败。
- 当前文档只描述现已上线的 `udp_global`、`signaling_rates`、`online_summary`、`runtime_tables`。