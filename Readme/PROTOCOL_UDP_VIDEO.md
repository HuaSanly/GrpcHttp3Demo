# 视频流协议文档 - UDP 传输 (Video Stream Protocol - UDP Transport)

## 概述

本文档描述了 RoboticSystemPlatform 中使用 UDP 进行视频数据传输的协议格式。
本协议专为低延迟视频流设计，支持应用层 FEC (XOR 冗余) 和 SCReAM 拥塞控制。

## 传输层

- **协议**: UDP
- **端口**: 默认 5002 (可配置)
- **MTU 策略**: 
  - 目标 Payload 大小: **1200 字节**
  - 协议头大小: **25 字节**
  - 总 UDP Payload 大小: **1225 字节** (远小于以太网 MTU 1500，安全防分片)

## 消息格式

### 整体结构

```
[VideoPacketHeader (25字节)] + [视频数据载荷 (Max 1200字节)]
```

### VideoPacketHeader (25字节)

所有视频数据包（包含原始分片和 FEC 冗余分片）都必须包含此头部。
所有多字节字段均采用 **小端字节序 (Little-Endian)**。

```c
struct VideoPacketHeader {
   uint8_t  Type;              // +0:  消息类型
   uint16_t PacketSeqNum;      // +1:  [修改] 连续包序号 (0-65535 循环)
   uint64_t Timestamp;         // +3:  视频帧时间戳 (毫秒)
   uint32_t FrameId;           // +11: 帧ID
   uint16_t FragmentIndex;     // +15: 当前分片索引 (包含冗余片)
   uint16_t TotalFragments;    // +17: 原始分片总数 (不含冗余片)
   uint16_t PayloadLength;     // +19: 当前包有效载荷长度
   uint32_t FramePayloadLength;// +21: 当前帧总的有效字节数
};  // Total: 25 bytes
```

### 字段详细说明

#### 1. Type (1字节)
- **值**: 固定为 `0x01` (表示视频数据)。
- **用途**: 用于接收端快速区分数据包类型（如区分心跳包、视频包、音频包）。

#### 2. PacketSeqNum (2字节)
- **类型**: `uint16_t` (小端)
- **用途**: 
  - 这是一个**连续递增**的序号，针对每一个 UDP 包（包括原始视频包和 FEC 冗余包）。
  - **SCReAM 算法专用**: 接收端利用此序号检测丢包、乱序，并计算拥塞窗口。
  - **范围**: 0 - 65535，循环使用。
  - **注意**: 这不是帧序号 (FrameId)，也不是分片序号 (FragmentIndex)。

#### 3. Timestamp (8字节)
- **类型**: `uint64_t` (小端)
- **格式**: **NTP 时间戳 (64-bit)**
  - 高 32 位: 秒 (Seconds since 1900-01-01)
  - 低 32 位: 秒的小数部分 (Fractional Seconds)
- **逻辑**: 
  - 记录数据包发送时的绝对时间。
  - 同一帧的所有分片应尽可能使用相同的采集时间或编码时间，但 SCReAM 更关注发送时刻。
  - **SCReAM 算法专用**: 用于计算单向延迟 (OWD) 和排队延迟。

#### 4. FrameId (4字节)
- **逻辑**: 视频帧的唯一标识符，单调递增。
- **用途**: 接收端组帧逻辑的核心依据。

#### 6. FragmentIndex (2字节)
- **范围**: `0` 到 `TotalFragments + RedundancyCount - 1`。
- **逻辑**:
  - `0` 到 `TotalFragments - 1`: 原始视频数据分片。
  - `>= TotalFragments`: FEC 冗余分片 (XOR Parity Packets)。
- **用途**: 接收端用于重组视频帧和进行 FEC 恢复。

#### 7. TotalFragments (2字节)
- **逻辑**: 当前帧的 **原始** 分片数量。
- **注意**: 不包含 FEC 冗余分片数量。
- **示例**: 
  - 原始数据切了 74 片，生成了 2 片冗余。
  - 发送 76 个包。
  - 所有 76 个包的 `TotalFragments` 字段都填 **74**。
  - `FragmentIndex` 分别为 0~73 (原始) 和 74~75 (冗余)。

#### 8. PayloadLength (2字节)
- **范围**: `0 - 1200`。
- **逻辑**: 紧跟在 Header 后面的实际数据长度。
- **用途**: 接收端读取数据边界。

#### 9. FramePayloadLength (4字节)
- **逻辑**: 当前帧所有原始分片的 Payload 总和。
- **用途**: 
  - 接收端预分配内存 buffer。
  - **关键**: 如果最后一个分片丢失，接收端可以通过 `FramePayloadLength` 和其他分片推算出最后一个分片的实际大小，从而正确进行 XOR 恢复。

---

## 组帧与 FEC 逻辑示例

假设一帧视频数据大小为 3000 字节，MTU 限制 Payload 为 1000 字节。
策略：3 个原始分片 + 1 个冗余分片。

### 包 1 (原始)
- `PacketSeqNum`: 10
- `FrameId`: 100
- `FragmentIndex`: 0
- `TotalFragments`: 3
- `Payload`: [Data 0-999]

### 包 2 (原始)
- `PacketSeqNum`: 11
- `FrameId`: 100
- `FragmentIndex`: 1
- `TotalFragments`: 3
- `Payload`: [Data 1000-1999]

### 包 3 (原始)
- `PacketSeqNum`: 12
- `FrameId`: 100
- `FragmentIndex`: 2
- `TotalFragments`: 3
- `Payload`: [Data 2000-2999]

### 包 4 (FEC 冗余)
- `PacketSeqNum`: 13  <-- 注意：SeqNum 继续递增
- `FrameId`: 100
- `FragmentIndex`: 3  <-- 注意：Index >= TotalFragments
- `TotalFragments`: 3 <-- 保持为 3
- `Payload`: [XOR(Data0, Data1, Data2)]

---

## SCReAM 适配说明

接收端在处理 `PacketSeqNum` 时，应处理 8 位回绕：
- 收到 `255` 后，下一个期望值是 `0`。
- 丢包检测逻辑应基于 `PacketSeqNum`，而不是 `FragmentIndex`。
