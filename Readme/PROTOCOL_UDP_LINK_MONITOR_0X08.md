# UDP 链路监控协议 `0x08`

本文档描述服务端自产的 UDP 链路监控数据 `0x08` 的订阅方式、HTTP 拓扑摘要接口和 UDP 包结构。

## 1. 作用范围

`0x08` 承载的是服务端视角的 UDP raw link 运行时快照。

边界说明：

- `0x08` 是服务端自产数据。
- `0x08` 不默认推送，只有创建了独立的拓扑监控订阅才会发送。
- `0x08` 使用独立的拓扑监控订阅表和独立的 HTTP 管理接口。
- `0x08` 订阅键是 `topologyId`，不是 raw `linkId`。
- `0x08` 不落数据库；`topologyId` 只保证当前拓扑视图下唯一，拓扑成员变化或服务重启后可能变化。

## 2. 订阅方式

`0x08` 使用独立的 HTTP 管理接口创建订阅，使用 UDP 接收数据。

### 2.1 创建订阅

接口：`POST /api/client/monitor/topology-subscriptions`

兼容别名：`POST /api/client/monitor/link-subscriptions`

鉴权：`Authorization: Bearer <admin_token>`

请求体示例：

```json
{
  "subscriberSessionId": "client-session-id",
  "topologyId": "top_2f0d3f14a14b9cde",
  "intervalMs": 1000
}
```

说明：

- `subscriberSessionId`：订阅者自己的 gRPC `SessionId`。
- `topologyId`：当前订阅目标只关心哪一个拓扑组。
- `intervalMs`：推送间隔，范围 `250..10000`，默认 `1000`。

服务端校验规则：

- bearer token 必须有效。
- `subscriberSessionId` 必须存在。
- 订阅者必须已经有 UDP endpoint。
- `topologyId` 必须提供且不能为空。

### 2.2 查询订阅

接口：`GET /api/client/monitor/topology-subscriptions`

兼容别名：`GET /api/client/monitor/link-subscriptions`

返回的每个 item 会包含：

- `prefix`
- `topic`
- `topologyId`
- `intervalMs`

其中：

- `prefix` 固定为 `0x08`。
- `topic` 固定为 `udp_link_metrics`。
- `topologyId` 表示当前客户端唯一生效的拓扑订阅。

### 2.3 取消订阅

接口：`DELETE /api/client/monitor/topology-subscriptions/{subscriberSessionId}`

兼容别名：`DELETE /api/client/monitor/link-subscriptions/{subscriberSessionId}`

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
- `topics`：固定为 `["udp_link_metrics"]`。
- `linksUpdatedUtc`：链路统计服务最近一次 tick 时间。
- `subscribedTopologyId`：当前订阅目标请求的唯一 `topologyId`。
- `links`：raw link 数组。

> `activeOnly` 字段已废弃，当前所有链路均基于订阅/配对意图创建，不再区分活跃/非活跃状态。

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
  "subscribedTopologyId": "top_2f0d3f14a14b9cde",
  "links": [
    {
      "linkId": "lk_6f7c6c5bf27d45db9b8d9dd6a5f7a0d7",
      "originSessionId": "robot-session-id",
      "sourceNodeId": "robot-session-id",
      "targetNodeId": "system:udp",
      "direction": "ingress",
      "mediaKind": "video",
      "firstSeenUtc": "2026-05-30T07:58:00Z",
      "lastSeenUtc": "2026-05-30T08:00:00Z"
    }
  ]
}
```

## 5. `links` 数组字段定义

`links` 里的每个元素仍然是细粒度 raw link，对象字段如下：

- `linkId`：当前进程生命周期内稳定的随机链路 ID。
- `originSessionId`：这条链路所属的原始流源 session。
- `sourceNodeId`：当前链路段起点；通常是源 session 或 `system:udp`。
- `targetNodeId`：当前链路段终点；通常是目标 session 或 `system:udp`。
- `direction`：`ingress` 或 `egress`。
- `mediaKind`：链路媒体类型。
- `firstSeenUtc`：该 raw `linkId` 首次创建时间。
- `lastSeenUtc`：该链路最近一次计数时间。
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

> `active` 字段已移除。当前链路是否存在只取决于订阅/配对意图，不再单独输出活跃标志。
>
> `queueEnqueued`、`queueDropped`、`retry` 在当前 Direct 发送模式下恒为 0，保留字段仅用于兼容。

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

## 6. HTTP 拓扑摘要接口

如果客户端需要低频拉取当前拓扑摘要，可使用：

- `GET /api/monitor/udp/topologies`

兼容别名：`GET /api/monitor/udp/links`

返回字段：

- `updatedUtc`
- `items`

其中 `items` 的每个对象表示一个拓扑组，字段如下：

- `topologyId`
- `active`
- `firstSeenUtc`
- `lastSeenUtc`
- `nodes`
- `edges`

`nodes` 中的每个元素包含：

- `nodeId`
- `sessionId`
- `deviceId`
- `displayName`
- `nodeKind`

`edges` 中的每个元素包含：

- `sourceNodeId`
- `targetNodeId`
- `active`
- `flows`

其中 `flows` 是该边当前过滤条件下包含的媒体类型数组，例如：

```json
[
  "video",
  "telemetry_low_rate"
]
```

示例：

```json
{
  "updatedUtc": "2026-05-30T08:00:00Z",
  "items": [
    {
      "topologyId": "top_2f0d3f14a14b9cde",
      "active": true,
      "firstSeenUtc": "2026-05-30T07:58:00Z",
      "lastSeenUtc": "2026-05-30T08:00:00Z",
      "nodes": [
        {
          "nodeId": "robot-session-id",
          "sessionId": "robot-session-id",
          "deviceId": "ROBOT-01",
          "displayName": "ROBOT-01",
          "nodeKind": "robot"
        },
        {
          "nodeId": "system:udp",
          "sessionId": null,
          "deviceId": null,
          "displayName": "UDP 转发服务",
          "nodeKind": "udp_service"
        },
        {
          "nodeId": "client-session-id",
          "sessionId": "client-session-id",
          "deviceId": "DESKTOP-EHOTNCN",
          "displayName": "DESKTOP-EHOTNCN",
          "nodeKind": "client"
        }
      ],
      "edges": [
        {
          "sourceNodeId": "robot-session-id",
          "targetNodeId": "system:udp",
          "active": true,
          "flows": ["video", "telemetry_low_rate"]
        },
        {
          "sourceNodeId": "system:udp",
          "targetNodeId": "client-session-id",
          "active": true,
          "flows": ["telemetry_low_rate"]
        }
      ]
    }
  ]
}
```

## 7. 客户端解析建议

- 先调用 `GET /api/monitor/udp/topologies` 获取当前可订阅的 `topologyId`。
- 用户选择目标拓扑后，再调用 `POST /api/client/monitor/topology-subscriptions`，携带对应的 `topologyId`。
- UDP 收到 `0x08` 后，使用 `subscribedTopologyId` 校验订阅目标，并用 `links` 渲染该拓扑组下的 raw link 详情。
- `0x08` 是高频可丢监控数据，不保证可靠重传。

## 8. 兼容性约定

- 后续新增 raw link 字段时，客户端应忽略未识别字段。
- raw `linkId` 当前仅保证单次进程生命周期内稳定；重启服务后可能变化。
- `topologyId` 取决于当前拓扑成员集合；拓扑关系变化时可能变化。
- `active` 字段已移除（链路存在仅取决于订阅/配对意图）。
- `activeOnly` 查询参数和响应字段已废弃。
- `queueEnqueued`、`queueDropped`、`retry` 在当前 Direct 发送模式下恒为 0，保留仅用于兼容历史客户端。
- 当前文档只描述现已上线的 `udp_link_metrics` topic 和 `0x08` 包结构。