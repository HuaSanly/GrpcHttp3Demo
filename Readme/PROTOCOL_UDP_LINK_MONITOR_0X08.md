# UDP 链路监控协议 `0x08`

本文档描述服务端自产的 UDP 链路监控数据 `0x08` 的订阅方式、包格式和字段定义，供客户端做拓扑渲染、压测观察和链路诊断使用。

## 1. 作用范围

`0x08` 承载的是服务端视角的 UDP 链路运行时快照。

边界说明：

- `0x08` 是服务端自产数据。
- `0x08` 不默认推送，只有创建了独立的链路监控订阅才会发送。
- `0x08` 使用独立的链路监控订阅表和独立的 HTTP 管理接口。
- `0x08` 按 `linkId` 订阅，不是默认返回全部链路。
- `0x08` 不落数据库；当前仅保证单次进程生命周期内的运行时稳定 `linkId`。

## 2. 订阅方式

`0x08` 使用独立的 HTTP 管理接口创建订阅，使用 UDP 接收数据。

### 2.1 创建订阅

接口：`POST /api/client/monitor/link-subscriptions`

鉴权：`Authorization: Bearer <admin_token>`

请求体示例：

```json
{
  "subscriberSessionId": "client-session-id",
  "linkId": "lk_6f7c6c5bf27d45db9b8d9dd6a5f7a0d7",
  "intervalMs": 1000
}
```

说明：

- `subscriberSessionId`：订阅者自己的 gRPC `SessionId`。
- `linkId`：当前订阅目标只关心哪一条 `linkId`。
- `intervalMs`：推送间隔，范围 `250..10000`，默认 `1000`。

服务端校验规则：

- bearer token 必须有效。
- `subscriberSessionId` 必须存在。
- 订阅者必须已经有 UDP endpoint。
- `linkId` 必须提供且不能为空。

### 2.2 查询订阅

接口：`GET /api/client/monitor/link-subscriptions`

返回的每个 item 会包含：

- `prefix`
- `topic`
- `linkId`
- `intervalMs`

其中：

- `prefix` 固定为 `0x08`。
- `topic` 固定为 `udp_link_metrics`。
- `linkId` 表示当前客户端唯一生效的订阅链路。

### 2.3 取消订阅

接口：`DELETE /api/client/monitor/link-subscriptions/{subscriberSessionId}`

## 3. UDP 包格式

`0x08` 包格式如下：

```text
[0x08][UTF-8 JSON envelope]
```

也就是：

- 第 1 个字节固定为 `0x08`
- 后续字节是 UTF-8 编码的 JSON 文本

## 4. 顶层 JSON envelope

顶层字段如下：

- `sequence`：针对当前订阅目标和 `0x08` 独立递增的序号。
- `serverTimeUnixMs`：服务端发送时间，Unix 毫秒时间戳。
- `publisherSessionId`：固定为 `system:link-monitor`。
- `targetSessionId`：当前订阅目标的 `SessionId`。
- `prefix`：固定为字符串 `0x08`。
- `topics`：固定为 `[
  "udp_link_metrics"
]`。
- `linksUpdatedUtc`：链路统计服务最近一次 tick 时间。
- `activeOnly`：当前推送是否仅包含活跃链路；当前固定为 `true`。
- `subscribedLinkId`：当前订阅目标请求的唯一 `linkId`。
- `links`：链路数组。

示例：

```json
{
  "sequence": 88,
  "serverTimeUnixMs": 1748580000123,
  "publisherSessionId": "system:link-monitor",
  "targetSessionId": "client-session-id",
  "prefix": "0x08",
  "topics": ["udp_link_metrics"],
  "linksUpdatedUtc": "2026-05-30T08:00:00Z",
  "activeOnly": true,
  "subscribedLinkId": "lk_6f7c6c5bf27d45db9b8d9dd6a5f7a0d7",
  "links": [
    {
      "linkId": "lk_6f7c6c5bf27d45db9b8d9dd6a5f7a0d7",
      "originSessionId": "robot-session-id",
      "sourceNodeId": "robot-session-id",
      "targetNodeId": "system:udp",
      "direction": "ingress",
      "mediaKind": "video",
      "active": true,
      "firstSeenUtc": "2026-05-30T07:58:00Z",
      "lastSeenUtc": "2026-05-30T08:00:00Z",
      "received": {
        "perSecond": { "packets": 30, "bytes": 45000 },
        "totals": { "packets": 1800, "bytes": 2700000 }
      },
      "routeMatched": {
        "perSecond": { "packets": 30, "bytes": 45000 },
        "totals": { "packets": 1790, "bytes": 2685000 }
      },
      "routeMiss": {
        "perSecond": { "packets": 0, "bytes": 0 },
        "totals": { "packets": 10, "bytes": 15000 }
      },
      "forwardPlanned": {
        "perSecond": { "packets": 0, "bytes": 0 },
        "totals": { "packets": 0, "bytes": 0 }
      },
      "queueEnqueued": {
        "perSecond": { "packets": 0, "bytes": 0 },
        "totals": { "packets": 0, "bytes": 0 }
      },
      "queueDropped": {
        "perSecond": { "packets": 0, "bytes": 0 },
        "totals": { "packets": 0, "bytes": 0 }
      },
      "sendAttempt": {
        "perSecond": { "packets": 0, "bytes": 0 },
        "totals": { "packets": 0, "bytes": 0 }
      },
      "sendSuccess": {
        "perSecond": { "packets": 0, "bytes": 0 },
        "totals": { "packets": 0, "bytes": 0 }
      },
      "sendFail": {
        "perSecond": { "packets": 0, "bytes": 0 },
        "totals": { "packets": 0, "bytes": 0 }
      },
      "retry": {
        "perSecond": { "packets": 0, "bytes": 0 },
        "totals": { "packets": 0, "bytes": 0 }
      },
      "failureReasons": {
        "noRoute": { "perSecond": 0, "total": 0 },
        "noTarget": { "perSecond": 0, "total": 0 },
        "queueFull": { "perSecond": 0, "total": 0 },
        "noBufferSpace": { "perSecond": 0, "total": 0 },
        "hostUnreachable": { "perSecond": 0, "total": 0 },
        "networkUnreachable": { "perSecond": 0, "total": 0 },
        "timedOut": { "perSecond": 0, "total": 0 },
        "socketError": { "perSecond": 0, "total": 0 },
        "unknown": { "perSecond": 0, "total": 0 }
      }
    }
  ]
}
```

## 5. `links` 数组字段定义

每个 link 对象字段如下：

- `linkId`：当前进程生命周期内稳定的随机链路 ID。
- `originSessionId`：这条链路所属的原始流源 session。
- `sourceNodeId`：当前链路段起点；通常是源 session 或 `system:udp`。
- `targetNodeId`：当前链路段终点；通常是目标 session 或 `system:udp`。
- `direction`：`ingress` 或 `egress`。
- `mediaKind`：链路媒体类型。
- `active`：当前是否活跃。
- `firstSeenUtc`：该 `linkId` 首次创建时间。
- `lastSeenUtc`：该链路最近一次计数时间。

### 5.1 `direction`

- `ingress`：表示 `源端 -> 服务端` 这一段。
- `egress`：表示 `服务端 -> 目标端` 这一段。

### 5.2 `mediaKind`

当前可能值：

- `video`
- `pose`
- `audio`
- `telemetry_low_rate`
- `telemetry_high_rate`
- `feedback`
- `system_monitor`
- `topology_monitor`

说明：

- `system_monitor`：服务端向订阅者发送 `0x07`。
- `topology_monitor`：服务端向订阅者发送 `0x08`。

### 5.3 流量阶段字段

以下字段结构一致，都是：

- `perSecond.packets`
- `perSecond.bytes`
- `totals.packets`
- `totals.bytes`

字段列表：

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

含义建议：

- `received`：服务端实际收到的数据量。
- `routeMatched`：服务端收到后找到合法转发路由的数据量。
- `routeMiss`：服务端收到后未命中路由或无目标的数据量。
- `forwardPlanned`：服务端决定要转发出去的数据量。
- `queueEnqueued`：成功进入发送队列的数据量。
- `queueDropped`：在发送队列阶段被丢弃的数据量。
- `sendAttempt`：服务端实际发起 UDP 发送尝试的数据量。
- `sendSuccess`：服务端实际发送成功的数据量。
- `sendFail`：服务端发送失败的数据量。
- `retry`：服务端对可重试失败执行重试的数据量。

### 5.4 `failureReasons`

`failureReasons` 下的每个字段结构一致：

- `perSecond`
- `total`

字段列表：

- `noRoute`
- `noTarget`
- `queueFull`
- `noBufferSpace`
- `hostUnreachable`
- `networkUnreachable`
- `timedOut`
- `socketError`
- `unknown`

含义：

- `noRoute`：服务端收到数据，但找不到 source route。
- `noTarget`：命中 source route，但当前链路没有任何目标。
- `queueFull`：发送队列已满导致丢弃。
- `noBufferSpace`：底层 UDP socket 发送时出现 `NoBufferSpaceAvailable`。
- `hostUnreachable`：底层 UDP socket 发送时出现 `HostUnreachable`。
- `networkUnreachable`：底层 UDP socket 发送时出现 `NetworkUnreachable`。
- `timedOut`：底层 UDP socket 发送时出现 `TimedOut`。
- `socketError`：底层 UDP socket 发送时出现通用 socket 错误。
- `unknown`：其它未分类错误。

## 6. 客户端解析建议

- 先判断首字节是否为 `0x08`。
- 再解析后续 UTF-8 JSON 文本。
- `links` 是当前活跃链路快照，不保证覆盖历史失活链路。
- `sequence` 可用于检测 `0x08` 包丢失或乱序。
- `0x08` 是高频可丢监控数据，不保证可靠重传。

## 7. HTTP 补充接口

如果客户端需要低频拉取当前链路列表，可使用：

- `GET /api/monitor/udp/links`

Query：

- `activeOnly=true|false`

返回字段：

- `updatedUtc`
- `activeOnly`
- `items`

其中 `items` 的每个对象字段与 `0x08` 的 `links` 元素一致。

另外，HTTP `GET /api/monitor/udp/links` 为了便于页面展示，会在每个 item 上额外补充：

- `origin`
- `source`
- `target`

这些对象会尽量从当前运行时 session 补出 `sessionId`、`deviceId`、`role`、`nodeKind`，并给 `displayName` 提供默认值：

- 普通会话节点默认使用 `deviceId`
- `system:udp` 默认显示为 `UDP 转发服务`
- `system:monitor` 默认显示为 `系统监控服务`
- `system:link-monitor` 默认显示为 `链路监控服务`

建议客户端流程：

1. 先调用 `GET /api/monitor/udp/links?activeOnly=true` 获取当前可订阅的 `linkId`。
2. 用户选择目标链路后，再调用 `POST /api/client/monitor/link-subscriptions`，携带对应的 `linkId`。
3. UDP 收到 `0x08` 后，只渲染当前订阅的 `linkId` 集合。

## 8. 兼容性约定

- 后续新增链路字段时，客户端应忽略未识别字段。
- `linkId` 当前仅保证单次进程生命周期内稳定；重启服务后可能变化。
- 当前文档只描述现已上线的 `udp_link_metrics` topic 和 `0x08` 包结构。