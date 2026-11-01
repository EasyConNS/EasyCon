# EasyCon.Device 模块

## 模块概述

Device 模块负责通过串口或桥接服务连接手柄设备，将上层的手柄操作发送到硬件。各连接实现声明固件信息和能力；按键、摇杆通用，Amiibo 等扩展命令取决于设备协议。

## 核心功能

### 设备连接
- 串口设备发现（`ECDevice` 枚举本机串口）
- 通用串口协议注册（`SerialConnectionProvider`）：默认依次探测原生协议 115200、9600 和 pokeldn-pad 921600
- 通用 TCP 桥接注册（`BridgeConnectionProvider`）：显式指定 `tcp://主机:端口`，默认适配 pokeldn 官方 BLE 服务
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

- **NintendoSwitch** — 高层设备 API（partial 类），提供按键、摇杆等操作；协议组合集中于 `NintendoSwitchConnections.cs`
- **ECDevice** — 设备发现，枚举可用串口
- **ConnectionEndpoint** — 规范化串口/TCP 地址及来源占用标识，不识别固件名称
- **IConnectionProvider** — 按传输类型提供延迟创建的候选协议，支持宿主注入
- **ECKey / KeyStroke** — 按键模型，封装按键编码和按下/释放动作
- **SwitchReport** — 7 字节控制器状态（2 字节 Button + HAT + LX/LY/RX/RY），支持原生固件的 7 位打包序列化
- **TTLSerialClient** — 原生固件连接：后台写循环、状态机（Connecting/Connected/Error）、一次性使用（断开后由上层重建）
- **PokeldnConnection** — pokeldn-pad 在线控制，串口 / 官方本地 BLE 服务共用报告队列、STOP 释放与状态轮询

## 连接层设计

连接抽象为 `IConnection` 基类（public）。`NintendoSwitch` 构造函数可注入
`IConnectionProvider` 集合，分别注册串口和桥接协议，不改变报告队列和上层调用。
原有 `protected virtual CreateConnection` 测试接缝保留，见 test/EasyCon.SDLInput.Tests。
原生固件使用 **TTLSerialClient**：`Connect()` 启动后台写循环，`Disconnect()` 取消并
有界等待循环退出（避免新旧串口句柄竞争同一 COM 口）。上层通过 `NintendoSwitch` 使用，
无需关心底层连接实现。

`IConnection.WriteReport` 在连接层选择 HID 编码；原生固件保持 7 位打包，pokeldn-pad 使用
8 字节原始 HID。`SupportsLegacyCommands` 阻止向 pokeldn 发送烧录、Amiibo 等专有指令。
`GetPortNames()` 只枚举物理串口；GUI 保留用户配置的桥接地址，不添加固件专用服务列表项。
地址配置、协议扩展和占用规则见 [通用设备连接](../../docs/DeviceConnections.md)，
具体协议和硬件限制见 [pokeldn 适配说明](../../docs/Pokeldn.md)。

## 依赖项

- System.IO.Ports（串口通信）
- 无其他项目依赖

---

**版本**: 2.0
