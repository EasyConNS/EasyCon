# 伊机控 EasyCon 文档中心

欢迎使用伊机控文档系统！这里包含项目的完整技术文档。

> 文档分两档：**现状规范**（与代码逐条对齐，可以照着改代码）与 **历史设计**（v1 时代或已失效，
> 只能当背景阅读）。改代码前请以现状规范 + 代码为准。

## 📚 现状规范（可信）

### 核心
- **[系统架构](Framework.md)** — 分层、依赖方向、组合根、模块职责、已知短板
- **[统一编译链路](Pipeline.md)** — 源码到 EcxImage 的完整阶段与单一事实源落点表
- **[模块系统](ModuleSystem.md)** — 接口式独立编译、Merkle 式缓存键、接口完备性清单
- **[脚本语法](Script.md)** — ECS 脚本语言完整语法说明
- **[函数手册](Functions.md)** — 面向脚本作者的内置函数说明（CN）

### 虚拟机与字节码
- **[V2 指令集](VM2.md)** — 现役虚拟机规格：值模型、指令表、宿主 ABI、分层（L0-L3）
- **[双端语义契约](VmSemanticContract.md)** — C# 解释器 ↔ C VM 的 S-01..S-19 强制 checklist 与 RC 协议
- **[二进制格式](EcmEcxFormat.md)** — ECM/ECX 位级布局、加载校验清单、版本演进
- **[MCU 产物下发](McuBytecodeDelivery.md)** — 编译为 .ecx 并烧录到单片机的完整实现方案

### 使用
- **[快速开始](GETTING_STARTED.md)** — 安装配置和使用教程

### 模块实现
- **[EasyCon.Core](../src/EasyCon.Core/README.md)** — 能力端口 + 组合根 + 执行桥（含装配示例）
- **[EasyCon.Device](../src/EasyCon.Device/README.md)** — 设备通信模块
- **[EasyCon.Capture](../src/EasyCon.Capture/README.md)** — 图像处理模块
- **[EasyCon.Script](../src/EasyCon.Script/README.md)** — 脚本解析模块
- **[EasyCon2.Avalonia](../src/EasyCon2.Avalonia/README.md)** — GUI 宿主（VPad 虚拟手柄位于其 `VPad/` 目录）

## 🗂 历史设计（勿作为实现依据）

| 文档 | 说明 |
|---|---|
| [V1 指令集](VM1.md) | 单片机端旧指令集，仅有外链记录；代码侧仅剩死代码（`Script/Assembly/`、`Device/V1.cs`） |
| [模块化设计](models.md) | v1 时代的模块编号/FFI 设计，与现役 `EcsSyscall`（1..17 扁平编号）不符 |
| [模块详细设计](MODULE_DESIGN.md) | v1 时代按模块拆的详细设计（含已迁入 GUI 的 VPad、SDL2 时期的输入层） |
| [项目架构设计文档](DESIGN_DOCUMENT.md) | EasyCon2 早期分层/MVVM/事件传递设计，部分与现状一致但整体待重写 |
| [EasyCon Assist](EasyCon%20Assist.md) | 远程助手功能说明；对应实现已在 ECX 收敛中移除 |
| [OBS 虚拟摄像头](obs-virtual-cam.md) | 外部工具配置说明，与主仓代码无关 |
| [架构审查报告](../ARCHITECTURE_REVIEW_REPORT.md) | 2026-09-26 的 94 条审查发现。P0/P1 大部分已在 `ac7e16c` 落地，**按历史清单读** |

## 📖 阅读建议

**新手路径**：系统架构 → 脚本语法 → 函数手册 → 快速开始

**改编译器/字节码**：Pipeline.md → ModuleSystem.md → VM2.md → VmSemanticContract.md → EcmEcxFormat.md

**改宿主/UI**：Framework.md §5-6 → `AGENTS.md` 的分层与组合根规则 → EasyCon.Core README

---

**最后更新**: 2026-09（随组合根收敛与 MCU 下发方案同步）
