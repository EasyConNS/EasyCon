# 通用设备连接

**状态：已实施。** 串口协议和桥接协议分开注册；地址、设备列表和来源占用不依赖固件名称。

## 选择传输

| 传输方式 | 地址示例 | 连接行为 |
|---|---|---|
| `Serial` | `COM3`、`/dev/ttyUSB0` | 在同一串口按注册顺序探测协议，失败后关闭连接再尝试下一项 |
| `TcpBridge` | `tcp://127.0.0.1:47800`、`tcp://[::1]:31789` | 直接访问指定桥接服务；不尝试打开串口 |

`ECDevice.GetPortNames()` 只枚举真实串口，自动连接也只扫描这些端口。桥接地址由用户配置，
GUI 在设备列表中保留该选择；「刷新」不会把它标记成丢失的串口。服务是否可用在连接时确认。

默认串口顺序为原生 EasyCon 协议 115200、9600，再尝试 pokeldn-pad 协议 921600。
默认桥接适配器实现 `pokeldn.pad.service` 的 JSON 行协议。`tcp://` 只表示传输地址，
不意味着任意 TCP / 蓝牙服务均可互通；采用其他消息格式的服务需要注册相应的协议适配器。
蓝牙扫描、权限、配对和链路维护由桥接服务负责。

## 配置桥接地址

GUI 使用现有的 `controller_profile`。Windows 上先退出 EasyCon，再编辑
`%APPDATA%\easycon\config.json` 中的这一项，保留其他设置：

```json
{
  "controller_profile": {
    "Endpoint": "tcp://127.0.0.1:47800",
    "Transport": "TcpBridge"
  }
}
```

重启后在设备列表中选择该地址，点击连接。切换到普通串口并保存时，`Transport` 会更新为 `Serial`。
其他平台的配置路径由 `AppPaths.ConfigFile` 确定（用户 ApplicationData 目录下的 `easycon/config.json`）。
此配置仅设置连接目标，不启动或安装服务。

CLI 可直接指定地址，也可复用同一份配置：

```powershell
ezcon run script.ecs --port COM3
ezcon run script.ecs --port tcp://127.0.0.1:47800
ezcon run script.ecs --profiles profiles.json
```

TCP 地址必须包含主机与 1–65535 的端口，不接受用户名、密码、路径（可有结尾 `/`）、查询或片段。
配置文件的 `Transport` 必须与地址一致；CLI 的显式 `--port` 覆盖会同时更新传输方式，保留采集配置。

## 注册协议

`IConnectionProvider.GetCandidates` 按端点返回连接工厂。`SerialConnectionProvider` 管理
串口协议与波特率，`BridgeConnectionProvider` 管理桥接协议。工厂延迟创建，确保探测失败时
先释放旧句柄；成功后停止探测。

默认注册集中在 `NintendoSwitchConnections.cs`。宿主可通过构造函数提供自己的协议集合，
替换默认注册；例如以下工厂由宿主提供，分别实现自己的串口帧和桥接消息格式：

```csharp
NintendoSwitch CreateController(
    Func<string, int, IConnection> serialFactory,
    Func<ConnectionEndpoint, IConnection> bridgeFactory) => new(
[
    new SerialConnectionProvider([new SerialProtocol(115200, serialFactory)]),
    new BridgeConnectionProvider([bridgeFactory])
]);
```

连接实现继承 `IConnection`，承担连接/断开、状态事件、输入编码、写入确认和队列清理。
连接成功须上报 `Status.Connected`；`WriteReport` 接收通用 `SwitchReport`，由适配器编码。
`BytesSent` 的报告事件保留 EasyCon 打包 HID 作为确认载荷，以便 `ResetAndWaitForWriteAsync`
验证中立报告已写出；该确认不代表 NS / 游戏已处理输入。
仅支持在线输入的协议应声明 `SupportsLegacyCommands = false`，拒绝离线烧录等原生专有命令。

扩展协议不需要在 Core、GUI 或 CLI 添加固件名称分支。`NintendoSwitch.CreateConnection`
和已有的 `CreatePokeldnConnection` 测试接缝仍保留，便于复用现有的无硬件测试。

## 端点与独占

`ConnectionEndpoint` 同时提供实际连接地址和来源占用键；`PadSession` 在打开设备前取得
`SourceOwnership`，用于同机 EasyCon 会话的进程间互斥：

- 去掉首尾空白；Windows 的 `COM3`、`com3`、`\\.\COM3` 使用相同串口标识。
- Unix 的绝对串口路径解析符号链接，避免 `/dev/serial/by-id/...` 与目标路径重复占用。
- TCP 统一主机名大小写、结尾斜杠和等价 IPv6 表示；相同端点的这些写法共享占用锁。

地址规范化不执行 DNS 查询，也不合并不同主机名或 IPv4 / IPv6 监听地址。多个客户端应使用
同一服务地址；第三方客户端或多个桥接服务访问同一蓝牙板的互斥仍需桥接端负责。

## 验证边界

`ConnectionProviderTests` 覆盖延迟探测、失败释放、注册自定义协议、串口与桥接隔离和地址校验。
`PokeldnServiceTests` 通过真实本机 TCP 与模拟服务核对请求/响应。
Core / GUI 测试覆盖别名锁、配置读写、CLI 覆盖及显式桥接选择。
这些测试不依赖实物，也不替代板子和 NS 的验收；两种 ESP32 的接线、固件与实测边界见
[pokeldn 适配说明](Pokeldn.md)。
