# 伊机控 EasyCon 文档中心

文档分四类。改代码前先读对应类；所有文档以**现状**为准——描述与代码冲突时，改文档或改代码，
不留"两说"。

## 📖 使用（面向脚本作者/用户）

| 文档 | 内容 |
|---|---|
| [GETTING_STARTED.md](GETTING_STARTED.md) | 安装、GUI/CLI 运行脚本、采集画面来源（含 OBS 虚拟摄像头） |
| [Script.md](Script.md) | ECS 脚本语言完整语法：变量/常量/类型/流程控制/函数/结构体/数组/按键语句/识图标签/IMPORT |
| [Functions.md](Functions.md) | 内置函数手册（按功能域；速查表含桌面/单片机可用性） |

## 🏗️ 架构与编译管线（改编译器/宿主前必读）

| 文档 | 内容 |
|---|---|
| [Framework.md](Framework.md) | 分层依赖图、组合根规则（能力装配/编译档位唯一落点）、三条主链路、已知短板 |
| [Pipeline.md](Pipeline.md) | 源码 → EcxImage 统一编译链路、链接期行为、单一事实源落点表 |
| [ModuleSystem.md](ModuleSystem.md) | 接口式独立编译、Merkle 缓存键、接口完备性清单、缓存 GC |

## ⚙️ 虚拟机与字节码（改 VM/格式/双端前必读）

| 文档 | 内容 |
|---|---|
| [VM2.md](VM2.md) | 现役 VM 规格：L0-L3 分层、值模型、指令表、宿主 ABI、调用约定 |
| [VmSemanticContract.md](VmSemanticContract.md) | C# 解释器 ↔ C VM 的 S-01..S-21 强制语义 checklist（S-21 = 能力降级缺省值表 + strict_caps 双态）+ RC 槽写协议 + 改动维护规则 |
| [EcmEcxFormat.md](EcmEcxFormat.md) | 产物容器位级规格：ECX1 平铺镜像 + ECM1 平铺模块缓存（36B 头/自计数表/全量 CRC）、加载校验、错误码 |

## 📐 设计思路与方案

| 文档 | 状态 | 内容 |
|---|---|---|
| [PROJECT_OUTLINE.md](../PROJECT_OUTLINE.md) | 📋 大纲 | 全项目现状一页图 + 决策记录（v3 定长/平铺容器、C VM 引擎、零分配、v2.3 对齐）+ 性能阶梯与四方对比定论 + 坑与门禁 + 挂账全景 |
| [McuBytecodeDelivery.md](McuBytecodeDelivery.md) | 📋 方案（未实施） | MCU 下发链路：McuImageBuilder/预检/能力查询 0x8A/四条件判定/标签不脱机硬门 |

## 📎 其他

- `架构图drawio.drawio` — 分层架构图源文件
- `../baselines/` — 产物尺寸与解释器性能基线（尺寸门 N1②、性能回退判据的对照物）

## 阅读路径

- **写脚本**：GETTING_STARTED → Script → Functions
- **改编译器**：Pipeline → ModuleSystem → VM2 → VmSemanticContract → EcmEcxFormat（＋ PROJECT_OUTLINE 决策记录）
- **改宿主/UI**：Framework → `AGENTS.md` 分层规则 → `src/EasyCon.Core/README.md`

## 维护规则

- 每份文档头部标**状态**（现状 / 已实施·含实施记录 / 方案·未实施）；改代码同步改文档。
- 格式/指令/契约改动遵循各文档内的 checklist（EcmEcxFormat 段注册表、契约维护规则、VM2 新增指令流程）。
- 历史文档不保留在 docs/（v1 设计、旧格式规格、已并入的调研均在 git 历史）。
