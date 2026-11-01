# EasyCon.Device 模块

## 模块概述

Device模块负责与伊机控下位机（单片机）的串口通信，将上层的手柄操作指令发送到硬件设备。模块实现了完整的Nintendo Switch控制器协议，支持按键、摇杆、Amiibo等所有操控功能。

## 核心功能

### 设备连接
- 串口设备发现（`ECDevice` 枚举本机串口）
- TTL 串口连接（`TTLSerialClient`：115200 失败回退 9600）
- pokeldn-pad：旧协议探测失败后尝试 921600；ESP32-S3 可选择 `pokeldn (BLE 服务)`
- 断线经 `StatusChanged` 上报，由上层（DeviceService）负责重连

### Switch控制器协议
- **按键操作**: 按下/释放按键（A/B/X/Y/L/R/ZL/ZR/加减/Home/截屏等）
- **方向键(HAT)**: 八方向输入
- **摇杆控制**: 左/右摇杆的精确坐标设定
- **Amiibo**: 切换Amiibo索引、保存Amiibo数据
- **设备控制**: LED触发、固件版本查询、手柄配对、手柄颜色修改、控制器模式切换

### 指令发送
- 后台写循环 + 有界间隔（30ms MINIMAL_INTERVAL）节流，避免指令过快丢包
- ConcurrentQueue 批量出队合并为单次串口写
- 同步发送带 ACK 确认机制（SendSync）

### 操作录制
- 支持录制用户的手动操作，回放为简单脚本文本

## 关键类

- **NintendoSwitch** — 高层设备API（partial类，JoyStickDevice/NintendoSwitchPriv/NintendoSwitchCmd 三分），提供按键、摇杆、Amiibo等操作方法
- **ECDevice** — 设备发现，枚举可用串口
- **ECKey / KeyStroke** — 按键模型，封装按键编码和按下/释放动作
- **SwitchReport** — 7 字节控制器状态（2 字节 Button + HAT + LX/LY/RX/RY），支持原生固件的 7 位打包序列化
- **TTLSerialClient** — 原生固件连接：后台写循环、状态机（Connecting/Connected/Error）、一次性使用（断开后由上层重建）
- **PokeldnConnection** — pokeldn-pad 在线控制，串口 / 官方本地 BLE 服务共用报告队列、STOP 释放与状态轮询

## 连接层设计

连接抽象为 `IConnection` 基类（public，作为测试接缝：测试可子类化 `NintendoSwitch` 并覆写
`protected virtual CreateConnection` 注入假连接，见 test/EasyCon.SDLInput.Tests）。
原生固件使用 **TTLSerialClient**：`Connect()` 启动后台写循环，`Disconnect()` 取消并
有界等待循环退出（避免新旧串口句柄竞争同一 COM 口）。上层通过 `NintendoSwitch` 使用，
无需关心底层连接实现。

`IConnection.WriteReport` 在连接层选择 HID 编码；原生固件保持 7 位打包，pokeldn-pad 使用
8 字节原始 HID。`SupportsLegacyCommands` 阻止向 pokeldn 发送烧录、Amiibo 等专有指令。
连接列表还包含本机 pokeldn 服务入口，纯串口枚举 `GetPortNames()` 保持原语义。
详细协议和限制见 [pokeldn 适配说明](../../docs/Pokeldn.md)。

## 依赖项

- System.IO.Ports（串口通信）
- 无其他项目依赖

---

**版本**: 2.0
