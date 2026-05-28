# UDP 机器人遥测协议 V1

本文档描述 teleop_robot_bridge 当前约定的机器人遥测 UDP 协议 V1。

这版协议只保留一个极简的 `0x05` 低频状态包，避免把本来很简单的遥测做成通用 TLV 容器。当前目标只有两个：

1. 先稳定传出最必要的运行状态。
2. 给后续补真实数据源留空间，但不提前设计过多字段。

当前约束如下：

1. `0x05` 为低频状态快照，本文档已定稿。
2. `0x06` 只保留 Type 占位，本版不冻结业务体。
3. `0x05` 与 `0x06` 复用同一 UDP 端口，通过首字节 `Type` 区分。
4. 所有多字节字段均使用 Little-Endian。
5. V1 统一按单个 UDP Datagram 发送，不做视频协议那种分片。

## 1. 总览

### 1.1 Type 定义

| Type | 名称 | 状态 | 说明 |
|---|---|---|---|
| `0x05` | Robot Telemetry Low Rate | 已冻结 | 低频状态快照 |
| `0x06` | Robot Telemetry High Rate | 预留 | 后续高频通道占位 |

### 1.2 发送原则

1. `0x05` 固定为低频状态快照，建议默认 `1 Hz` 周期发送。
2. `0x05` 不区分 `full`、`delta`、`heartbeat` 等模式，每包都表示“当前这一秒边端能给出的状态快照”。
3. 单包大小建议不超过当前默认 `1200 bytes` 的 UDP payload 预算。
4. 未来若还需要更多低频字段，应优先评估是否真的必要；若确实要扩展，优先升 `Version`，不要把 `0x05` 再做回多层 block 协议。

### 1.3 缺失值编码约定

1. 无符号整数字段未知时，填该字段全 `1`。
2. 有符号整数字段未知时，填该类型最小值。
3. 字符串未知或暂不提供时，长度为 `0`。

## 2. 通用外层头

`0x05` 与 `0x06` 使用同一套轻量外层头。

```c
struct TelemetryDatagramHeaderV1 {
    uint8_t  Type;           // 0x05 或 0x06
    uint16_t Seq;            // 小端, 16 位包序号, 发送端自增
    uint64_t TxTimestampMs;  // 小端, 发送时刻 Unix 毫秒时间戳
    uint8_t  SessionIdBytes; // UTF-8 字节长度
    uint8_t  DeviceIdBytes;  // UTF-8 字节长度
    uint8_t  IdentifierTable[]; // 顺序: SessionId, DeviceId
};
```

### 2.1 字段说明

| 字段 | 长度 | 说明 |
|---|---:|---|
| `Type` | 1 | 包类型标识 |
| `Seq` | 2 | 16 位循环递增序号, 允许回绕 |
| `TxTimestampMs` | 8 | 边端发包时刻, Unix 毫秒 |
| `SessionIdBytes` | 1 | `SessionId` 的 UTF-8 字节长度 |
| `DeviceIdBytes` | 1 | `DeviceId` 的 UTF-8 字节长度 |
| `IdentifierTable` | 可变 | 先写 `SessionId`, 再写 `DeviceId` |

### 2.2 外层头规则

1. 一份 UDP Datagram 只承载一个逻辑包。
2. `Seq` 仅用于观测丢包、乱序、重放，不用于强一致控制。
3. `SessionId` 与 `DeviceId` 属于外层头的一部分，接收端应在解析 payload 前先读取它们，用于机器人识别与会话归属。
4. 对 `0x05` 而言，`TxTimestampMs` 就表示这一秒状态快照的时间戳，不再在 payload 内重复放时间戳。

### 2.3 标识字段约定

1. `SessionId` 为当前 gRPC 注册成功后得到的 session id；若当前运行模式下没有 session，可发送空字符串。
2. `DeviceId` 为机器人静态设备标识，当前实现中来自 `grpc.device_id` 参数。
3. 两个字符串都使用 UTF-8 编码，单独长度都不应超过 `255` 字节；超过时发送端应截断。
4. 接收端应按 `SessionIdBytes` 先截取 `SessionId`，再按 `DeviceIdBytes` 截取 `DeviceId`。

## 3. Type `0x05`: 低频状态快照

## 3.1 Payload 结构

```c
struct RobotTelemetryLowRatePayloadV1 {
    uint8_t  Version;                  // 固定 1
    uint16_t BatteryVoltageMv;         // 小端, 毫伏, 未知填 0xFFFF
    int16_t  BatteryCurrentMa;         // 小端, 毫安, 放电为负, 未知填 INT16_MIN
    int16_t  BatteryTemperatureDeciC;  // 小端, 0.1 摄氏度, 未知填 INT16_MIN
    uint8_t  FaultCodeBytes;           // UTF-8 字节长度
    uint8_t  NetworkQualityBytes;      // UTF-8 字节长度
    uint8_t  ModelBytes;               // UTF-8 字节长度
    uint8_t  FirmwareVersionBytes;     // UTF-8 字节长度
    uint8_t  TextTable[];              // 顺序: FaultCode, NetworkQuality,
                                       //      Model, FirmwareVersion
};
```

该结构固定只承载 7 类信息：

1. 电池电压
2. 电池电流
3. 当前温度
4. 故障码
5. 网络质量
6. 型号
7. 固件版本

除此之外，V1 不再额外携带设备信息、能力宣告、schema、状态位图或调试块。

## 3.2 字段说明

| 字段 | 长度 | 说明 |
|---|---:|---|
| `Version` | 1 | 固定为 `1` |
| `BatteryVoltageMv` | 2 | 电池电压, 单位 mV |
| `BatteryCurrentMa` | 2 | 电池电流, 单位 mA, 放电为负 |
| `BatteryTemperatureDeciC` | 2 | 当前温度, 单位 0.1 摄氏度 |
| `FaultCodeBytes` | 1 | `FaultCode` 的 UTF-8 字节长度 |
| `NetworkQualityBytes` | 1 | `NetworkQuality` 的 UTF-8 字节长度 |
| `ModelBytes` | 1 | `Model` 的 UTF-8 字节长度 |
| `FirmwareVersionBytes` | 1 | `FirmwareVersion` 的 UTF-8 字节长度 |
| `TextTable` | 可变 | 依次写 `FaultCode`、`NetworkQuality`、`Model`、`FirmwareVersion` |

## 3.3 文本字段约定

### 3.3.1 `FaultCode`

1. `FaultCode` 为 UTF-8 字符串。
2. 推荐直接发送机器人当前对外暴露的故障码文本，例如 `E_STOP`, `MOTOR_OVER_TEMP`, `BATTERY_LOW`。
3. 若当前无故障，允许发送空字符串。
4. 若未来同时需要“故障等级”“告警码数组”等更复杂信息，应升协议版本，而不是继续往 V1 里叠字段。

### 3.3.2 `NetworkQuality`

1. `NetworkQuality` 为 UTF-8 字符串。
2. 本版只冻结“有这样一个字段”和“它是字符串”这两件事。
3. 本版**不定义**它的具体枚举、评分区间、计算方法或来源话题。
4. 在网络质量口径尚未达成一致前，发送端应发送空字符串。
5. 后端当前只需原样存储或打印，不应对该字段做业务判断。

### 3.3.3 `Model`

1. `Model` 为 UTF-8 字符串。
2. 该字段用于承载机器人型号，内容由边端自定义。
3. 当前实现约定直接从参数文件读取，而不是从运行时 topic 推导。
4. 若当前未配置，允许发送空字符串。

### 3.3.4 `FirmwareVersion`

1. `FirmwareVersion` 为 UTF-8 字符串。
2. 该字段用于承载设备当前固件版本文本，例如 `v1.2.3`、`2026.05.28-rc1`。
3. 该字段未来预计来自单独的话题或运行时状态源；本版先冻结字段，不冻结来源。
4. 在实际来源尚未接入前，允许发送空字符串。

## 3.4 `TextTable` 编码规则

1. `TextTable` 中不带分隔符。
2. 接收端应按 `FaultCodeBytes`、`NetworkQualityBytes`、`ModelBytes`、`FirmwareVersionBytes` 的顺序依次截取文本字段。
3. 若 `TextTable` 的剩余长度不足，整包视为损坏并丢弃。
4. 四个字符串单独都不应超过 `255` 字节；超过时发送端应截断。

## 4. `0x05` 发送约定

### 4.1 基本规则

1. 边端默认每 `1` 秒发送一包 `0x05`。
2. 每包都视为“当前可用状态的全量快照”。
3. 即使部分字段当前未知，也应继续发包，只需按缺失值约定填充。
4. 建议边端在链路建立后立即先发一包 `0x05`，之后再进入 `1 Hz` 周期发送。

### 4.2 当前最小实现

如果当前只想尽快打通一条稳定链路，边端最小建议先保证这 5 个字段有值：

1. `BatteryVoltageMv`
2. `BatteryCurrentMa`
3. `BatteryTemperatureDeciC`
4. `FaultCode`
5. `Model`

`NetworkQuality` 与 `FirmwareVersion` 在来源或口径未定前允许一直为空字符串。

## 5. Type `0x06`: 高频通道占位

`0x06` 当前只保留 Type 占位，本版不冻结 payload 结构。

当前约束只有两条：

1. 外层头仍然使用 [doc/PROTOCOL_UDP_ROBOT_TELEMETRY_V1.md](doc/PROTOCOL_UDP_ROBOT_TELEMETRY_V1.md) 第 2 节定义的公共头。
2. 后端不得假设 `0x06` 一定存在，也不得假设其业务体已经定稿。

## 6. 后端解析建议

### 6.1 基本解析流程

1. 先检查包长是否至少包含 `13` 字节固定外层头。
2. 根据首字节 `Type` 区分 `0x05` 与 `0x06`。
3. 先按 `SessionIdBytes` 与 `DeviceIdBytes` 解析出 `SessionId`、`DeviceId`。
4. 对 `0x05`：检查 `Version == 1`，再按固定结构解析数值字段与字符串字段。
5. 对 `0x06`：当前可直接记录类型后跳过，不做业务解析。

### 6.2 错误处理原则

1. 包长不足时，直接丢包。
2. `Version` 不支持时，直接丢弃该包。
3. 任意 header 字符串长度或 payload 字符串长度导致越界时，整包视为损坏并丢弃。

### 6.3 存储建议

1. 后端建议按 `DeviceId`、`SessionId`、`Type`、`Seq`、`TxTimestampMs` 建基础索引。
2. 对 `FaultCode`、`NetworkQuality`、`Model` 与 `FirmwareVersion`，当前建议先按原始文本落库。
3. `NetworkQuality` 在口径冻结前，不建议做聚合分桶或告警阈值判断。

## 7. 与当前实现的关系

当前仓库实现已经按本文档收敛为一个极简 `0x05` 结构。

当前阶段的工程策略是：

1. 先把协议收敛成稳定、够用、易解析的最小集。
2. 先打通电压、电流、温度、故障码、型号这条主路径。
3. 固件版本和网络质量等存在来源或口径争议的字段，先留字符串位，后续再讨论其来源、定义和计算方式。