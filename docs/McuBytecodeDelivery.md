# MCU 字节码下发通道 —— 方案

> **状态：方案（未实施）**。这是唯一保留的未实施方案：接回「桌面编译 → 单片机执行」链路。
> 前置条件（2026-10 单流化落地后已变化）：原「断点 1：桌面档产物不可序列化」**已消失**——
> 全部产物同为 ECX1 平铺形态（`EcsContainer.WriteImage`），GUI 会话镜像可直接序列化。
> 仍在的断点：**GUI 产物入口未接**（`ScriptService.BuildAsync` 抛 NotImplemented）、
> **无自动传输通道**（`NintendoSwitch.Flash(byte[])` 协议已就绪、0x82/0x83 命令可用，但无人调用）。

## 1. 目标

编辑器里有脚本 + 单片机已连接 → 点一次「编译并烧录」→ 脚本在真机运行；任何失败给出**可操作的单一原因**
（在烧之前说清，而不是让用户拿到"设备没反应"）。

## 2. 核心设计（六个决定）

| # | 决定 | 要点 |
|---|---|---|
| D1 | **单一产物入口 `McuImageBuilder`** | 与 `ScriptHostAssembler` 同属组合根；产出 `McuImage`（序列化字节 stripDebug + Features + 静态极值），保证可分发镜像只有一处产出 |
| D2 | **预检 `McuPreflight`（纯函数）** | 输入镜像元数据 + `McuTargetProfile`，输出结构化判定（§3 四条件），不碰设备 |
| D3 | **能力查询命令 0x8A**（需固件配合） | 设备上报 `feats:u32 / arena_bytes / frame_seg_bytes / obj_block_bytes / obj_block_count / image_capacity / format_ver / abi_rev`（小端）。**上报池容量而非 max_slots**——零分配下固件 RAM 是编译期常数，主机侧拿路径帧和来比，不猜乘积。老固件无应答 → fail-closed 保守档（能力全无、容量 0=未知），容量只能来自 `config.json` 固件档案；都没有 → 拒烧并说明，**不许凭空猜**（产品事实：MCU 可用烧录容量约 512–1024 B） |
| D4 | **传输层加固** | 复用 `Flash(byte[])` 但：①入口加 32767 上限守卫（现 7+8 位偏移编码超限会**静默回绕**）；②`FlashAsync(bytes, IProgress<FlashProgress>, ct)`（进度/取消，同步 API 保留薄封装）；③失败返回 `FlashResult`（Success/RetriesExhausted/ControlLost/TooLarge）而非 bool——可区分"没响应"与"太大" |
| D5 | **烧录后启动** | 编排既有 `RemoteStart()/RemoteStop()` |
| D6 | **能力判定两段式** | 烧录前（C# 预检）给可操作诊断；加载期（C VM）critical 段/特征位 fail-closed 兜底——镜像是可手工拷贝分发的文件，"烧录工具检查过"不是设备能信任的前提 |

## 3. 可执行性判定：四个条件的合取（"装得下"≠"能执行"）

flash 容量与 RAM 是两件事。RAM 需求 = **活跃调用路径的帧和**（`Σ nslots×16`，非 `max_depth×max_slots`
乘积——光速过帧实测 336 B vs 乘积式 480 B；且链接期 `MaxDepth` 对递归回边按 1 计会**低估**，
不能直接当预算）。零分配池（[PROJECT_OUTLINE.md](../PROJECT_OUTLINE.md) §4）落地后，C4 退化为常数比对：

| # | 条件 | 判定 | 失败表现 |
|---|---|---|---|
| C1 | 容器/ABI 版本匹配 | ECX1 magic/format、`abi_rev` | 加载期拒跑（IMAGE/UNSUPPORTED） |
| C2 | 能力满足 | critical 段集 ⊆ 加载器支持集；镜像 feats ⊆ 设备 feats | 预检诊断 + 加载期 fail-closed |
| C3 | flash 装得下 | 镜像字节 ≤ image_capacity | 预检拒绝 |
| C4 | RAM 池够 | ①活跃路径帧和 ≤ frame_seg_bytes（非递归可精确算）②单帧 ≤ 帧段 ③递归脚本须声明深度否则拒（fail-closed）④静态极值（平铺头 u16×3 × 元素定宽）≤ obj_block_bytes | 帧段：预检；对象段峰值不可静态判定 → 运行期 `ECS_ERR_POOL` 干净失败（区分帧段满/对象段满） |

可以承诺：不满足容量不烧；满足了不会因 RAM 崩溃、只会干净报错。不能承诺："放下就一定跑得完"。

## 4. 产品硬门：带图像识别的脚本不脱机运行

产物只存标签**名字**（`@标签` → ExtVars → `Img/LoadK`），图像数据在 PC 侧；
镜像带 VISION/IL 特征位 → MCU 加载期拒跑（`ECS_ERR_FEAT/IL`）。预检在烧录前给出
"该脚本引用图像标签，不支持脱机运行"的可操作诊断。这条不可绕过。

## 5. 分阶段

| 阶段 | 内容 | 依赖 |
|---|---|---|
| P0 | GUI/CLI 接回：`McuImageBuilder` + `McuPreflight` + 编排 `Flash/RemoteStart`（结构化 FlashResult） | 无固件改动 |
| P1 | 能力查询 0x8A + 传输健壮性（FlashAsync/上限守卫/进度取消） | 固件配合 |
| P2 | 一键闭环与体验（进度 UI、失败原因定位） | P0/P1 |
| P3 | CRC 默认开启、产物格式演进 | 双端协商升版 |

测试锚点（实施时建）：`PortableProfile_AllExamplesSerializable`（examples+corpus 全量编译→序列化→往返）、
预检单测（四条件各一拒绝路径）、`FlashResult` 归因测试。
