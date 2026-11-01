# 通用手柄原子模块

状态：2026-10-24。对外只提供 `easycon-device`；公共手柄接口、串口枚举／会话和自动探查都在此 crate 内，`backends::easycon` 与 `backends::pokeldn` 是两种内置实现，不依赖现有 .NET 程序。

## 模块边界

```text
调用方 / 后续编排原语
        │
        ├── ports()                         通用串口枚举
        ├── GamepadBackend::connect(...)     指定端口、指定后端
        └── Box<dyn Gamepad>                 按键 / 方向帽 / 摇杆 / 状态 / 复位 / 断开
                    │
             DeviceSession<Backend>          唯一串口所有权、限速、取消、故障和收尾
                    │
           ┌────────┴─────────┐
        EasyCon             Pokeldn
      7-bit 打包         A5 5A 帧与应答
           └────────┬─────────┘
                Transport → serialport
```

通用层持有逻辑状态、连接身份和生命周期，后端只处理波特率、控制线策略、握手、帧编码和协议应答。`Transport` 提供有限超时的读写和 Drop 释放接缝，可注入假串口测试。两个后端都可以通过 `Box<dyn Gamepad>` 使用。

上层 `easycon_sense::controller::connect_serial(port, token)` 只需串口名，按 EasyCon 115200 → EasyCon 9600 → Pokeldn 921600 自动探查并返回通用手柄。实现位于组合宿主层，两种协议库仍相互独立。`easycon-pad probe --port <串口名>` 缺省使用自动探查；显式 `--backend` 仍供诊断使用。取消或已建立会话的动作／清理失败不会进入下一协议。

| 原子能力 | API | 语义 |
|---|---|---|
| 枚举 | `ports()` | 返回所有系统串口及可用的 USB VID/PID/序列号，不打开端口、不按厂商过滤、不探测固件 |
| 连接 | `backend.connect(port, options, token)` | 独占打开所选串口，完成对应协议握手并发送中立状态后返回会话 |
| 按下 | `Gamepad::press(button, token)` | 设置一个按钮位，保留其他按钮、方向帽和摇杆；不自动抬起 |
| 抬起 | `Gamepad::release(button, token)` | 清除一个按钮位，保留其他状态 |
| 多键按下 | `Gamepad::press_buttons(buttons, token)` | 在同一份报告中设置所有指定按钮位，保留未指定的状态 |
| 多键抬起 | `Gamepad::release_buttons(buttons, token)` | 在同一份报告中清除所有指定按钮位，保留未指定的状态 |
| 方向帽 | `Gamepad::set_hat(hat, token)` | 替换完整方向帽；支持八个方向，`Hat::Center` 释放方向帽 |
| 摇杆 | `Gamepad::set_stick(StickId, position, token)` | 设置指定左／右摇杆的 x、y，保留另一摇杆、按键与方向帽 |
| 摇杆回中 | `Gamepad::center_stick(StickId, token)` | 只将指定摇杆设置为 `(128, 128)` |
| 完整状态 | `Gamepad::apply_report(report, token)` | 用一次完整报告替换按钮、方向帽和双摇杆状态，可表示同时按下多个键 |
| 复位 | `Gamepad::reset(token)` | 一次全中立报告，不包含等待或后续动作 |
| 断开 | `Gamepad::disconnect()` | 尝试发送中立报告，然后释放串口；失败返回 `CleanupFailed`，重复断开返回相同结果 |

本层不提供长按、点击、组合键时序、宏、自动连点或动作重试。未来长按由“按下 → 等待 → 抬起”组合。多键同时按下通过 `press_buttons` 表达；需要同时修改按钮、方向帽和双摇杆时使用 `apply_report`。当前会话是单一状态所有者，不实现跨运行的按键租约或引用计数。

增量操作由 `Gamepad` trait 的默认方法统一实现，全部调用 `apply_report`，两种协议后端和 mock 共用相同语义。每次成功调用恰好交付一份完整报告；重复按钮自动折叠，空按钮列表及重复设置也发送报告并产生新的 `WriteReceipt`，不通过跳过发送伪造交付结果。摇杆使用原始坐标，不隐含角度换算、死区或校准。

## 状态与执行结果

`Report` 包含 14 位按钮掩码、方向帽、左右摇杆。按钮位 14/15 保留，设置时在发送前报参数错误。方向帽为 0 上、顺时针至 7 左上、8 居中。摇杆坐标 0..255，128 居中，x 向右增大、y 向下增大。

按键、方向帽与摇杆增量操作从最后成功交付的完整报告计算新状态；只有完成交付后才提交本地状态。重复按下不增加计数，重复抬起不影响其他按键。`state()` 是最后交付的状态，不是从主机或游戏读回的状态。

`WriteReceipt` 包含进程内连接代次、报告序号、主机串口写入完成时刻和交付级别：

- `Delivery::Written`：EasyCon 没有本报告的可靠逐帧应答，只证明操作系统串口写调用完成。
- `Delivery::Accepted`：Pokeldn 返回了校验通过、类型 `0x81`、结果码 0 的应答，只证明固件接受该报告。

两者都不证明 Switch 或游戏已执行。连接时的中立报告也占用一个序号，因此调用方首次动作通常从序号 2 开始。重新连接获得新的会话代次。

同一会话的可变引用使动作串行执行，没有后台队列、状态合并或动作丢弃。两种后端的报告事务结束后至少等待 30ms 才启动下一次报告，包含连接复位和断开复位；这是设备发送节流，不是业务长按时间。`written_at` 记录写完成时刻，Pokeldn 的应答完成可能稍晚。

串口中途失败、部分写超时、固件拒绝或应答超时会使状态变为未知，后续动作返回故障而不会自动重发。`is_connected()` 表示本地会话是否仍可发送，不是持续后台心跳；物理拔插可能到下一次 I/O 才被观察。故障会话仍须断开，再建立新连接。

## 协议后端

| 项目 | EasyCon | LDN Pokémon / Pokeldn |
|---|---|---|
| 内置模块 | `easycon_device::backends::easycon` | `easycon_device::backends::pokeldn` |
| 后端 | `EasyCon` | `Pokeldn` |
| 波特率 | 默认先 115200，失败释放端口并等待 100ms 后尝试 9600 | 921600 |
| 线路 | 8N1，无流控 | 8N1，无流控 |
| 握手 | 发送 `A5 A5 81`，在接收流中识别 `80` | 周期发送 STATUS `02`，等待合法 STATUS_REPLY `82` |
| 默认握手预算 | 每档 1 秒 | 3 秒，每 300ms 查询一次，以容纳打开端口后的启动延迟 |
| 报告 | 7 字节原始状态 → 8 字节传输包 | 8 字节报告载荷 → 14 字节帧 |
| 报告应答 | 不要求 | `81` + i8 结果：0 成功，-1 长度错误，-2 拒绝 |

连接时显式选择后端；枚举不会自行确定协议，也不会依次向所有端口发送握手。`ConnectOptions.baud_rate` 指定后只尝试该速率，并必须属于所选后端。可覆盖握手预算及报告事务预算，后者默认 200ms。

### EasyCon 7 字节状态与线路编码

原始状态：`buttons u16 BE | HAT | LX | LY | RX | RY`，共 56 位。按大端拆成八组 7 位，前七字节最高位为 0，最后字节最高位置 1；无附加 CRC。

```text
中立原始状态：00 00 08 80 80 80 80
中立线路数据：00 00 01 08 04 02 01 80
A 键线路数据：00 01 01 08 04 02 01 80
```

这就是原项目中口头称为“7 字节协议”的控制器报告，真实串口传输长度为 8。没有移植 `V1.cs` 的历史编码，也没有实现烧录、版本、LED、amiibo 等设备命令。

### Pokeldn 帧

```text
A5 5A | length u16 LE（type + payload）| type | payload | checksum
checksum = type 与 payload 所有字节之和 mod 256

STATUS 查询：A5 5A 01 00 02 02
报告载荷：buttons u16 LE | HAT | LX | LY | RX | RY | vendor=0
中立报告帧：A5 5A 09 00 01 00 00 08 80 80 80 80 00 09
```

直接从通用 `Report` 编码，避免先编码成 EasyCon 再解码。接收解析器支持分片、连续帧、噪声、长度及校验失败后的重新同步，type+payload 最大 600 字节；有多个已缓冲的完整帧时调用 `next_frame()` 排空。校验错误不算成功应答，等待受事务预算约束。状态帧可穿插在报告结果之间。

报告事务不流水并发，一次只等待一个结果。协议应答没有请求序号；超时后禁止继续发送普通动作，避免把迟到应答当成下一次动作结果。故障收尾仅尽力回中，不能依赖迟到应答证明释放。

这里只实现串口手柄输入，不包含 Pokémon 游戏网络协议、BLE 连接、板载宏、下载模式或刷写固件。

## 取消与资源所有权

API 是同步阻塞接口。异步宿主应把会话放在自己管理、最终会等待结束的阻塞任务中。枚举／打开串口可能受系统驱动阻塞影响，无法承诺硬取消期限。

取消在打开前、握手读取和发送节流中检查。第一字节尚未写出时，可返回 `Cancelled`，原状态不变。一旦开始写帧，即使外部取消，也先完成剩余字节并在预算内解析对应应答，随后返回真实结果，防止截断帧或把已发送动作误报为未发生。部分写失败会明确使会话失效。

断开收尾使用独立取消状态，尝试回中后始终释放串口；`Drop` 也会收尾，但错误只能通过显式 `disconnect()` 观察。进程强制退出、物理断电和原生驱动崩溃不能由 Drop 保证释放按钮。

EasyCon 在非 Windows 打开时保持 DTR 高，打开后先撤销 RTS、再撤销 DTR；Pokeldn 请求以 DTR/RTS 低状态使用端口。串口库／驱动无法保证打开时绝无控制线毛刺；部分 CP2102 在 macOS 仍会让 ESP32 重启，Pokeldn 的启动探测预算因此与旧 .NET 实现的 900ms 不同。

## 使用与诊断

宿主仅依赖 `easycon-device`。通常调用 `connect_serial(port, token)` 自动探查；诊断和协议测试也可显式使用内置后端：

```rust
use easycon_device::EasyCon;
use easycon_device::{Button, ConnectOptions, Gamepad, GamepadBackend, Result};
use tokio_util::sync::CancellationToken;

fn one_state_update(port: &str, token: &CancellationToken) -> Result<()> {
    let mut pad: Box<dyn Gamepad> = Box::new(EasyCon.connect(port, ConnectOptions::default(), token)?);
    let action = pad.press(Button::A, token);
    let cleanup = pad.disconnect();
    cleanup.and(action.map(|_| ()))
}
```

替换为 `easycon_device::backends::pokeldn::Pokeldn.connect(...)` 即使用另一个后端。实际编排应持有同一会话，通过 `press`、`release` 和显式等待组织动作，不反复打开串口。

使用同一通用接口更新控制器状态：

```rust
use easycon_device::{Button, Gamepad, Hat, Result, Stick, StickId};
use tokio_util::sync::CancellationToken;

fn update_controls(pad: &mut dyn Gamepad, token: &CancellationToken) -> Result<()> {
    pad.press_buttons(&[Button::A, Button::ZL], token)?;
    pad.set_hat(Hat::UpRight, token)?;
    pad.set_stick(StickId::Left, Stick { x: 255, y: 128 }, token)?;
    pad.center_stick(StickId::Left, token)?;
    pad.set_hat(Hat::Center, token)?;
    pad.release_buttons(&[Button::A, Button::ZL], token)?;
    Ok(())
}
```

示例是六次独立更新；宿主负责在整个运行成功、失败或取消后释放仍持有的状态，可通过 `reset` 或 `disconnect` 收尾。单次动作取消不会自动撤销之前成功的按下操作。

在 `nextgen` 目录运行：

```sh
cargo run --locked --bin easycon-pad -- ports
cargo run --locked --bin easycon-pad -- encode --backend easycon --buttons 0x4
cargo run --locked --bin easycon-pad -- encode --backend pokeldn --buttons 0x4
cargo run --locked --bin easycon-pad -- probe --backend easycon --port '<port>'
cargo run --locked --bin easycon-pad -- probe --backend pokeldn --port '<port>'
```

`ports` 只枚举，`encode` 完全离线；`probe` 会打开端口、握手、发送中立状态并断开，可能触发板子重启。不会自动执行非中立按键动作。

Linux 增加 `libudev-dev` 构建依赖。Windows/macOS 使用系统串口 API。设备核心可单独构建，不拉入视频或音频依赖：

```sh
cargo check --locked -p easycon-device
cargo test --locked --workspace --all-targets
```

无硬件测试验证固定线路向量、全部按钮及摇杆边界、握手分片、校验／长度恢复、两后端通用按键行为、波特率回退、取消、部分写入、应答拒绝／丢失、故障封闭、节流和收尾。新增原子控制测试从实际写入假串口的数据还原报告，验证多键一次交付、重复／空列表、九种方向帽状态、左右摇杆独立更新与回中，以及取消、故障和断开后不再发送动作。尚未完成真实串口板卡及三平台硬件验收。

## 协议依据

- 本仓库 `src/EasyCon.Device/Utils/SwitchReport.cs`、`SwitchCommand.cs`：按钮位、方向帽、摇杆和现役 7-bit 编码。
- 本仓库 `Connection/TTLSerialClient.cs`、`JoyStickDevice.cs`、`NintendoSwitchPriv.cs`：握手、波特率回退、控制线和节流流程。
- 本仓库 `Connection/PokeldnSerialClient.cs`：Pokeldn 帧、状态查询和报告字段映射。
- [Pokeldn 官方 Controller board 文档](https://github.com/Decryptu/pokeldn/blob/main/docs/hardware_pad.md#the-serial-side)，核对于 2026-10-22：串口帧、结果码、打开控制线和启动探测预算。本实现依据协议描述独立编写，不依赖或搬入其固件、Python 服务和宏执行代码。
