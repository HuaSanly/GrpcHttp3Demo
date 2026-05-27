# 位姿数据传输协议 (Pose Datagram Protocol)

## 概述

本文档描述了RoboticSystemPlatform中VR设备位姿数据的传输协议格式。该协议通过QUIC Datagram进行实时位姿和输入状态的传输。

## 协议标识

- **Type字段**: `0x02` (位姿数据标识)
- **协议版本**: 1
- **传输方式**: QUIC Datagram (无序、不可靠、低延迟)
- **ResourceMask**: `0x02` (Pose资源类型)

---

## 数据包格式

### 统一数据包头部 (Universal Datagram Header)

所有位姿数据包都以统一的头部开始，与视频数据包格式保持一致：

```c
struct PoseDataPacket {
    uint8_t  Type;              // +0: 固定 0x02 (表示位姿数据)
    uint64_t Timestamp;         // +1: 时间戳，毫秒 (8字节，小端)
    uint8_t  Version;           // +9: 协议版本 (当前为 1)
    uint8_t  Flags;             // +10: 特性标志位 (见下表)
    uint8_t  Reserved[2];       // +11: 保留字段 (填充 0)
};  // Total: 13 bytes
```

#### 字段说明

- **Type (1字节)**: 数据类型标识符，位姿数据固定为 `0x02`
- **Timestamp (8字节)**: Unix 时间戳，以毫秒为单位，小端字节序
- **Version (1字节)**: 协议版本号，当前固定为 `1`
- **Flags (1字节)**: 特性标志位，指示数据包包含的可选内容
- **Reserved (2字节)**: 保留字段，当前填充为 `0`，用于未来扩展

#### Flags 位定义

| Bit | Mask | Name              | Description                        |
|-----|------|-------------------|------------------------------------|
| 0   | 0x01 | INCLUDE_EULER     | 是否包含欧拉角数据 (调试用)         |
| 1   | 0x02 | INCLUDE_AIM       | 是否包含手部瞄准数据 (手势识别)     |
| 2   | 0x04 | INCLUDE_BUTTONS   | 是否包含手柄按键数据               |
| 3-7 | -    | -                 | 保留                               |

---

## Payload 数据结构

### 1. 位姿数据 (Pose Data) - 必需

#### 标准位姿 (28 bytes 每个设备)
发送顺序: **HMD → Left Controller → Right Controller**

| Offset | Size | Type    | Field      | Description                        |
|--------|------|---------|------------|------------------------------------|
| 0      | 4    | float   | pos_x      | 位置 X (米)                         |
| 4      | 4    | float   | pos_y      | 位置 Y (米)                         |
| 8      | 4    | float   | pos_z      | 位置 Z (米)                         |
| 12     | 4    | float   | rot_x      | 四元数 X                            |
| 16     | 4    | float   | rot_y      | 四元数 Y                            |
| 20     | 4    | float   | rot_z      | 四元数 Z                            |
| 24     | 4    | float   | rot_w      | 四元数 W                            |

**总计**: 28 × 3 = **84 bytes** (HMD + 左手柄 + 右手柄)

#### 可选: 欧拉角 (如果 flags & 0x01)
每个设备额外 12 bytes:

| Offset | Size | Type    | Field      | Description                        |
|--------|------|---------|------------|------------------------------------|
| 0      | 4    | float   | euler_x    | 欧拉角 X (度)                       |
| 4      | 4    | float   | euler_y    | 欧拉角 Y (度)                       |
| 8      | 4    | float   | euler_z    | 欧拉角 Z (度)                       |

**总计**: 12 × 3 = **36 bytes** (如果启用)

---

### 2. 手柄按键数据 (Controller Button Data) - 可选

如果 `flags & 0x04`，包含左右手柄按键数据。

#### 单个手柄按键 (23 bytes)

| Offset | Size | Type    | Field              | Description                        |
|--------|------|---------|--------------------|-----------------------------------|
| 0      | 4    | float   | trigger_value      | 扳机模拟值 [0.0 - 1.0]             |
| 4      | 1    | uint8   | trigger_button     | 扳机按钮 (0=未按下, 1=按下)         |
| 5      | 4    | float   | grip_value         | 握把模拟值 [0.0 - 1.0]             |
| 9      | 1    | uint8   | grip_button        | 握把按钮 (0=未按下, 1=按下)         |
| 10     | 1    | uint8   | primary_button     | 主按钮 (左手=X, 右手=A)             |
| 11     | 1    | uint8   | secondary_button   | 副按钮 (左手=Y, 右手=B)             |
| 12     | 1    | uint8   | menu_button        | 菜单按钮                            |
| 13     | 4    | float   | thumbstick_x       | 摇杆 X 轴 [-1.0 - 1.0]             |
| 17     | 4    | float   | thumbstick_y       | 摇杆 Y 轴 [-1.0 - 1.0]             |
| 21     | 1    | uint8   | thumbstick_click   | 摇杆按下                            |
| 22     | 1    | uint8   | thumbstick_touch   | 摇杆触摸                            |

**总计**: 23 × 2 = **46 bytes** (左手柄 + 右手柄)

---

### 3. 手部瞄准数据 (Aim Data) - 可选

如果 `flags & 0x02`，包含左右手瞄准数据。

#### 单只手瞄准数据 (38 bytes)

| Offset | Size | Type    | Field              | Description                        |
|--------|------|---------|--------------------|-----------------------------------|
| 0      | 1    | uint8   | valid              | 射线是否有效                        |
| 1      | 1    | uint8   | pinch              | 是否捏合手势                        |
| 2      | 4    | float   | pinch_strength     | 捏合强度 [0.0 - 1.0]               |
| 6      | 4    | float   | ray_pos_x          | 射线起点 X                          |
| 10     | 4    | float   | ray_pos_y          | 射线起点 Y                          |
| 14     | 4    | float   | ray_pos_z          | 射线起点 Z                          |
| 18     | 4    | float   | ray_rot_x          | 射线方向四元数 X                    |
| 22     | 4    | float   | ray_rot_y          | 射线方向四元数 Y                    |
| 26     | 4    | float   | ray_rot_z          | 射线方向四元数 Z                    |
| 30     | 4    | float   | ray_rot_w          | 射线方向四元数 W                    |
| 34     | 4    | float   | reserved           | 保留字段                            |

**总计**: 38 × 2 = **76 bytes** (左手 + 右手，如果启用)

---

## 完整数据包大小计算

### 基础配置 (仅位姿数据)
```
Header          : 13 bytes
Pose Data       : 84 bytes (3 devices × 28 bytes)
-----------------------------------
Total           : 97 bytes
```

### 标准配置 (位姿 + 按键)
```
Header          : 13 bytes
Pose Data       : 84 bytes
Button Data     : 46 bytes (2 controllers × 23 bytes)
-----------------------------------
Total           : 143 bytes
```

### 完整配置 (含欧拉角 + 按键 + 瞄准)
```
Header          : 13 bytes
Pose Data       : 84 bytes
Euler Angles    : 36 bytes (3 devices × 12 bytes)
Button Data     : 46 bytes
Aim Data        : 76 bytes (2 hands × 38 bytes)
-----------------------------------
Total           : 255 bytes
```

---

## 传输特性

- **字节序**: 所有多字节字段使用小端字节序
- **传输方式**: QUIC Datagram (无序、不可靠)
- **数据包大小**: 受QUIC连接MTU限制，通常不超过1200字节
- **频率**: 建议90Hz-120Hz，匹配VR设备刷新率
- **延迟优先**: 允许丢包，优先保证低延迟

## 中继转发规则

### 后端转发逻辑

位姿数据包通过后端中继时，采用**透明转发**策略：

1. **类型识别**: 检测 `Type == 0x02` 识别为位姿数据
2. **订阅过滤**: 根据 `ResourceMask & 0x02` 过滤订阅者
3. **直接转发**: 不解析内部结构，整包转发
4. **无MTU检查**: 位姿数据包通常较小(<256字节)，不进行MTU适配

### 转发流程

```
发送端 (VR Device)
  ↓
[Type=0x02 位姿数据包]
  ↓
后端服务器 (Relay Server)
  ├─ 识别 Type=0x02
  ├─ 查询订阅关系 (ResourceMask & 0x02)
  └─ 透明转发 → 订阅者
       ↓
接收端 (ROS Node / Unity Client)
```

## 与视频协议的统一

### 共同特性

| 特性           | 视频数据 (Type=0x01) | 位姿数据 (Type=0x02) |
|----------------|---------------------|---------------------|
| Type字段       | 0x01                | 0x02                |
| Timestamp字段  | Unix ms (8字节)      | Unix ms (8字节)      |
| 字节序         | Little-Endian       | Little-Endian       |
| 传输层         | QUIC Datagram       | QUIC Datagram       |
| ResourceMask   | 0x01 (Video)        | 0x02 (Pose)         |
| 转发策略       | MTU切片或直接转发    | 直接透明转发         |

### 设计理念

- **统一识别**: 通过首字节Type字段统一识别数据类型
- **透明中继**: 后端不解析具体内容，降低处理开销
- **类型对齐**: Type值与ResourceMask值保持一致
- **可扩展性**: 预留字段支持未来协议扩展

---

## 实现建议

### 发送端 (VR Client)

```csharp
// 构造位姿数据包
byte[] packet = new byte[143]; // 标准配置
packet[0] = 0x02;  // Type: Pose
BitConverter.GetBytes(timestampMs).CopyTo(packet, 1);  // Timestamp
packet[9] = 1;  // Version
packet[10] = 0x04;  // Flags: INCLUDE_BUTTONS
// ... 填充位姿和按键数据
```

### 接收端 (ROS Node)

```cpp
// 解析位姿数据包
if (data[0] == 0x02) {  // Type: Pose
    uint64_t timestamp = *reinterpret_cast<uint64_t*>(data + 1);
    uint8_t version = data[9];
    uint8_t flags = data[10];
    
    // 解析位姿数据 (offset 13)
    ParsePoseData(data + 13, flags);
}
```

### 后端中继 (Relay Server)

```csharp
// 位姿数据透明转发
if (data[0] == 0x02) {  // Type: Pose
    // 查询订阅者 (ResourceMask & 0x02)
    var targets = GetSubscribers(sourceDeviceId, resourceMask: 0x02);
    
    // 直接转发，不解析
    foreach (var target in targets) {
        SendDatagram(target, data);
    }
}
```

---

## 版本历史

- **Version 1**: 初始协议版本
  - 统一Type和Timestamp字段格式
  - 与视频协议保持一致的头部设计
  - 支持位姿、欧拉角、按键和瞄准数据

---

**最后更新**: 2025年11月3日  
**协议版本**: 1.0  
**Type标识**: 0x02 (位姿数据)  
**ResourceMask**: 0x02 (Pose)
