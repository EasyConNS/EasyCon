# MCU 字节码下发通道 —— 完整实现方案

> **状态**：方案（未实施）。本文是接回「桌面编译 → 单片机执行」这条产品核心链路的实施依据。
> 关联：[Pipeline.md](Pipeline.md)（编译链路）、[EcmEcxFormat.md](EcmEcxFormat.md)（ECX 布局与校验）、
> [VM2.md](VM2.md)（宿主 ABI 与特征位）、[VmSemanticContract.md](VmSemanticContract.md)（双端语义）、
> [Framework.md](Framework.md) §5.2（编译档位）。

---

## 1. 背景与目标

EasyCon 的产品宣传语是"编译一次，桌面解释器与单片机执行同一份字节码"。这句话在 `EcxImage`
对象层成立，在**产物与传输层不成立**——链路有三个断点（§2）。本文给出接回方案。

### 1.1 目标形态

| 用户故事 | 验收标准 |
|---|---|
| GUI 一键下发 | 编辑器里有脚本 + 单片机已连接 → 点一次「编译并烧录」→ 脚本在真机跑起来；失败时给出可操作的单一原因 |
| 脚本超容量时不被静默截断 | 产出超限时在**编译期或预检期**报错，并指出是槽位/深度/镜像尺寸/特征位中的哪一项 |
| 不匹配的固件/镜像不写入设备 | 固件协议版本、ECX `format_ver`、syscall ABI 版本、镜像特征位四项任一不匹配 → 拒绝烧录且不动设备 |
| CLI 可脚本化 | `ezcon compile --target mcu -o x.ecx` / `ezcon flash x.ecx` / `ezcon run --flash x.ecs` 三条命令可组合进 CI |
| 失败可归因 | 传输层重试与失败有日志与计数；烧录不完整不得返回成功 |

### 1.2 非目标

- 不改 ECX 二进制格式（`format_ver=2` 保持冻结；格式演进见 §8 P3）。
- 不做无线/网络烧录（现有链路是串口）。
- 不改单片机固件的运行语义（本文只定义**需要固件配合的最小协议增量**，见 §4.3）。

---

## 2. 现状：三个断点

### 断点 1 — 桌面档产物在结构上不可序列化

GUI 与 CLI 的运行路径都启用 PC 专用宽槽位指令：

- `src/EasyCon2.Avalonia.Core/Services/ScriptService.cs`（`ScriptCompileProfiles.Desktop`，`EnablePcWideSlots = true`）
- `src/EasyCon2.CLI/Program.cs` 的 `run` / `format` / `ir` 同样用 `Desktop` 档

而 `EcxWriter.Write` 与 `EcmFormat.Write` 会**主动拒绝**含 `PcCode` 的镜像：

```csharp
// src/EasyCon.Script/Bytecode/EcxWriter.cs
if (image.Functions.Any(f => f.PcCode != null))
    throw new BytecodeException(... "PC 宽槽位指令不能写入 ECX（单片机仅支持冻结的 8 位槽位格式）" ...);
```

即"桌面同名档产物 → 落 .ecx"这条路径不存在，且不是遗漏而是刻意设计。
`ProjectCompiler` 也据此只在**窄槽**档启用 `obj/` 磁盘缓存（`ProjectCompiler.cs:105`），
`ModuleCompilePipeline` 只在窄槽档启用进程缓存（`:80`、`:124`）。

### 断点 2 — GUI 的产物入口已废弃

```csharp
// src/EasyCon2.Avalonia.Core/Services/ScriptService.cs
public Task<byte[]> BuildAsync(bool autoRun)
{
    // v1 Assemble 已随 IRunner 移除（P2）；ECX 产物请用 CLI compile
    throw new NotImplementedException("v1 Assemble 已移除；请用 CLI compile 产出 .ecx");
}
```

上层唯一消费者是 `FlashFirmwareViewModel.CompileFlashAsync`：它捕获异常后写一句
"编译失败"日志、拿到空数组后写"编译为空"日志返回。**GUI 的「编译并烧录」按钮必然失败，且失败原因
是不可操作的实现细节。**

### 断点 3 — 没有自动传输通道

- 串口烧录协议**在**：`NintendoSwitch.Flash(byte[])`（20 字节分包 + `FlashStart`/`FlashEnd` 握手 + 重试）、
  `EzDvCommand.Flash = 0x82`、`Reply.FlashStart/FlashEnd`、`DeviceService.Flash` 均已就绪。
- 唯一能产出可序列化 `.ecx` 的是 CLI `compile`（`ProgramCompiler.CompileProject` + `EcxWriter.Write`），
  之后只能**人工**执行 `ecs-vm run image.ecx`（那是桌面参考宿主，不是单片机）。

**结论**：缺的不是协议，而是"①一个便携档产物入口 ②一层兼容性预检 ③一条从产物到串口的自动通道"。

---

## 3. 依赖技术事实（方案必须遵守的既有约束）

| 事实 | 位置 | 影响 |
|---|---|---|
| ECX 定长头 0x24：`max_slots:u8@0x08`、`max_depth:u8@0x09`、`feats:u16@0x0A` | `EcmEcxFormat.md §2`、`EcxWriter.cs` | 预检可直接读 `EcxImage.MaxSlots/MaxDepth/Features`，无需解析字节 |
| 槽位是 `u8`：`max_slots > 255` 或任一函数 `NSlots > 255` → `EcxWriter` 抛错 | `EcxWriter.cs` | 便携档已强制，无需新增校验；但要给出**可操作**的超限提示 |
| 特征位 `EcsImageFeatures`：`Il=0x1 / Capture=0x2 / Ffi=0x4 / File=0x8 / Vision=0x10` | `EcsOpcode.cs` | 镜像声明"我需要什么"，加载期由 MCU 校验 |
| C 侧 `ECS_FEAT_IL/CAPTURE/FFI/FILE`，**无 VISION**；参考宿主只声明 `ECS_FEAT_FILE` | `ecs_vm.h:55-58`、`ecs_main.c:236` | 含 `Vision` 的镜像在参考宿主上加载即拒（`ECS_ERR_FEAT`）；真实固件的 feats 目前**无法查询** |
| 加载期静态校验器（槽位/常量/全局/结构/原生索引/Jmp 目标/EXT 槽） | `ecs_vm.c` `validate_code` | 坏镜像在设备侧会被拒；报错是 `ECS_ERR_SLOT/OPCODE/IMAGE`，需映射为用户可读文本 |
| 固件协议版本常量 `0x45`，`GetVersion()` 读回 `[0x40, 0x80]` 区间字节 | `FlashFirmwareViewModel.cs`、`NintendoSwitchCmd.cs` | 已有握手，可扩展 |
| 远程脚本控制已存在：`RemoteStart()`/`RemoteStop()`（`0x83`/`0x84`，等 `Reply.ScriptAck=0x83`） | `NintendoSwitchCmd.cs` | 烧录后可一键启动，无需新增固件能力 |
| `ResetControl()` = 发 `0xA5 0x81`(Hello) 并等 `Reply.Hello` | 同上 | 语义是"重新取得控制权"，不是硬件复位；重试路径已用它 |
| 烧录分包 20 字节；偏移与长度各按 **7 + 8 位**编码；失败重试前 `ResetControl()` | `NintendoSwitchCmd.cs Flash()` | 偏移可表示上限 = `(255<<7)|127 = 32767` 字节；**当前无上限校验，超限会静默回绕** |

---

## 4. 设计

### 4.0 总体数据流

```
脚本文件 ──► McuImageBuilder.Build（Portable 档编译 + 链接）
                 │
                 ├─ 产物元数据：EcxImage（MaxSlots/MaxDepth/Features/函数数）
                 ├─ 序列化：EcxWriter.Write(image, stripDebug: true)  → byte[]
                 └─ 预检：Preflight(report, target)  → McuPreflightResult
                        │
                        ▼ 通过
                 IMcuFlasher.FlashAsync(bytes, progress, ct)
                        │  ① 固件协议版本握手（已存在）
                        │  ② （可选）设备能力查询（新增命令，见 §4.3）
                        │  ③ 分片写入 + 重试 + 计数
                        ▼
                 设备已就绪 ──（可选）RemoteStart()
```

### 4.1 D1 — 单一产物入口 `McuImageBuilder`

新增 `src/EasyCon.Core/Hosting/McuImageBuilder.cs`（与 `ScriptHostAssembler` 同属组合根，
保证"便携档产物"只有一处产出）：

```csharp
namespace EasyCon.Core.Hosting;

/// <summary>MCU 分发产物：可序列化镜像 + 预检所需的全部元数据。</summary>
public sealed class McuImage
{
    public required byte[] Bytes { get; init; }          // 已序列化（stripDebug: true）
    public required int MaxSlots { get; init; }
    public required int MaxDepth { get; init; }
    public required uint Features { get; init; }         // EcsImageFeatures 掩码
    public required int FunctionCount { get; init; }
    public required bool KeyAction { get; init; }
    public required bool NeedIL { get; init; }
    public required IReadOnlyList<string> ModuleChain { get; init; }
}

public sealed class McuImageBuildResult
{
    public McuImage? Image { get; init; }                // null = 编译失败
    public required IReadOnlyList<Diagnostic> Diagnostics { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
}

public static class McuImageBuilder
{
    /// <summary>便携档编译 + 链接 + 序列化。这是产生可下发镜像的唯一入口。</summary>
    public static McuImageBuildResult Build(
        string mainPath,
        IEnumerable<string>? extVars = null,
        bool stripDebug = true);
}
```

实现要点：

1. **必须走 `ScriptCompileProfiles.Portable`**（`EnablePcWideSlots = false`）。
   这同时解锁 `obj/` 内容寻址缓存与进程缓存，重复编译零成本。
2. `extVars` **必须包含识图标签名**。当前 `ezcon compile` 没有加载标签，含 `@标签` 的脚本
   在绑定期就失败——这是接回链路的必要修复（已在 §7 P0 列出）。
3. 序列化用 `stripDebug: true`：剥离调试区（函数名表/全局名表），MCU 发布镜像零名字重量。
   调试用 `.ecx`（`stripDebug: false`）仅作为可选的 `--keep-debug` 输出。
4. 单一实现意味着 GUI / CLI / 未来任何宿主共用同一条产物路径，不再可能各自漂移。

### 4.2 D2 — 预检 `McuPreflight`

预检是**纯函数**（便于单测），输入 = 产物元数据 + 目标能力，输出 = 可枚举的问题列表：

```csharp
public sealed record McuTargetProfile(
    uint SupportedFeatures,      // 设备上报（或保守缺省）
    int MaxSlots,                // 设备帧区槽位上限
    int MaxDepth,                // 设备调用深度上限
    int MaxImageBytes,           // 设备端镜像分区大小
    ushort FormatVersion,        // 设备支持的 ECX format_ver
    int AbiRevision);            // 设备支持的 syscall ABI 修订号

public enum McuPreflightCode
{
    Ok,
    ImageTooLarge,          // Bytes.Length > MaxImageBytes 或 > 传输协议上限
    SlotsExceeded,          // MaxSlots > target.MaxSlots
    DepthExceeded,          // MaxDepth > target.MaxDepth
    FeatureUnsupported,     // Features & ~SupportedFeatures != 0（逐位报告）
    FormatVersionMismatch,
    AbiRevisionMismatch,
    TransportOffsetOverflow // Bytes.Length > 32767（协议偏移位宽，见 §4.4）
}

public sealed record McuPreflightResult(IReadOnlyList<(McuPreflightCode Code, string Message)> Problems)
{
    public bool Ok => Problems.Count == 0;
}

public static class McuPreflight
{
    public const int TransportMaxImageBytes = 32 * 1024;   // 7+8 位偏移编码的硬上限

    public static McuPreflightResult Check(McuImage image, McuTargetProfile target);
}
```

`FeatureUnsupported` 必须**逐位**给原因（"脚本使用了图像标签（@xxx）与 FFI 原生库，而设备固件不支持"
远比"特征位不匹配 0x5"可操作）。

### 4.3 D3 — 设备能力查询（需要固件配合的最小增量）

预检需要 `McuTargetProfile`，而当前**无法从设备查询**——`GetVersion()` 只回一个版本字节。
新增一条命令（沿用既有帧格式 `0xA5 <cmd> [payload]`）：

| 方向 | 字节 | 含义 |
|---|---|---|
| 主机 → 设备 | `A5 8A` | `CapabilityQuery` |
| 设备 → 主机 | `A5 8A` + `feats:u32` + `max_slots:u16` + `max_depth:u16` + `image_capacity:u32` + `format_ver:u16` + `abi_rev:u16` | `Reply.Capability`（小端，共 18 字节） |

**兼容回退（必须实现）**：老固件不认 `0x8A`，不会应答。超时后按保守缺省档处理：

```csharp
// 老固件 / 无应答时的保守缺省：只允许"不需要任何高级能力"的镜像
static readonly McuTargetProfile ConservativeDefault = new(
    SupportedFeatures: 0,          // 不含 IL/CAPTURE/FFI/FILE/VISION
    MaxSlots: 64, MaxDepth: 16,
    MaxImageBytes: 16 * 1024,
    FormatVersion: 2, AbiRevision: 1);
```

并在日志与 UI 明确提示"固件未上报能力，按保守能力估算；若脚本使用图像识别请升级固件"。
保守档的具体数值应做成配置文件项（`config.json`），便于不同固件批次调整而不改代码。

> 为什么不做成"跳过预检直接烧"：MCU 侧的加载期校验虽然能挡住坏镜像，
> 但报错是 `ECS_ERR_FEAT` 这类码，用户拿到的是"设备没反应"。预检的价值在于**在烧之前**说清原因。

### 4.4 D4 — 传输层

**复用现有 `NintendoSwitch.Flash(byte[])`**，但要做三件事：

1. **新增显式上限校验**。当前偏移按 `(byte)(i & 0x7F), (byte)(i >> 7)` 写入，超 32767 字节会**静默回绕**，
   导致镜像被写到错误偏移。在 `Flash` 入口加：

   ```csharp
   public const int MaxImageBytes = (255 << 7) | 127;   // 32767：7+8 位偏移编码上限
   if (asmBytes.Length > MaxImageBytes) return false;   // 或抛 McuDeliveryException
   ```

2. **可观测与可取消**：`Flash` 目前是同步阻塞、无进度、无取消。新增
   `FlashAsync(byte[] bytes, IProgress<FlashProgress>? progress, CancellationToken ct)`，
   内部沿用同一分包/重试逻辑；`Flash` 保留为薄封装（同步 API 的既有调用点不变）。
   `FlashProgress(offset, total, retryCount)` 让 GUI 能显示进度、CLI 能在 `--verbose` 下打点。

3. **失败即失败**：重试次数上限（`MaxRetriesPerPacket`，缺省 3）用尽后返回 `FlashResult` 结构
   （`Success` / `RetriesExhausted(offset)` / `ControlLost(offset)` / `TooLarge`），
   而不是 `bool`——让调用方能区分"设备没响应"和"镜像太大"。

**协议上限表**（写进代码注释与本文，二者互为锚点）：

| 量 | 值 | 来源 |
|---|---|---|
| 单包净荷 | 20 字节 | `NintendoSwitchCmd.Flash` `PacketSize` |
| 偏移可表示范围 | 0..32767 | 7+8 位编码 |
| 单包握手超时 | 1000 ms | `SendSync` 调用点 |
| 重试前动作 | `ResetControl()`（重新 Hello 握手） | `Flash` 重试分支 |

### 4.5 D5 — 烧录后启动

`RemoteStart()` / `RemoteStop()` 已可用，直接编排：

```
Flash → ResetControl（取得控制权）→ RemoteStart → 等待 Reply.ScriptAck
```

GUI 提供"烧录并运行"勾选（默认开）；CLI 提供 `ezcon run --flash`。为避免用户在 MCU 上
留下"看不见的运行中脚本"，**烧录前若检测到脚本在运行，先 `RemoteStop`**。

### 4.6 D6 — 宿主接口

**GUI**（恢复 `BuildAsync` 的语义，改为返回 `McuImage?` 而不是 `byte[]`）：

```
FlashFirmwareViewModel.CompileFlashAsync
  ├─ McuImageBuilder.Build(editorText 或 scriptPath)      → 诊断上日志（复用 CompileCore 的格式化）
  ├─ McuPreflight.Check(image, targetProfile)             → 失败：逐条日志 + 中止
  ├─ McuTargetProfile：设备能力查询（超时回落保守档）
  ├─ FlashAsync(bytes, progress, ct)                      → 进度条 + 取消
  └─ 可选 RemoteStart
```

同时把 `FirmwareProtocolVersion = 0x45` 的硬编码判定改为"预检的一项"（`FormatVersionMismatch`/
`AbiRevisionMismatch`），失败文案统一由预检产出。

**CLI**：

| 命令 | 行为 |
|---|---|
| `ezcon compile <file> [-o out.ecx] [--keep-debug] [--json]` | 便携档编译并落 `.ecx`（已有雏形；补标签加载与 `--target mcu` 显式化） |
| `ezcon flash <file.ecx> [-p COM] [--verify] [--run]` | 读镜像 → 预检（无设备能力时用保守档）→ 烧录 → 可选启动；退出码见 §5 |
| `ezcon run <file> --flash` | `compile` + `flash` + `run` 的等价串联 |
| `ezcon image <file.ecx>`（可选） | 反汇编/头部信息（`EcxDisassembler` 已有），便于不接设备时排查产物 |

### 4.7 D7 — 缓存与产物纪律

- **只有 `Portable` 档进缓存**（现有 `ProjectCompiler.cs:105` 的门已正确，不要放宽）。
- `Portable` 档的 `ProductFingerprint()` 含 `W=0`，与桌面档天然不同键，不存在串档。
- 用户可见产物（`-o out.ecx`）写到用户指定路径；`obj/*.ecm` 是内部缓存，不宣称可分发。
- 若将来引入格式版本 3，必须同步升 `format_ver` 并让旧缓存自然失效（`EcmEcxFormat.md §5`）。

### 4.8 D8 — 退出码

| 码 | 含义 |
|---|---|
| 0 | 成功 |
| 1 | 编译失败（诊断已打印） |
| 2 | 预检失败（原因已打印，可 grep 关键字） |
| 3 | 设备不可用 / 固件版本不匹配 |
| 4 | 传输失败（重试耗尽 / 控制权丢失） |
| 130 | 用户取消（SIGINT 约定，与 `run` 现状一致） |

---

## 5. 分阶段实施计划

### P0 — 恢复 GUI 一键烧录（0.5–1 人日，无固件改动）

| # | 改动 | 文件 | 验收 |
|---|---|---|---|
| 1 | 新增 `McuImageBuilder`（便携档编译 + 序列化 + 元数据） | `src/EasyCon.Core/Hosting/McuImageBuilder.cs` | 对 `examples/` 全部脚本可产出镜像 |
| 2 | `ScriptService.BuildAsync` 改为调用 `McuImageBuilder`（去掉 `NotImplementedException`） | `ScriptService.cs` | GUI 点「编译并烧录」不再必然失败 |
| 3 | `ezcon compile` 加载识图标签并显式用 Portable 档 | `src/EasyCon2.CLI/Program.cs` | 含 `@标签` 的脚本可 `compile` 成功 |
| 4 | 预检（尺寸/槽位/深度/特征位）接入 GUI 与 `flash` | `McuPreflight` + 两个宿主 | 超限脚本报出具体项而非"编译为空" |
| 5 | `Flash` 新增 32767 字节上限守卫 | `NintendoSwitchCmd.cs` | 超限镜像被拒（新增单测） |
| 6 | 新增 `ezcon flash` 子命令 | `Program.cs` | 可脚本化烧录 |

**P0 完成即"链路接回"**：GUI 与 CLI 都能编译出便携档镜像并写入设备；固件无需任何改动。
设备能力取保守缺省 + 用户可见提示。

### P1 — 能力协商与传输健壮性（1–2 人日，需固件配合）

| # | 改动 | 验收 |
|---|---|---|
| 1 | 固件实现 `CapabilityQuery(0x8A)`，回 18 字节能力块 | 设备上报 feats/槽位/深度/容量/格式版本/ABI 修订 |
| 2 | C# 侧 `QueryCapability()` + 超时回落保守档 | 老固件走保守档并提示升级 |
| 3 | `FlashAsync`（进度 + 取消 + 结构化 `FlashResult`） | GUI 进度条可见、可取消；CLI `--verbose` 打点 |
| 4 | 预检用真实能力值替换保守档；`FirmwareProtocolVersion` 硬编码迁移进预检 | 版本不匹配的文案统一 |
| 5 | 烧录前 `RemoteStop` 清理在跑的脚本 | 不会在 MCU 上留隐藏运行态 |

### P2 — 一键闭环与体验（1 人日）

| # | 改动 | 验收 |
|---|---|---|
| 1 | 烧录后自动 `RemoteStart`（可关） | 一键"编译-烧录-运行" |
| 2 | 镜像信息面板（函数数/槽位/深度/特征位/字节数/模块链） | 用户在烧前能看到"要下发的到底是什么" |
| 3 | 超限的可操作建议（如何降槽位/拆模块） | 报错含具体函数名与建议 |
| 4 | `ezcon image` 反汇编查看 | 不接设备也能排查产物 |

### P3 — 产物完整性与格式演进（需双端协商）

沿用 `EcmEcxFormat.md §5.1` 的 v2 窗口：

| # | 改动 | 前置 |
|---|---|---|
| 1 | 镜像尾部 CRC-32 + 新错误码 `ECS_ERR_CRC` | 双端同步；`format_ver` 升版本 |
| 2 | `ezcon flash --verify` 回读校验 | 需要固件支持回读命令 |
| 3 | 调用约定寄存器化（降镜像体积，等价于放宽容量） | 双端 ABI 同步 |

---

## 6. 测试策略

**可机械化的门禁（必须进 CI，否则通道会再次静默腐烂）**

| 测试 | 断言 | 落点 |
|---|---|---|
| `PortableProfile_AllExamplesSerializable` | 对 `examples/` 与 `Bytecode/corpus/` 每个 `.ecs` 用 Portable 档编译 → `EcxWriter.Write` 不抛且 `EcmFormat.Write/Read` 往返一致 | `test/EasyCon.Tests/Bytecode/` |
| `PortableProfile_RejectsPcWideSlots` | 构造超 255 槽位脚本 → 便携档编译**响亮失败**，桌面档成功 | 同上 |
| `McuPreflight_*` | 尺寸/槽位/深度/特征位/版本/ABI 六类问题各一条用例；`Ok` 路径一条 | 新建 `test/EasyCon.Tests/Hosting/McuPreflightTests.cs` |
| `Flash_SplitsInto20BytePackets` | 假连接记录分片：offset/len 编码正确、末片长度正确、无重叠无空洞 | `test/EasyCon.SDLInput.Tests`（复用 `FakeConnection` 模式） |
| `Flash_RejectsOversizedImage` | `> 32767` 字节被拒（不得静默回绕） | 同上 |
| `Flash_RetriesThenFails` | 设备不应答时重试达上限后返回 `RetriesExhausted(offset)` | 同上 |
| `McuImageBuilder_RequiresPortableProfile` | 产物不带 `PcCode`（结构性保证，防止有人改回桌面档） | 新建 |

**对照物**：`ecs-vm run out.ecx` 是真机语义的桌面参考实现。每个 P0/P1 产物都应能被
`ecs-vm run` 正常执行——这把"编译产出的镜像"与"双端对拍基线"绑在一起，
避免出现"能烧但跑不对"的产物。

**手工验收（有硬件时）**：`examples/光速过帧v1.4精准版.txt`（604 B 级）与
`nqueens_bitwise.ecs` 各烧一次并 RemoteStart；确认按键/打印行为与桌面一致。

---

## 7. 风险与未决问题

| # | 风险 | 影响 | 缓解 |
|---|---|---|---|
| R1 | 真实固件的 `feats` 未知，保守档可能过严（把能跑的脚本拦下） | 用户困惑 | 保守档做成配置项；P1 用能力查询替换；提示文案指向"升级固件" |
| R2 | Flash 偏移位宽 15 位是否与固件解析一致（固件可能只解低 14 位） | 大镜像写错位置 | 先与固件作者确认位宽；P0 的上限守卫取保守值 16 KiB，确认后放宽到 32767 |
| R3 | MCU 帧区容量（`max_slots`/`max_depth`）无文档 | 小脚本也可能 OOM | 能力查询上报；预检比对；加载期已有 `nslots ≤ max_slots` 校验兜底 |
| R4 | 便携档 255 槽位限制对复杂脚本不可达 | 功能上限 | 报错给可操作建议（拆模块/降局部变量）；长期靠 P3 调用约定寄存器化 |
| R5 | 烧录是同步阻塞串口 I/O，可能数分钟 | UI 卡死 | P0 保持后台线程（现状已如此）；P1 提供 `FlashAsync` + 进度 + 取消 |
| R6 | 无设备时的开发体验 | 无法验证 | `--port mock` 已有虚拟单片机；烧录路径用假连接单测覆盖 |
| R7 | `obj/*.ecm` 与用户 `.ecx` 混淆 | 用户分发内部缓存 | `compile` 输出提示区分；文档写明 `.ecm` 是内部缓存 |

**未决（需产品/固件决策）**

1. 能力查询命令号 `0x8A` 与 18 字节布局是否被固件接受？若不接受，是否有替代（例如复用 `Version` 回多字节）？
2. 是否需要在 MCU 侧提供"回读镜像 + CRC"（P3）？
3. 便携档产物的默认 `stripDebug` 是否应暴露给最终用户（体积 vs 可调试性）？

---

## 8. 附录

### A. 与既有文档的关系

| 文档 | 关系 |
|---|---|
| `Pipeline.md §5` | 其中"GUI 一键产 `.ecx`：经 `IScriptSession.Info.Image` → `EcxWriter`（CLI compile 已可用）"是本文 P0 的**更朴素版本**。本文修正了它的一个隐含前提：`Info.Image` 在桌面档下**不可序列化**，必须重新以 Portable 档编译，不能直接复用桌面会话的镜像 |
| `EcmEcxFormat.md §2.3` | 加载校验清单是 MCU 侧的最后一道防线；本文的预检是**烧录前**的第一道防线，二者互补 |
| `VM2.md §7` | 宿主 ABI 分层（L0–L3）决定特征位语义；L3（采集洞/FFI/ENCODE/JQ/NET_*）在 MCU 上缺位 → 含这些的镜像必须被预检拦下 |
| `Framework.md §5.2` | 两大编译档位；本文是 `Portable` 档的第一份消费者 |

### B. 一页速查

```
编译：  McuImageBuilder.Build(portable)  →  EcxWriter.Write(stripDebug: true)
预检：  McuPreflight.Check(image, targetProfile)   六类问题：尺寸/槽位/深度/特征位/格式/ABI
传输：  NintendoSwitch.FlashAsync  →  20B/包 + FlashStart/FlashEnd 握手 + ResetControl 重试
启动：  RemoteStart（0x83）→ 等 Reply.ScriptAck（0x83）
上限：  单包 20B；偏移 ≤ 32767；槽位 ≤ 255；深度 ≤ 255；特征位 L3 在 MCU 缺位
```
