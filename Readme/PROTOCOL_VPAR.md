# VPAR 协议规范

## 协议概述

**协议名称**：VPAR  
**协议版本**：1.0  
**Magic String**：`VPAR` (ASCII: 0x56 0x50 0x41 0x52)  
**用途**：视频参数配置（分辨率、帧率、编码参数集）  
**QCT0 分类**：
  - VPAR消息：Video (Category=1)
  - VPAR_ACK响应：Control (Category=3)
**触发时机**：机器人收到START指令后立即发送

---

## 消息格式

### 外层：QCT0 封装

```
偏移   长度   字段名            类型        说明
0      4      Magic             bytes       固定 ASCII 'Q' 'C' 'T' '0'
4      1      Version           u8          固定 0
5      1      Flags             u8          ControlKind: 1=Info
6      2      Reserved          u16         固定 0
8      8      TimestampMs       u64         Unix 毫秒时间戳 (Little-Endian)
16     2      Code              u16         AppErrorCode (Little-Endian)
18     1      Category          u8          AppCategory: 1=Video
19     2      MsgLen            u16         VPAR 内层负载长度 (Little-Endian)
21     MsgLen Msg               bytes       VPAR 内层负载
```

### 内层：VPAR 负载

```
偏移   长度     字段名           类型    说明
0      4        Magic            bytes   固定 ASCII 'V' 'P' 'A' 'R'
4      1        Version          u8      协议版本，当前为 1
5      1        CodecType        u8      编码类型 (H264=1, H265=2)
6      2        Reserved         u16     保留字段 (Little-Endian)
8      2        Width            u16     视频宽度 (Little-Endian)
10     2        Height           u16     视频高度 (Little-Endian)
12     2        Fps              u16     帧率 (Little-Endian)
14     2        Reserved         u16     保留字段 (Little-Endian)
16     2        SpsLen           u16     SPS 长度 (Little-Endian)
18     2        PpsLen           u16     PPS 长度 (Little-Endian)
20     2        VpsLen           u16     VPS 长度 (Little-Endian, H265专用)
22     2        Reserved         u16     保留字段 (Little-Endian)
24     SpsLen   Sps              bytes   SPS NAL 数据（不含起始码）
24+S   PpsLen   Pps              bytes   PPS NAL 数据（不含起始码）
24+S+P VpsLen   Vps              bytes   VPS NAL 数据（不含起始码，H265）
```

**总长度**：24 + SpsLen + PpsLen + VpsLen

---

## 字段说明

### CodecType 枚举

| 值 | 名称 | 说明 |
|---|------|------|
| 0 | Unknown | 未知编码 |
| 1 | H264 | H.264/AVC 编码 |
| 2 | H265 | H.265/HEVC 编码 |

### Width / Height

- **类型**：u16 (Little-Endian)
- **单位**：像素
- **示例**：1920×1080, 1280×720

### Fps

- **类型**：u16 (Little-Endian)
- **单位**：帧/秒
- **示例**：30, 60

### SPS / PPS / VPS

- **SPS (Sequence Parameter Set)**：H.264/H.265 序列参数集
- **PPS (Picture Parameter Set)**：H.264/H.265 图像参数集
- **VPS (Video Parameter Set)**：H.265 专用视频参数集（H.264无此字段，VpsLen=0）
- **格式**：纯 NAL 数据，**不包含起始码**（0x00 0x00 0x00 0x01）
- **用途**：初始化解码器

---

## VPAR_ACK 响应

**纯文本消息**，封装在 QCT0 中：

```
偏移   长度   字段名            类型        说明
0      4      Magic             bytes       固定 ASCII 'Q' 'C' 'T' '0'
4      1      Version           u8          固定 0
5      1      Flags             u8          ControlKind.Info (1)
6      2      Reserved          u16         固定 0
8      8      TimestampMs       u64         Unix 毫秒时间戳 (Little-Endian)
16     2      Code              u16         AppErrorCode.Ok (0)
18     1      Category          u8          AppCategory.Control (3)
19     2      MsgLen            u16         8 (固定)
21     8      Msg               bytes       ASCII "VPAR_ACK" (UTF-8)
```

**关键字段**：
- **Category**：`Control (3)` - **注意不是 Video (1)**
- **Msg**：**纯文本** `"VPAR_ACK"` (8字节UTF-8)

**总长度**：29 字节

---

## 消息流程

```
Robot                                 Server                           VR Clients
  │                                      │                                  │
  │  1. 发送 VPAR 消息                   │                                  │
  │     (1920x1080, 30fps, H264, SPS/PPS)│                                  │
  │ ──────────────────────────────────> │                                  │
  │                                      │                                  │
  │                                      │  2. 服务器处理                   │
  │                                      │     - 验证参数 (Width/Height/Fps)│
  │                                      │     - 持久化到 PublisherInfo     │
  │                                      │     - 查询订阅者 (by ConnId)     │
  │                                      │     - 查询配对设备               │
  │                                      │                                  │
  │                                      │  3. 转发到所有目标               │
  │                                      │ ──────────────────────────────> │
  │                                      │     (原样转发 VPAR)              │
  │                                      │                                  │
  │  4. 接收 VPAR_ACK 响应               │                                  │
  │     Category=Control (3)             │                                  │
  │     Msg="VPAR_ACK" (纯文本)          │                                  │
  │ <────────────────────────────────── │                                  │
  │                                      │                                  │
```

---

## 服务器处理流程

```
1. 解析 VPAR 消息
   ↓
2. 验证参数
   - Width != 0
   - Height != 0
   - Fps != 0
   ↓
3. 持久化视频参数到 SubscriptionManager.PublisherInfo
   - UpdatePublisherVideo(publisherDeviceId)
   - 存储 Width, Height, Fps, CodecType, Sps, Pps, Vps
   ↓
4. 获取转发目标 (使用连接ID)
   - 所有订阅者: GetSubscribers(publisherDeviceId) → [subscriberConnId, ...]
   - 配对设备: 通过 PairedWithDeviceId 获取配对设备的 ConnectionId
   - HashSet 去重
   ↓
5. 并行转发到所有目标
   - 原样转发完整 VPAR 消息（QCT0封装）
   - Task.WhenAll() 异步发送
   ↓
6. 返回 "VPAR_ACK" (纯文本, Category=Control)
```

---

## 转发机制

### 转发目标计算

```csharp
var targetConnIds = new HashSet<string>();

// 1. 添加所有订阅者（返回连接ID列表）
var subscribers = subscriptionManager.GetSubscribers(publisherDeviceId);
foreach (var subscriberConnId in subscribers)
{
    targetConnIds.Add(subscriberConnId);
}

// 2. 添加配对设备的连接ID
if (sessions.TryGetByDevice(publisherDeviceId, out var publisherSession) && 
    publisherSession.IsPaired)
{
    if (sessions.TryGetByDevice(publisherSession.PairedWithDeviceId, out var pairedSession))
    {
        targetConnIds.Add(pairedSession.ConnectionId);  // HashSet 自动去重
    }
}

// 3. 并行转发（跳过发送方自己）
foreach (var targetConnId in targetConnIds)
{
    if (targetConnId == context.ConnectionId) continue;
    
    _ = Task.Run(async () => {
        var stream = await openOutboundByConnId(targetConnId, cancellationToken);
        await stream.WriteAsync(vparQct0Data);
    });
}
```

### 关键点

- **查询用设备ID**：`GetSubscribers(publisherDeviceId)`
- **返回是连接ID**：`["conn_vr_456", "conn_vr_789"]`
- **转发用连接ID**：`openOutboundByConnId(subscriberConnId)`

---

## 错误处理

### 参数无效

**场景**：Width/Height/Fps 为 0

**响应**：
```
Code: InvalidArgument
Category: Control
Msg: "Invalid video dimensions" / "Invalid FPS"
```

### 设备未认证

**场景**：发送VPAR但未通过HELLO认证

**响应**：
```
Code: Unauthorized
Category: Control
Msg: "Device not authorized"
```

---

## 与其他协议的关系

| 协议 | 作用 | VPAR 的角色 |
|------|------|------------|
| START | 启动视频发送 | START后触发VPAR发送 |
| **VPAR** | **视频参数配置** | **提供解码器初始化参数** |
| QVR0 | 关键帧传输 | 使用VPAR的参数解码 |
| Datagram | 非关键帧传输 | 使用VPAR的参数解码 |
| SUB1 | 订阅管理 | 订阅后才能收到VPAR |

**典型流程**：
```
1. VR → Server: SUB1 (订阅 robot_001)
   ↓
2. VR → Server: PAR1 (配对 robot_001)
   ↓
3. Server → Robot: START (Video)
   ↓
4. Robot → Server: VPAR (1920x1080, 30fps, H264, SPS/PPS)
   ↓
5. Server → VR: 转发 VPAR
   ↓
6. Server → Robot: VPAR_ACK
   ↓
7. VR 初始化解码器
   ↓
8. Robot → Server: QVR0 / Datagram (视频流)
   ↓
9. Server → VR: 转发视频流
   ↓
10. VR 使用 VPAR 参数解码视频
```

---

## 文档版本

- **版本**：1.0
- **日期**：2025-11-05
- **状态**：已实现
- **更新**：匹配实际代码实现，明确订阅机制和VPAR_ACK格式
