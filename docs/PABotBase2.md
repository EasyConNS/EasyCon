# PABotBase2 / Pico W 连接

EasyCon 可以通过 USB 串口向 PABotBase2 固件发送在线手柄输入。适配器加入现有
`SerialConnectionProvider`，GUI 和 CLI 使用相同的设备列表、连接路径与串口来源占用锁。

## 板子与固件

- Pico W / Pico WH 使用官方 `PABotBase2-Pico1W-<版本>.uf2`。
- Pico 2 W / Pico 2 WH 使用官方 `PABotBase2-Pico2W-<版本>.uf2`。
- 本适配使用 NS1 无线 Pro Controller 模式（`0x1180`）；不支持普通非 W Pico 的有线模式、
  Joy-Con 专有操作或 PABotBase 第一代协议。NS2、主机唤醒和陀螺仪不在当前验收范围。
- 握手校验 RSC 和 MLC 的 `20260618xx` 协议版本，并查询固件支持的手柄类型。
  若需要切换模式，使用保留配对设置的 `CHANGE_CONTROLLER_MODE`，不使用清除设置的重置命令。

固件请从 [PokemonAutomation 官方配置指南](https://pokemonautomation.github.io/zh-CN/SetupGuide/Controllers/Controller-PicoW-USB.html)
获取，按其说明刷入。EasyCon 不附带或修改这些固件文件。

## 连接步骤

1. Pico W 的 USB 接电脑，Pico W 通过自身蓝牙连接 NS。
2. 在 EasyCon 中选择对应串口，例如 Windows 的 `COM3` 或 Linux 的 `/dev/ttyACM0`，点击连接。
   程序按注册顺序自动探测协议；首次连接可能需要等待前面的协议探测结束。
3. 首次配对时，在 NS 打开「手柄 → 更改握法/顺序」，按官方指南完成配对。
   电脑串口连接成功时，NS 可能尚未配对；两者的连接状态分别记录。
4. 验证 A/B、十字键、两个摇杆和停止脚本后的释放行为，再运行自动化脚本。

CLI 同样使用普通串口地址：

```powershell
ezcon run script.ecs --port COM3
```

不需要电脑蓝牙适配器或本地桥接服务。不要让 PokemonAutomation 和 EasyCon 同时占用该串口。
按官方文档，Pico W 断电后需要重新配对；关闭电脑软件与断电是不同的操作。

## 时序和停止行为

按键、十字键和摇杆转换为 OEM 十字节状态，Y 轴方向和 Pro Controller 摇杆死区按官方实现转换。
在线输入通过「下一条替换」命令更新，持续按住时每约 200 ms 刷新一个 1000 ms 的状态命令。
发送队列保留按下/释放边沿，并沿用至少 30 ms 的发送间隔；积压超过 256 项时报错。
这些是电脑端调度参数，不保证游戏帧级精度，也不表示 NS 已接收或处理输入。

停止脚本会清空待发队列，随后取消固件命令队列并发送中立报告；正常断开也执行释放。
ACK 超时后会先重新建立可靠串流，避免用旧序号发送新的停止数据。
若 USB 被拔出、进程强退或无线断开，电脑无法确认释放，不能把周期刷新当作已验证的固件看门狗。

本协议适配只支持电脑运行脚本和在线手柄输入，不支持 EasyCon 原生离线脚本烧录、Amiibo 等专有命令。

## 实现与验证

实现位于 `src/EasyCon.Device/Connection/PABotBase2/`，包括带会话 CRC32C 的可靠数据包、
有界重试、分包/重组、重复包去重、接收乱序窗口、序号及流偏移回绕、状态查询和输入队列。
串口保持 DTR=1、RTS=0，与官方 USB 串口连接设置一致。

协议参考 [PokemonAutomation/Arduino-Source](https://github.com/PokemonAutomation/Arduino-Source/tree/159a7e9929b84428a547696ddc511f5cdb664954/Common/PABotBase2)，
来源和 MIT 许可保存在同目录 `NOTICE.txt` 并随应用输出分发。

无硬件测试通过模拟固件验证报文、ACK 丢失/错序、重传去重、接收乱序、协议版本拒绝、
状态切换、连续按下/释放、长按刷新、队列溢出、取消和串口释放。
CRC 固定向量由官方 C 实现生成，用于避免收发两端共用辅助函数掩盖编码错误。

**尚未进行 Pico W / Pico 2 W 与 NS 的实物验收。** 合并前仍需确认两种板子的枚举、配对、
常用脚本时序、停止/重连，以及断电后的行为。