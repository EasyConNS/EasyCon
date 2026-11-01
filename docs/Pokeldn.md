# pokeldn ESP32 手柄固件适配

**状态：已实施；协议及模拟设备测试通过。用户已确认早期适配构建的 ESP32E 连接和控制可用；
本次对齐上游后的构建及 S3 实机验证待完成。**

实现基于 EasyConNS/EasyCon `dev` 提交 `8787f0bda441f85848ad0f978f780d6ebd82c93e`，
在已有的实验性 `PokeldnSerialClient` 上补充可靠通信和 S3 服务连接。核对的 pokeldn
提交为 `30cbb0a47541a4293ca92152838b03cd7c8b9615`，控制器固件版本 **1.3.0**（2026-10-30）。
适配发生在 EasyCon 电脑端，使用上游原版固件，无需重编译 ESP32 固件。

## 固件与硬件选择

pokeldn 有两个独立的固件项目：

- `firmware/esp32` / `pokeldn-radio*.bin`：承载 LDN 无线通信，用于交换、配信等功能，不能当作手柄使用。
- `firmware/pad` / `pokeldn-pad*.bin`：模拟 Switch 手柄，本适配使用这一组固件。

| 板子 | 应刷固件 | 电脑 → 板子 | 板子 → NS | EasyCon 入口 |
|---|---|---|---|---|
| 普通 ESP32 / WROOM-32 | `pokeldn-pad.bin` | USB 转串口，921600、8N1 | Bluetooth Classic Pro Controller | 对应 COM/tty 端口 |
| ESP32-S3 | `pokeldn-pad-s3.bin` | 电脑 BLE，经 pokeldn 本地手柄服务 | 原生 USB，HORI HID | `pokeldn (BLE 服务)` |
| ESP32-C3 / C6 | 不支持手柄固件 | — | 缺少上述手柄所需的硬件组合 | — |

S3 本次通过**官方 pokeldn 服务**桥接 BLE，并非 EasyCon 直接调用操作系统蓝牙 API。
使用 S3 时需运行 pokeldn 的手柄服务；普通 ESP32 串口直连不需要该服务。

## 普通 ESP32 使用步骤

1. 从 [pokeldn Releases](https://github.com/Decryptu/pokeldn/releases) 下载普通 ESP32 的
   `pokeldn-pad.bin`，或在 pokeldn Board 页安装 **Controller** 固件。勿刷 Wireless/Radio。
2. 通过 USB 数据线将 ESP32 接电脑，安装开发板所需的 CP210x/CH340 驱动。
3. 在 NS 上进入「手柄 → 更改握法/顺序」完成配对。固件会在重启后尝试连接已配对主机。
4. 退出会占用该串口的 pokeldn 控制连接/串口监视器。EasyCon 中选择对应端口并连接。
5. 确认日志出现 `pokeldn-pad (串口) 1.3.0`，然后使用虚拟手柄、键盘映射或电脑端脚本。

普通端口按原有 `115200 → 9600` 探测 EasyCon 固件，失败后再探测 `921600` 的 pokeldn-pad。
已知是 pokeldn 时，CLI/API 可显式指定 `pokeldn:COM3` 或 `pokeldn:/dev/ttyUSB0`，跳过旧协议探测。
开口可能触发开发板复位，握手会在约 3 秒内重试状态查询。连接后保持串口打开，不按每个按键重开。

```powershell
ezcon run script.ecs --port pokeldn:COM3
```

## ESP32-S3 使用步骤

1. 安装 `pokeldn-pad-s3.bin` / Board 页的 **Controller** 固件。
2. 将 S3 的**原生 USB/OTG 接口**接 NS；NS 供电并将它枚举为 `0f0d:0092` 的 HORI 手柄。
   控制固件占用此接口作为 HID，不能将其当作电脑串口使用。
3. 开启电脑蓝牙，在 pokeldn **Control** 页面连接 `POKELDN-PAD`，保持 pokeldn 运行。
   若同时插着普通 ESP32，先在 pokeldn 中确认连接的是目标 S3；官方服务会优先尝试串口板。
4. EasyCon 设备下拉框选择 **pokeldn (BLE 服务)**，点击连接。
5. 使用 EasyCon 控制。连接时会停止板上已有宏并释放按键；不要同时在 pokeldn Control 页面发按键或启动宏。

也可在已配置依赖的 pokeldn 源码环境启动官方服务：

```powershell
python -m pokeldn.pad.service
```

然后在 EasyCon CLI 中使用：

```powershell
ezcon run script.ecs --port pokeldn://service
```

服务固定为本机 `127.0.0.1:47800`，无需开放防火墙端口。EasyCon 不会自动启动、下载或退出 pokeldn
进程。EasyCon 断开时发送释放和 `disconnect`，服务进程继续运行。
macOS 的 BLE 权限和服务启动方式沿用 pokeldn 官方应用的设置。

## 已适配的能力与边界

- A/B/X/Y、L/R/ZL/ZR、加减、左右摇杆按压、HOME、截屏、八向十字键和双摇杆全范围。
- 虚拟手柄、SDL 键盘/手柄映射、电脑端 ECS 脚本以及共用 `NintendoSwitch` 的 Flow/CLI 控制。
- 停止脚本/Reset 会清空待发送输入并发送固件 STOP，正常断开时再次释放全部按键。
- 每秒查询板子状态，发送逐条确认；串口错误、拒绝的 ACK 或失联会退出连接并上报错误。
- 不支持 EasyCon 离线字节码烧录、远程启动/停止离线脚本、Amiibo、颜色、控制器模式和软件解除配对。
  这些操作会明确失败，不能向 pokeldn 发送 EasyCon 私有命令。普通「运行脚本/停止脚本」仍可用。
- `FirmwareVersion` 单独保存 `1.3.0` 文本，旧 `GetVersion()` 的 EasyCon 协议版本不能伪造。
- 「已连接」表示电脑已连接控制板。`ConsoleConnected` 对应固件 `mounted`；为 false 时，
  日志会提示检查 USB/配对，但保留电脑连接以便之后配对。
- BLE 写入使用上游服务的可靠写入路径，速度受系统蓝牙影响；原有 30 ms 节流不构成实机端延迟保证。
  极短点按和严格帧计时需要实测。
- 普通 ESP32 的 UART 固件没有电脑失联看门狗；若电脑进程崩溃或串口线意外断开，
  主动释放可能无法送达，应重连或重启板子。此限制不能仅靠电脑端适配消除。
- 用户已确认早期适配构建中 ESP32E 使用正确的 Controller 固件后可连接并控制 NS。
  本次对齐上游的构建尚未进行实机复测；ESP32-S3 和 Switch 2 尚未实测。
- 当前固件不提供从 NS 休眠状态主动唤醒的保证。普通 HOME 报告仅在蓝牙链路已建立时发送；
  `bt_esp32.c` 在链路关闭后重新进入可发现状态，没有收到 HOME 输入就主动重连/唤醒的路径。
  用户反馈 ESP32E 无法唤醒，与该实现一致。请先用 NS 电源键或原装手柄唤醒主机。

## 协议分析与实现

共享 HID 报告为 8 字节：按钮 `u16 little-endian`、HAT、LX、LY、RX、RY、vendor（主机置 0）。
按钮位与 EasyCon `SwitchButton` 相同，HAT 的 0–7 为顺时针方向、8 居中，摇杆 128 居中、0 左/上。
旧 EasyCon 采用 7 位打包，不能把 `SwitchReport.GetBytes()` 直接交给 pokeldn。
新增 `IConnection.WriteReport` 让连接实现选择编码；旧连接继续使用原有序列化。
保留 `PokeldnSerialClient` 的公开入口、翻译方法和 `CreatePokeldnConnection` 测试接缝。
`BytesSent` 在收到固件/服务 ACK 后回传原来的 7 位打包 HID，保持 Maa 会话确认中立报告的事件契约；
这不代表 NS 或游戏已经处理输入。串口别名及服务的显示名/URI 共用同一个来源占用锁。

普通 ESP32 的 UART 帧：

```text
A5 5A | length u16 LE | type | payload | sum(type + payload) mod 256
length = 1 + payload 长度，最大 600
01 report（8 字节） → 81 result（有符号错误码，0 成功）
02 status           → 82 status record
03 command（13 STOP）→ 81 result

查询状态：A5 5A 01 00 02 02
停止宏：  A5 5A 02 00 03 13 16
按下 A：  A5 5A 09 00 01 04 00 08 80 80 80 80 00 0D
```

S3 的 BLE 服务 UUID 为 `7a1e0001-5d2c-4c3e-9f4b-504f4b454c44`，末段 `0002/0003/0004`
分别对应报告、状态、命令。当前实现通过 `pokeldn.pad.service` 的 JSON 行协议调用：

```json
{"op":"connect"}
{"op":"status"}
{"op":"stop"}
{"op":"send","report":"0400088080808000"}
{"op":"disconnect"}
```

每条请求等待一条 `ok` 响应，拒绝/超时不当作成功；扫描可等待最多 45 秒，普通 I/O 有独立超时。
串口、服务共用 `PokeldnConnection` 的队列、停止释放和状态轮询，测试通过传输接缝注入模拟固件。

核对来源（固定提交）：

- [Controller board 文档](https://github.com/Decryptu/pokeldn/blob/30cbb0a47541a4293ca92152838b03cd7c8b9615/docs/hardware_pad.md)
- [UART 固件实现](https://github.com/Decryptu/pokeldn/blob/30cbb0a47541a4293ca92152838b03cd7c8b9615/firmware/pad/main/uart_link.c)
- [蓝牙发送与连接生命周期](https://github.com/Decryptu/pokeldn/blob/30cbb0a47541a4293ca92152838b03cd7c8b9615/firmware/pad/main/bt_esp32.c)
- [报告状态与宏命令](https://github.com/Decryptu/pokeldn/blob/30cbb0a47541a4293ca92152838b03cd7c8b9615/firmware/pad/main/player.c)
- [官方串口客户端](https://github.com/Decryptu/pokeldn/blob/30cbb0a47541a4293ca92152838b03cd7c8b9615/pokeldn/pad/serial_link.py)
- [官方 BLE 服务协议](https://github.com/Decryptu/pokeldn/blob/30cbb0a47541a4293ca92152838b03cd7c8b9615/pokeldn/pad/service.py)

## 验证

2026-10-30 本机验证：设备协议测试 **51/51**，新增 Maa 会话集成测试 **4/4**；
Release 全项目测试 **1,883 通过、182 跳过、0 失败**，另有 29 项显式测试未选择运行。
Release 构建及 `dotnet format --verify-no-changes` 通过。
跳过项主要依赖本机未安装的 C99 编译器、OCR 模型/资料，以及非 Windows 平台功能。
这些结果不替代 NS 实机验证。

```powershell
dotnet test test/EasyCon.Device.Tests/EasyCon.Device.Tests.csproj
dotnet test EasyCon2.slnx -c Release
dotnet format --verify-no-changes
```

设备测试覆盖固定报文、14 个按钮位、9 个 HAT 值、摇杆边界、逐字节分片、粘包、噪声/坏校验、
启动重试、负 ACK/超时/取消、真实设备队列输出、Reset/断开释放、空闲掉线、旧协议回归、
能力拒绝，以及真实本机 TCP 上的官方服务请求/响应契约。

实机验收：分别检查两种板的 A 点按、组合键、十字键、摇杆边界、持续按下后停止脚本、主动断开、
拔插重连，以及 S3 关闭 pokeldn 服务后能否在 EasyCon 观察到断线。首次配对和每种 NS 型号单独记录。
