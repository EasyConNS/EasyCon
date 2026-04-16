# EasyCon.Device 模块

## 模块概述

Device模块负责处理与伊机控下位机的通讯功能，提供抽象接口给上层应用。该模块实现了多种通信方式，支持串口、蓝牙和网络连接，为EasyCon2项目提供可靠的硬件设备控制能力。

## 核心功能

### 1. 设备发现与连接
- **串口设备**: 自动扫描和识别可用的串口设备
- **蓝牙设备**: 支持蓝牙串口配置文件(SPP)的设备发现
- **网络设备**: 支持TCP/IP网络连接的设备识别

### 2. 通信协议支持
- **Modbus协议**: 工业标准通信协议
- **自定义串口协议**: 针对伊机控设备的专用协议
- **TCP/IP协议**: 网络通信支持

### 3. 设备管理
- 连接状态监控
- 自动重连机制
- 设备响应时间统计
- 错误处理和恢复

## 主要组件

### NintendoSwitchCmd 类
负责与任天堂Switch下位机的具体命令通信：

```csharp
public class NintendoSwitchCmd
{
    public enum ConnectResult
    {
        Success,
        PortNotFound,
        AccessDenied,
        Timeout,
        DeviceNotResponding
    }
    
    public ConnectResult TryConnect(string port);
    public void Disconnect();
    public bool SendCommand(byte[] command);
    public byte[] ReceiveData();
}
```

### ECDevice 类
设备管理的核心类，提供统一的设备访问接口：

```csharp
public class ECDevice
{
    public static string[] GetPortNames();
    public static bool TestConnection(string port);
    public static DeviceInfo GetDeviceInfo(string port);
}
```

### JoyStickDevice 类
虚拟手柄设备管理：

```csharp
public class JoyStickDevice
{
    public void SendStickData(byte leftX, byte leftY, byte rightX, byte rightY);
    public void SendButtonData(uint buttonData);
    public void Reset();
}
```

## 通信协议

### 自定义串口协议格式

```
┌─────────┬─────────┬─────────┬─────────┬─────────┐
│ 帧头    │ 数据长度│ 数据体  │ 校验码  │ 帧尾    │
│ 0xAA55  │ 2 bytes │ N bytes │ CRC16   │ 0x0D0A  │
└─────────┴─────────┴─────────┴─────────┴─────────┘
```

### 命令类型定义
- `0x01`: 按键按下命令
- `0x02`: 按键释放命令
- `0x03`: 摇杆移动命令
- `0x04`: 设备状态查询
- `0x05`: 设备配置命令

## 连接管理

### 自动连接机制
```csharp
public class AutoConnector
{
    public string? FindAvailableDevice();
    public bool TryAutoConnect(out string connectedPort);
    public void SetConnectionTimeout(int milliseconds);
}
```

### 连接状态监控
```csharp
public enum DeviceStatus
{
    Disconnected,
    Connecting,
    Connected,
    Reconnecting,
    Error
}
```

## 使用示例

### 基本连接示例
```csharp
// 创建设备实例
var device = new NintendoSwitchCmd();

// 尝试连接
var result = device.TryConnect("COM3");
if (result == NintendoSwitchCmd.ConnectResult.Success)
{
    Console.WriteLine("连接成功");
    
    // 发送按键命令
    device.SendCommand(new byte[] { 0x01, 0x01, 0x00 }); // 按下A键
    Thread.Sleep(50);
    device.SendCommand(new byte[] { 0x02, 0x01, 0x00 }); // 释放A键
    
    // 断开连接
    device.Disconnect();
}
```

### 自动连接示例
```csharp
var connector = new AutoConnector();
if (connector.TryAutoConnect(out string port))
{
    Console.WriteLine($"自动连接成功: {port}");
}
else
{
    Console.WriteLine("未找到可用设备");
}
```

## 性能特性

### 通信参数
- **波特率**: 支持9600、115200等标准波特率
- **数据位**: 8位
- **停止位**: 1位
- **校验位**: 无校验
- **超时时间**: 可配置，默认1000ms

### 性能优化
- 异步I/O操作
- 批量命令发送
- 连接池管理
- 错误重试机制

## 错误处理

### 异常类型
```csharp
public class DeviceException : Exception
{
    public DeviceErrorType ErrorType { get; }
    
    public DeviceException(DeviceErrorType errorType, string message) 
        : base(message)
    {
        ErrorType = errorType;
    }
}

public enum DeviceErrorType
{
    PortNotFound,
    AccessDenied,
    Timeout,
    DeviceNotResponding,
    InvalidData,
    ConnectionLost
}
```

### 错误恢复策略
1. **连接失败**: 自动重试3次，间隔递增
2. **通信超时**: 自动重连，记录错误日志
3. **设备无响应**: 重置连接状态，通知上层应用
4. **数据校验失败**: 请求重新发送

## 配置选项

### 串口配置
```csharp
public class SerialPortConfig
{
    public int BaudRate { get; set; } = 115200;
    public int DataBits { get; set; } = 8;
    public int StopBits { get; set; } = 1;
    public Parity Parity { get; set; } = Parity.None;
    public int ReadTimeout { get; set; } = 1000;
    public int WriteTimeout { get; set; } = 1000;
}
```

### 连接策略配置
```csharp
public class ConnectionStrategy
{
    public int MaxRetryCount { get; set; } = 3;
    public int RetryIntervalMs { get; set; } = 1000;
    public bool EnableAutoReconnect { get; set; } = true;
    public int HeartbeatIntervalMs { get; set; } = 5000;
}
```

## 调试支持

### 日志记录
```csharp
public class DeviceLogger
{
    public void LogCommand(byte[] command, string direction);
    public void LogError(Exception ex, string context);
    public void LogConnectionStatus(DeviceStatus status);
}
```

### 调试工具
- 串口监视器
- 协议分析器
- 连接测试工具

## 依赖项

- **.NET**: System.IO.Ports
- **项目依赖**: 无
- **硬件要求**: 串口设备、蓝牙适配器或网络连接

## 未来发展

### 计划功能
- [ ] WiFi直连支持
- [ ] USB HID设备支持
- [ ] 更多设备型号适配
- [ ] 设备固件升级功能
- [ ] 云端设备管理

### 性能优化
- [ ] 通信协议压缩
- [ ] 多线程并发处理
- [ ] 更高效的错误恢复算法

---

**模块维护者**: EasyCon.Device开发团队  
**最后更新**: 2026年4月16日  
**版本**: 1.0