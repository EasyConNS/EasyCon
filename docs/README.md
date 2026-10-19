# 伊机控 EasyCon 文档中心

文档分四类。改代码前先读对应类；所有文档以**现状**为准——描述与代码冲突时，改文档或改代码，
不留"两说"。

## 📖 使用（面向脚本作者/用户）

| 文档 | 内容 |
|---|---|
| [GETTING_STARTED.md](GETTING_STARTED.md) | 安装、GUI/CLI 运行脚本、采集画面来源（含 OBS 虚拟摄像头） |
| [Script.md](Script.md) | ECS 脚本语言完整语法：变量/常量/类型/流程控制/函数/结构体/数组/按键语句/识图标签/IMPORT |
| [Functions.md](Functions.md) | 内置函数手册（按功能域；速查表含桌面/单片机可用性） |
| [AudioSource.md](AudioSource.md) | 连接 Windows 游戏音频输入、查看电平及连接排查 |
| [AudioLabels.md](AudioLabels.md) | 声音标签制作、ECS 目录、LOAD/SCORE/UNLOAD 后台识别示例、0～100 整数分数校准与排错 |

## 🏗️ 架构与编译管线（改编译器/宿主前必读）

| 文档 | 内容 |
|---|---|
| [Framework.md](Framework.md) | 分层依赖图、组合根规则（能力装配/编译档位唯一落点）、三条主链路、已知短板 |
| [Pipeline.md](Pipeline.md) | 源码 → EcxImage 统一编译链路、链接期行为、单一事实源落点表 |
| [ModuleSystem.md](ModuleSystem.md) | 接口式独立编译、Merkle 缓存键、接口完备性清单、缓存 GC |
| [Flow.md](Flow.md) | 节点编排（`*.flow.json`）：图格式、节点目录与语义、执行模型（看门狗/计时/停止/慢感知）、HTTP + MCP 接口、Python 画布（`frontend/`）、已知挂账 |

## ⚙️ 虚拟机与字节码（改 VM/格式/双端前必读）

| 文档 | 内容 |
|---|---|
| [VM2.md](VM2.md) | 现役 VM 规格：L0-L3 分层、值模型、指令表、宿主 ABI、调用约定、能力降级（§6.3）。双端语义一致性由 corpus/FullChain/ABI 对拍测试锁定（`test/EasyCon.Tests/Bytecode/`），产物容器位级规格见 `EcsContainer`/链接器测试 |

## 📐 设计思路与方案

| 文档 | 状态 | 内容 |
|---|---|---|
| [McuBytecodeDelivery.md](McuBytecodeDelivery.md) | 📋 方案（未实施） | MCU 下发链路：McuImageBuilder/预检/能力查询 0x8A/四条件判定/标签不脱机硬门 |

## 📎 其他

- `架构图drawio.drawio` — 分层架构图源文件
- `../baselines/` — 产物尺寸与解释器性能基线（尺寸门 N1②、性能回退判据的对照物）

## 阅读路径

- **写脚本**：GETTING_STARTED → Script → Functions
- **改编译器/VM**：Pipeline → ModuleSystem → VM2（双端语义以对拍测试为准绳，见 `test/EasyCon.Tests/Bytecode/`）
- **改宿主/UI**：Framework → `AGENTS.md` 分层规则 → `src/EasyCon.Core/README.md`
- **做编排/画布**：Flow（＋ `frontend/README.md`；改节点必须同时改 `FlowNodeCatalog` 与 `FlowNodeRuntime`）

## 维护规则

- 每份文档头部标**状态**（现状 / 已实施·含实施记录 / 方案·未实施）；改代码同步改文档。
- 格式/指令/契约改动遵循各文档内的 checklist（VM2 新增指令流程；ABI/语义以对拍测试期望为准）。
- 历史文档不保留在 docs/（v1 设计、旧格式规格、已并入的调研均在 git 历史）。