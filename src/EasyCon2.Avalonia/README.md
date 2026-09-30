# EasyCon2.Avalonia 模块

## 模块概述

基于 Avalonia 12 的跨平台 GUI（CommunityToolkit.Mvvm），支持 Windows、Linux 和 macOS。
本工程是 **View 宿主 + 平台服务实现层**；纯逻辑与 ViewModel 主体在
[EasyCon2.Avalonia.Core](../EasyCon2.Avalonia.Core/)（零 Avalonia 包引用，编译器强制）。

## 技术架构

- **MVVM**：`Views/`（`.axaml` + 少量 code-behind）↔ ViewModel。功能自洽目录
  （`VPad/`、`Editor/`、`Terminal/`、`TagEditor/`、`AiAgent/`、`Mcp/`、`AlertConfig/`、`ModelsConfig/`）
  的 View 在本工程、VM 在 Core 工程，但**命名空间统一为 `EasyCon2.Avalonia.Core.*`**，
  便于 axaml 的 `using:` 与 `x:DataType` 解析。
- **组合方式**：**没有 DI 容器**。对象图在 `App.axaml.cs` 的
  `OnFrameworkInitializationCompleted` 里显式 `new`（日志 → 设备 → 采集 → 脚本 → 手柄 →
  对话框/窗口 → `MainWindowViewModel`），退出时在 `desktop.Exit` 统一释放。
  本工程的服务实现落在 Core 定义的端口上（`DeviceService : IDeviceService`、
  `CaptureService : ICaptureService`、`ControllerService : IControllerService` 等）。
- **编译绑定**：`AvaloniaUseCompiledBindingsByDefault=true`，所有 axaml 绑定需 `x:DataType`。
- **View 解析是显式的**：axaml 里 `DataContext="{Binding ...}"` 或由 `WindowService` 构造。
  `ViewLocator` 已注册但几乎不匹配任何类型，属遗留死代码，不要依赖 `FooViewModel → FooView` 命名映射。
- **能力装配不在本工程**：`CapabilitySet` 只由 `EasyCon.Core.Hosting.ScriptHostAssembler` 构造，
  GUI 侧只提供原料（见 [EasyCon.Core/README.md](../EasyCon.Core/README.md)）。

## 目录

| 目录 | 内容 |
|---|---|
| `Views/` | 窗口与大页面（MainWindow、FileTreeView、MonitorView、各配置窗口、`Shared/`） |
| `ViewModels/` | **带 Avalonia 渲染类型的 VM**（MainWindow/FileTree/Monitor/ESPConfig/KeyMapping/ControllerConnection/ViewModelBase） |
| `Services/` | 平台服务实现（DeviceService、CaptureService、ControllerService/Mock、DialogService、WindowService、ThemeManager、LogService 适配、UiPreloader、SkiaImageProcessor） |
| `VPad/` | 虚拟手柄悬浮窗与绘制控件 |
| `Editor/`、`Terminal/`、`TagEditor/`、`AiAgent/`、`Mcp/`、`AlertConfig/`、`ModelsConfig/` | 各功能域的 View 与控件 |
| `Controls/`、`Converters/`、`Behaviors/`、`Markup/`、`Resources/` | 控件、转换器、行为、本地化标记扩展与资源 |

## 主要功能

- 设备（单片机）连接与管理、固件版本查询
- 脚本编辑（AvaloniaEdit + LSP 高亮/补全）、执行、日志控制台
- 视频源连接与画面监视（OpenCV `Mat` → `WriteableBitmap`）
- 图像标签编辑器、按键映射配置窗口、VPad 虚拟手柄
- AI Agent 会话面板（工具调用驱动脚本/截图）、MCP 服务器与模型供应商配置

## 相关模块

- [EasyCon2.Avalonia.Core](../EasyCon2.Avalonia.Core/) — 纯 VM/逻辑层（本工程的主要依赖）
- [EasyCon.Core](../EasyCon.Core/) — 能力端口 + 组合根 + 脚本执行桥
- [EasyCon.Device](../EasyCon.Device/) — 设备通信
- [EasyCon.Capture](../EasyCon.Capture/) — 图像采集与识别
- [EasyCon.Script](../EasyCon.Script/) — 脚本编译与解释
- [EasyCon.SDLInput](../EasyCon.SDLInput/) — 键盘/手柄输入后端（`VPad/` 与 `ControllerService` 消费）
- 虚拟手柄控件位于本工程 `VPad/` 目录（早年的独立 `EasyCon.VPad` 工程已删除）

---

**版本**: 3.0（组合根收敛后同步）
