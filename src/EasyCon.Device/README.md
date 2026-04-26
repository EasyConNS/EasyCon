# EasyCon.Device 模块

## 模块概述

Device模块负责与伊机控下位机（单片机）的串口通信，将上层的手柄操作指令发送到硬件设备。模块实现了完整的Nintendo Switch控制器协议，支持按键、摇杆、Amiibo等所有操控功能。

## 核心功能

### 设备连接
- 串口设备自动发现（`ECDevice`）
- 支持标准串口和TTL串口两种连接实现
- 内建心跳检测和断线自动重连机制

### Switch控制器协议
- **按键操作**: 按下/释放按键（A/B/X/Y/L/R/ZL/ZR/加减/Home/截屏等）
- **方向键(HAT)**: 八方向输入
- **摇杆控制**: 左/右摇杆的精确坐标设定
- **Amiibo**: 切换Amiibo索引、保存Amiibo数据
- **设备控制**: LED触发、固件版本查询、手柄配对、手柄颜色修改、控制器模式切换

### 指令发送
- 基于时间调度的指令队列，确保按键时序精确
- 最小间隔控制，避免指令过快导致设备丢包
- 同步发送带ACK确认机制

### 操作录制
- 支持录制用户的手动操作，回放为简单脚本文本

## 关键类

- **NintendoSwitch** — 高层设备API（partial类），提供按键、摇杆、Amiibo等操作方法
- **ECDevice** — 设备发现，枚举可用串口
- **ECKey / KeyStroke** — 按键模型，封装按键编码和按下/释放动作
- **SwitchReport** — 6字节控制器状态报文（Button + HAT + LX/LY/RX/RY），支持7位打包序列化
- **SerialPortClient** — 串口客户端，管理连接生命周期、心跳和报文收发

## 连接层设计

连接抽象为 `IConnection` 基类，有两个实现：
- **TTLv2SerialClient** — 基于SerialPortClient的新版TTL连接
- **TTLSerialClient** — 基于原始SerialPort流的旧版连接

上层代码通过 `NintendoSwitch` 使用，无需关心底层连接实现。

## 依赖项

- System.IO.Ports（串口通信）
- 无其他项目依赖

---

**版本**: 2.0
