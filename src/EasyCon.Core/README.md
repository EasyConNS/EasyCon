# EasyCon.Core 模块

## 模块概述

Core 是**能力端口 + 组合根**：向上为 GUI/CLI 提供统一的能力装配与脚本执行面，向下引用
Script/Device/Capture 三个实现库并把它俩装配成可用的宿主。

> ⚠️ 它不是"最底层的抽象层"。四个叶子库（`EasyCon.Script` / `EasyCon.Device` /
> `EasyCon.Capture` / `EzTesseract`）零工程引用且**不得**反向引用 Core。
> 完整分层图与规则见 [docs/Framework.md §2](../../docs/Framework.md) 与 `AGENTS.md`。

## 目录与职责

| 目录 / 文件 | 职责 |
|---|---|
| `Capabilities/` | **能力端口**：`CapabilitySet`（宿主 = 能力实例集合，null = 不可用）+ `IPadInput` / `IConsoleIo` / `IHostEnvironment` / `IFileSystem` / `ICaptureSource` / `IVisionService` / `IOcrService` / `IInference` |
| `Capabilities/*Adapter*.cs`、`DesktopFileSystem`、`DnnInference`、`TesseractOcrService`、`PaddleOnnxOcr` | 端口的默认/过渡实现（委托适配器、桌面文件、OpenCV DNN、两个 OCR 后端） |
| `Hosting/` | **组合根**：`ScriptHostAssembler`（全仓唯一构造 `CapabilitySet` 的地方，返回 `CapabilityLease` 负责 OCR/DNN 释放）+ `ScriptCompileProfiles`（`Desktop` / `Portable` 两个编译档位） |
| `Script/` | 脚本引擎门面：`IScriptEngine`（源码/文件 → 会话）、`IScriptSession`（诊断 + `Run(token, capabilities)`）、默认实现 `EasyScriptEngine` |
| `Runner/` | 桌面 ECX 执行桥：`EcxVm.Run/BuildHost` 把 `CapabilitySet` 编成 `EcxHost` 委托闭包并把 VM 错误码映射回 `ScriptException`；`BuiltinCallable` 为按名分发的内建/采集洞实现；`NativeLoader` 为 FFI 动态库解析 |
| `Config/` | `ConfigManager`（静态 JSON 读写：损坏先备份再降级并上报、Save 原子替换）、`ConfigState`、`KeyMappingConfig`、`GamepadMappingConfig`、`AlertConfig` + `AlertDispatcher`、`AppPaths` |
| `LLM/` | OpenAI 兼容 chat client（流式/非流式、SSE、退避重试、工具协议 DTO）、models/provider 配置、技能体系（`Skills/`）、MCP 服务器配置 |
| `Input/` | `IInputBinder`（输入后端契约；实现位于 `EasyCon.SDLInput`） |
| `IO/` | `CustomSleep`（低占用延时） |
| `Logging/` | `CoreLog` 静态汇点：叶子库把诊断交给宿主（GUI 接 LogService、CLI 接 Serilog） |
| `Services/` | `ILogService` / `IConfigService` 端口 |
| `ECCore.cs`、`GamePadAdapter.cs`、`KeyExt.cs`、`StringExtensions.cs`、`OcrDelegateFactory.cs` | 便捷门面与桥接：采集源/设备枚举、`NintendoSwitch → ICGamePad`、按键转换、`FrameDelegate` 工厂 |

## 使用方式

### 装配并运行一个脚本（宿主视角）

```csharp
IScriptEngine engine = new EasyScriptEngine();

// 1) 编译：档位只用 ScriptCompileProfiles，不要手写 CompileOptions
IScriptSession session = engine.LoadFile(path, new ScriptHostOptions
{
    Compile = ScriptCompileProfiles.Desktop(labelNames),   // 桌面解释器档
});

// 2) 装配能力：唯一装配点，宿主只提供"原料"
using CapabilityLease lease = ScriptHostAssembler.Assemble(new ScriptHostContext
{
    Pad    = hasKeyAction ? new GamePadAdapter(device, highResolution) : null,
    Console = new ConsoleIoAdapter(logService),
    Frame  = frameDelegate,          // Base64 PNG，可空
    Roi    = MatExtensions.CropBase64,
    LabelMatch = labelMatch,         // 可空
    Args   = args,
});
// 默认值由装配器补齐：Environment、Files、OCR(Tesseract)、Inference(DNN)

// 3) 执行（能力可在 Run 时覆盖编译期设置）
session.Run(cancellationToken, lease.Capabilities);
```

`CapabilityLease.Dispose()` 释放 OCR 引擎与推理会话，重复调用安全。

### 编译档位

| 档位 | 槽位 | 缓存 | 产物 |
|---|---|---|---|
| `ScriptCompileProfiles.Desktop` | PC 宽槽位 | 不落盘现编 | 只供 `EcxInterpreter`；含 `PcCode` 时 `EcxWriter`/`EcmFormat` 拒绝序列化 |
| `ScriptCompileProfiles.Portable` | 冻结 8 位槽位 | 启用 `obj/` 内容寻址缓存 | 可落 `.ecx` 交单片机 C VM（MCU 分发唯一合法档位） |

## 配置文件

JSON 存储于 `%AppData%/easycon/`（`AppPaths` 统一管理）：
`config.json`（应用配置）、`keymapping.json`（按键映射）、`alert.json`（推送通知）、
`models.json`（LLM 供应商/模型）、`mcp.json`（MCP 服务器）。

## 依赖项

- EasyCon.Device — 设备通信（HID 报文、串口协议）
- EasyCon.Capture — 采集帧、视觉匹配、OCR 引擎缓存
- EasyCon.Script — 编译管线、字节码、C# 解释器
- YamlDotNet（仅 Core 自身的 NuGet 依赖；OpenCV/OpenCvSharp 经 Capture 传递获得）

---

**版本**: 3.0（ECX 统一链路 + 组合根收敛）
