# ECX 项目大纲 —— 现状、决策记录与待办（2026-10-06 整理）

## 1. 系统现状一页图

- **双 VM 架构**：`.ecs` → C# 编译器（SSA + SlotAllocator 槽型闭化 + 链接期优化）→
  **v3 定长指令流**（4B/8B）→ **ECX1 平铺镜像**（.ecx）/ **ECM1 平铺模块缓存**（.ecm）→
  双端执行：PC C# `EcxInterpreter`（语义基准）↔ MCU C `ecs_vm.c`（镜像零拷贝 XIP、
  定宽直取、零解码 RAM），corpus 逐行/逐事件锁步对拍。
- **宿主三层**：L1 域回调（wait/key/stick/rand，全部宿主必须实现，不降级）／
  L2 编号 syscall（封闭集，Amiibo = #16）／L3 名表动态原生（vision/ocr/net/jq/env…）。
  平台差异 = 宿主能力差异，VM 核单一确定性语义（S-21 能力降级）。
- **确定性地基**：加载期 `validate_stream` 唯一安全层 + S-01..S-21 契约 + corpus 双端对拍
  （现口径：主套件 915/0 双态、917 总）。
- **内存**：运行期零 malloc（P1/P2/P3，§4）；RAM 编译期可预算；资源耗尽 = 可预期失败
  `ECS_ERR_POOL(18)`，容器校验失败 = `ECS_ERR_CRC(17)`/`ECS_ERR_IMAGE`；
  SECTION(15)/UNSUPPORTED(16) 编号保留停用。
- **关键常量**（参考宿主）：`ECS_ARENA_BYTES`=256KB（帧段/对象段各半）、`ECS_MAX_SLOTS`=255、
  `ECS_MAX_CALL_DEPTH`=512、`ECS_DEFAULT_BUDGET`=100 万步、对象块 128B。

## 2. 性能阶梯与四方对比定论

### 2.1 执行阶梯（同机同批，Apple M4 / clang -O2；素数筛 2..10000 纯执行）

| 形态 | 耗时 | 状态 |
|---|---|---|
| LuaJIT 2.1（天花板参照） | ≈2.5ms | 参照 |
| Lua 5.5 PUC | ≈9ms | 参照 |
| ECX C VM 解码缓存形态 | 16.5ms | **历史过渡态**（2026-10-04，次日被定长化取代） |
| CPython 3.14 | ≈27ms | 参照 |
| **ECX C VM 定长直取（现行）** | **≈36ms** | 大筛 2..100000 ≈1.29s；callhot 3000 万次 ≈1.10s |
| ECX C# 解释器 | 37-40ms | 语义基准端 |

桌面定长直取相对解码形态回退 ~2× 是**既定取舍**（换真 XIP 零解码 RAM），性能回退预案
（解释器劣化 >30% 则在镜像上缓存解码结果，不恢复双分派臂）未触发。

### 2.2 四方对比（ECX / Lua 5.5 / CPython 3.14 / uvm32）要点

| | ECX | Lua 5.5 | CPython 3.14 | uvm32 |
|---|---|---|---|---|
| 本质 | 自有 DSL 槽位机 | 寄存器机字节码 VM | 栈机 + 自适应特化 | RV32IMA ISA 模拟器 |
| 线格式 | 4B/8B 定长 | 4B | 2B + 内联缓存单元 | 4B 裸机器码 |
| max RSS | **1920KB**（固定） | 2000KB | 14320KB | 1984KB（=镜像） |
| 本体 | 69KB | 53KB（最小运行时 70KB text） | 5.47MB | **2604B @M0+** |
| RAM 可静态预算 | ✅ | ❌（GC 堆） | ❌ | ✅ 但足迹 ≥ 镜像（非 XIP） |
| 安全 | 加载期 validate 沙箱 | 勿载不可信字节码 | 非安全边界 | 总线宏 + safeptr + meter 看门狗 |

- **每维赢家**：产物密度 + MCU 静态 RAM 预算 + 确定性/沙箱 → ECX；速度/体积比 + 表达力 → Lua；
  生态/特化与 JIT 上升空间 → CPython；本体最小 + 任意 RV32 后端语言自由度 → uvm32。
- **定论**：ECS 现路线 = 「uvm32 的壳 + Lua 的芯」，**不改道 ISA 模拟**（语义级效率慢 1-2 个
  数量级、无 F 扩展浮点致命、非 XIP）。槽位机 ≈ 寄存器机的 MCU 变体。
- **已取的经**：Lua 的定宽取指/局部化/融合指令（ForStep≈FORPREP、CmpJ≈比较融合）/switch 分派；
  uvm32 的编号 syscall/预算协议/固定内存。
- **不取的经**：CPython 自适应特化与内联缓存（需运行时可写代码区，与 XIP/固定池/双端锁步
  三面冲突）；JIT；Lua 的 GC/堆（RC + 静态池是 S-19 确定性语义的地基）。
- **ECX 剩余与 Lua 的 ~2× 差距**：4B vs 8B 混合取指宽度 + 逐指令 budget/cancel 协议
  （语义要求，不动）。数据不支持再优化。
- **可再借（挂账）**：uvm32 式 HUNG 看门狗语义（budget 耗尽且无显式 yield → 报「疑似卡死」；
  S-18 契约变更点，建议做成宿主层策略而非 VM 核改动）；「实例 = 单结构 + 两个可选外部缓冲」
  的文档化承诺。

## 3. 线格式与容器：v3 定长 + ECX1/ECM1 平铺（✅ 已实施）

### 3.1 设计决定（位级细节见 EcmEcxFormat.md v2.0）

| 决定 | 现行选择 | 理由 |
|---|---|---|
| 唯一指令表示 | 内存只有解码形态 `EcsInstruction` | 线格式只是序列化投影；编码器格式感知发射（`EcsFormat.Get` fail-fast） |
| 定长指令 | 4B（Iabc/ABx:u16/AsBx:s16/IsJ:s24）或 8B（Ext/IabcJ = iABC + u32 数据字） | MCU 真 XIP 零解码 RAM；越界**编译期响亮失败**（LoadI s16 超界发射期归一 LoadK） |
| 跳转双表示 | 内存 = 相对指令下标；线上 = 相对字节偏移（基准 = 指令起始 + 本指令字节数） | 换算单处收口 `InstructionCodec`，互逆由 `StreamInvariantTests` 锁定 |
| 接收槽哨兵 | 线上 `C=255` = 无接收槽；内存 `NoSlot=-1` | u8 槽位内无歧义 |
| 容量出格式 | 产物只记静态极值 u16×3@24（只记录不判定） | 容量属宿主档案（`ECS_MAX_SLOTS`=255 加载期逐函数校验）；函数槽位 >254 编译期响亮拒绝（**已获用户确认的收缩**，恢复路径 = 每函数宽窄双布局，挂账）；`EnablePcWideSlots` 与宽窄档位已删除 |
| 平铺容器 | ECX1/ECM1：36B 定长头 + 自计数表序 + 全量 CRC + 严格等长校验 | TLV 段框架开销 95-100B/产物 → ~40B 单头；演进 = 版本断代 + 全量重编，不做前向兼容/多版本加载器 |

### 3.2 容器选型决策记录（2026-10-05 定案）

准则 =「**不考虑兼容，清晰紧凑尽可能小**」。ECSC TLV → ECX1 平铺（尺寸账决定性：
midsieve 440B → 226B strip，−49%；光速过帧 674B → 446B strip）；.ecm → ECM1 平铺模块
`[头][常量池][字符串池][函数表+码][符号表]`，**不做 ELF-like**（.rela/.data/.bss 无语言语法
对应、无外部工具链消费方；链接重定位维持内存隐式模型）。模块缓存仅 PC 侧消费，
旧 ECSC kind=module 经魔数不符按未命中自动重编。

### 3.3 数据字步进铁律

12 条带数据字指令——Ext 8 条（`Call`/`CallN`/`NewArrV`/`Slice`/`GetFI`/`PutFI`/`StickP`/
`StickPv`）+ IabcJ 4 条（`ForStep`/`CmpJ`/`WaitI`/`KeyI`）。任何指令流扫描必须按 `WordCount`
步进（1=4B / 2=8B），**不得按操作数个数推算**。权威表：C# `EcsFormat.BuildTable`（漏登自检
抛出）↔ C `ecs_op_words`（逐项对齐）。

### 3.4 六张验证网

| # | 网 | 断言 | 载体 |
|---|---|---|---|
| N1 | 语义不变 + 尺寸不劣化 | corpus 双端逐行/逐事件一致；strip 产物 ≤ 门槛（560B，实测 446B） | `CorpusCrossValidationTests` / `Guangshu_Size_Breakdown` |
| N2 | 编解码互逆 | Lift(Project(x)) ≡ x；Project(Lift(b)) == b | `StreamInvariantTests` |
| N3 | 双端语义 | corpus/fuzz/examples 解释器 ↔ C VM 对拍 | `CvmCrossValidationTests` / `FullChainVerificationTests` |
| N4 | 缓存 round-trip | .ecm 写读模型等价 | `ModuleInterfaceTests` / `LineTableTests` / `ModuleProjectTests` |
| N5 | 尺寸核算 | ECX1 平铺逐表核算 = 产物字节 | `BytecodeSizeAnalysisTests` |
| N6 | 加载期静态校验 | 操作码/槽位/索引/跳转落点/数据字/严格等长 | C `validate_stream` + `ECS_ERR_*` 拒载用例 |

### 3.5 编码/解释器优化栈（已启用，各带守卫）

- **ForStep 快速路径**（15 条 → 3 条）：守卫 = `DeadStoreSweep` 终结符集必须含 ForStep
  （漏边 → 循环携带值误删，嵌套 FOR 死循环实证）。
- **循环不变量常量外提**：热点环 FOR 每轮 ≤5 条；共享组定槽双向 BFS 守卫防覆写。
- **空 trampoline 折叠**：守卫 = 重定向产生**重复边**必须跳过（φ 双臂被同一边门控误并，
  素数筛 9999 实证，`loop_sieve.ecs` 锁定）。
- **零 φ 拷贝**：非常量臂逐臂合并 + ForStep 臂直写 φ 槽；`ECS_NO_ZERO_PHI=1` 完整旁路
  （回归二分用）。
- **std/vision 包装函数强制内联**（链接期 `InlineStdWrappers`）：守卫 = 壳 ≤24 条/单块无跳转
  无体内 Call/末条 Ret/NParams==实参/内联后槽位 ≤254/每调用者共享 fresh 块，不动点 ≤4 轮；
  体内含调用的壳（OCR→OCR_INIT）保持包装形态。
- **CmpJ 比较跳转融合**：C = typeBlock×6+op；**守卫 = 新条件终结符必须同批进
  `DeadStoreSweep` `ops()` 终结符集**（漏入 → 循环携带值误删，素数筛回归实证）。
- **CLI compile 缺省 stripDebug**（`--no-strip` 保留）。
- 解释器期：比较族 21 case 直写、取指局部化、尾跳消除、phi 臂二地址合并、steps/budget
  局部化（~5%）。

### 3.6 镜像尺寸账（对照 .luac 269B 的拆解结论）

269B = 「全寄存器局部量 + 就地循环零 φ 拷贝 + 薄头」三事相乘。ECX 单条指令密度不吃亏
（CmpJ 1 条 vs Lua LE+JMP 2 条）；差距在 Move 副本、print 包装链与容器/调试开销——
前两处已由零 φ 拷贝 + std 内联 + CLI strip 解决（292B/1 函数/36 条），容器开销由 ECX1 平铺
解决（strip 226B）。ECM1 proj 链路 5 模块：answer 465 / math 492 / std 567 / main 696 /
vision 1232 B。

## 4. 零运行期分配：P1/P2/P3（✅ 已实施，P3c 暂缓）

目标：**MCU 固件里 malloc/calloc/realloc/free 运行期零命中，RAM 是固件编译期常数。**

- **三条既定决策**：D-A 镜像区 = NVS/EEPROM 专用分区、镜像缓冲零拷贝借用（可 XIP）；
  D-B 数组元素定宽紧凑存储（标量/句柄 4B、UINT64/DOUBLE/PTR 8B；**结构体元素数组 16B 定宽
  回退**——「按 sid 定宽」需扩 ABI，扩 ABI 时此处是候选）；D-C 单一 arena 切帧段 + 对象段，
  两段独立判定满溢。
- **P1 常量 pinned**（S-20）：句柄编码 bit31=1 ⟹ 低 31 位 = 常量池索引，字符数据直接引用
  镜像缓冲；不参与 RC；与动态串按内容等价。C# 对齐 `TaggedValue.FromStaticString`，
  驻留表已删除。
- **P2 固定帧池**：arena 帧段 bump 分配、弹帧回退 O(1)；`ECS_MAX_CALL_DEPTH=512` 仍为先行
  硬上限（保证深递归双端同报 ERR_DEPTH；真实固件调小 arena 后由池先收窄，属资源性分歧）。
- **P3 固定对象池**：单块 = 一对象（128B，24B 头 + 空闲链）；有界拒绝不做块链；
  `ECS_ERR_POOL` 三种归因（帧段满/对象块满/单对象超块，`err_name` 输出符号名）；
  TOSTR 工作缓冲 512 units 有界。
- **P3b 静态极值进产物**：u16×3@24 记录 max_literal_string_units / max_literal_array_elems /
  max_struct_slots——只记录事实，预检（McuBytecodeDelivery.md）比对设备档案。
- **资源边界**：「能不能装下」是资源问题（烧录前判定），「同程序行为一致」是语义问题
  （S 条目锁定）；C# 解释器（PC 真实堆）不设上限，两端允许资源性分歧。
- **P3c（加载期表入池）暂缓**；锁定测试：`PoolTests` / `StaticExtremesTests` /
  `PinnedStringTests` + corpus `pinned_strings.ecs`。

## 5. C VM 执行引擎决策记录（防走回头路）

现行引擎 = v3 定长直取（格式类查表 + switch + WJ 跳转字延迟读；fr/code/end/R/pc/idx/steps
循环局部化，ret_pc/ret_idx 仅调用边界与让出点写回）。历史上的解码缓存形态已于定长化落地日
整体退役——以下实测定论在**任何**取指形态下都成立：

- **反直觉实测**：① 仅取指通路局部化（不动字节流）只 +3%——解码/取指宽度才是绝对瓶颈；
  ② computed goto 直分派比 switch **慢 20%**（19 处 vmbreak 内联展开复制取指 → 代码膨胀；
  Lua「直分派 +10-20%」经验不适用于定宽数组取指形态）——**switch 保留，直分派不做**；
  ③ 3000 万次调用下整帧 memset 与 S-17 深拷贝**完全不可见**——调用约定不动（Lua 式
  CallInfo 重构被实测否定）。
- **error_pc/ret_pc = 指令下标**（与 C# ErrorPc 同单位；SEC_LINES 行表加载期转指令下标，
  MCU 侧可直接做行号诊断）。
- **clang 内联教训**：① 函数体内含任何辅助函数调用 → 整个函数被拒内联（move_to 拆
  move_to_slow 实测回退 20%+，已回退并注释记录）；② 叶子化也不够——clang 按「调用点数×
  代码尺寸」启发式拒绝，必须 `always_inline` 强制（store_fresh 实测消掉 ~15-18% 纯调用开销）。
- **二期候选全部销项**（按解码形态 profile，定长化后更不成立）：16B 打包（纯 RAM 赢 0.3-0.6KB）、
  LoadI+AddI 融合（热点环无 LoadI，不变量已外提）、句柄槽位位图（需 M 升版，pop_frame 不可见）。
  剩余地板 = 取指分派 + ALU 真实运算。

## 6. v2.3 双 VM 架构对齐（✅ 已收束，2026-10-05）

### 6.1 一期能力降级语义层（S-21，✅）

- **语义**：镜像 `feats` 降级为元信息（加载器缺省**不拒载**，`ECS_ERR_FEAT(14)`/`IL(13)` 保留给
  strict 模式与预检工具）；能力缺失按缺省值表响应，双端逐字相同。
- **缺省值表**：VISION/FRAME/IL → 槽 ← int 0；OCR → ""；NET_LOAD → -1；NET_RUN/NET_OUT → 0；
  ENV/JQ → ""；PRINT/ALERT/BEEP（L2）→ no-op；**AMIIBO → no-op 且不写任何槽**；
  实现修正：`Img` 运行期降级 = 槽 ← -1（对齐 EcxHost 缺省 ImgLabel）。降级不是旁路，
  是「换一个结果值」（走正常槽写原语）；L1 域操作不降级。
- **strict_caps 双态**（迁移保底/诊断/严格对拍档）：`EcxHost.StrictCaps` / `ecs-vm --strict-caps`，
  on 时恢复现行响亮拒跑。
- **锁步论证**：corpus 对拍继续用全能力宿主（既有期望不变）；新增**能力矩阵 corpus**
  同一镜像 × {全能力, 桩} 两档宿主 × 双端 = 四路对拍（`CapabilityMatrixTests`）。
  平台差异 = 宿主能力差异，VM 核仍是单一确定性语义。

### 6.2 二期 Amiibo（✅，按「不做兼容/适配」实施）

- 语言语句 `AMIIBO n` = 规范名 `AMIIBO`（**L2 syscall #16**），语义原地升级为槽位选择；
  **不设** `AMIIBO_SELECT` 别名、不追加 #18、不用 0x87 专用 R 型操作码（ISA +1 与
  「VM 核纯调度」分层冲突，可观察行为等价故否决）；n>9 旧约束删除。
- 槽位 0-19（AMIIBO_SLOT_MAX=20），越界静默（宿主内判，VM 无感知）；无接收槽，不写寄存器。
- 运行通路：脚本 → `CallN #16` → `EcxHost.Amiibo` → `GamePadAdapter.ChangeAmiibo` →
  `ChangeAmiiboIndex`（0x91）→ 设备；烧录/库管理 = 现有 `ESPConfig` 界面
  （`IDeviceService.Flash`/`GetVersion`）。corpus `amiibo_select` 双端对拍锁定。

### 6.3 三期 UART / 四期烧录：✅ 定案**不实施**

现有 `EasyCon.Device` EzDv 串口协议已覆盖远程控制（`RemoteStart/Stop`）、HID 实时、烧录
（`Flash`）与 Amiibo 槽位（0x91）；v2.3 原文的新帧层/HID 桥/UartBridge/amiibo_flash_tool.py
不再排期。两种运行模式按既有实现：PC 远程 = PC 解释器 + 串口宿主；自主 = MCU 固件 + 本地宿主。

### 6.4 平台矩阵 → 宿主容量档案（R-3 既有机制）

| 平台 | arena（帧+对象） | 能力集 |
|---|---|---|
| atmega16u2 | 极简档（如 4KB/1KB） | 基础 + 静默桩 |
| atmega32u4 / stm32f103 | 小档 | 基础 + 静默桩 |
| esp32-s3 | 标准档（256KB） | 基础 + Amiibo 本地 |
| PC C# | 动态（无池） | 全能力（槽 65535 = u16 上限） |

字节码槽（1-4 个 Stored programs）= 固件存储管理，不在 VM 实例内。

## 7. Script 编译器现代化（进行中）

> 骨架已达现代水准（单向流水线类型分界/模块 Merkle 增量/接口区/SSA pass/verifier/差分对拍），
> 原则 = **不向内核加机制**；补齐全部在「面向人的基础设施」层。每批门禁：双态主套件 +
> Lsp/Avalonia 四工程 + corpus 双端对拍，零警告纪律不变。

### 7.1 已完成

- **M1 诊断结构化 ✅**（2026-10-05）：`Diagnostic` 加 `Severity`/`Code`/`Notes`；
  `DiagnosticCodes` 分段码表（ECX0001 式，40+ 语义方法全接线）；TextLocation 列号补齐；
  CLI `compile --diagnostic-format=json`；LSP 发布 code/警告/列级 range。
- **M2 管线级联容错 ✅**（2026-10-05）：失败模块标记 `Failed` 跳过（不再快速终止整链）；
  依赖者报 **ECX0402** 级联诊断、绑定前拦截（无符号错误风暴）；隐式 lib 互见边随拓扑传递级联。
  `PipelineCascadeTests` ×2。

### 7.2 待做

- **M3 模块拓扑波次并行**：Kahn 层级分波，同波 `Parallel` 编译，主模块恒末波；隐式 lib
  接口预声明仍在并行前串行。线程安全依据：产物互不共享、`ProcessModuleCache` 为
  ConcurrentDictionary、磁盘写入 MoveWithRetry 原子、诊断聚合加锁。验收 = 多 lib 项目输出
  与串行逐字节一致（诊断排序后比对）+ 冷编译时长记录进本节。
- **M4 SsaOptimizer pass 声明化**：函数内 pass 循环改带元数据注册表（名字/旁路开关/跳过
  条件/执行委托），`ECX_PASS_TRACE` 与未来 per-pass verify 统一走 instrumentation。
  顺序与语义零变化，corpus 对拍为硬证据。
- **N3 未使用 IMPORT 警告**（ECX0120）：导入 v2 落地时 `OriginModule` 已就绪，
  仅差 graph builder 的 import 集 ↔ 绑定实际解析集比对（先 Warning，观察后可收紧）。

### 7.3 M5 lib 导入规则 v2（设计定稿 → N1/N2 已落地 2026-10-06）

对标 rust/go/zig 模块系统。旧规则的实际代价（均有实证）：自动加载致缓存粒度稀释（任何
lib 文件变动 → main 重编译）、互见包隐式耦合级联、全量导出致接口哈希噪声、平板命名冲突。

**设计定稿（R1-R5）**：R1 去自动加载（`IsImplicitRootLib` 退役，std/vision 保持隐式）；
R2 显式互导（删互见包）；R3 模块路径 = 命名空间；R4 未使用 IMPORT = 警告（ECX0120）；
R5 可见性不做（用户修正：全量导出保留，待后续新关键字）。迁移不做、直接替换（项目
「不做兼容/适配」准则）；`LegacySyntax` 仅 lexer 语义，下层不感知；缓存键
`ProductFingerprint` M=6 → M=7 断代（导入 v2 落地）；**2026-10-06 已重置为 M=1**——前提无旧版
缓存、仅开发测试，M4–M7 履历清除，机制保留（下次产物语义变更仍 +1，见 `Compilation.cs`）。

**已落地（N1+N2，2026-10-06）**：
① 去 lib/ 自动加载 + 根级库互见包退役——依赖边 = 显式 IMPORT 闭包，死文件不进编译；
② **lib 根唯一**：主脚本 lib/ 沿发现链传播（顺带修复嵌套导入 `lib/lib/` 漂移缺陷，
`../` 逃逸退役）；③ 模块名 = 相对 lib/ 层级路径（缓存文件名 `/` 转义）。
**随行的重大缺陷修复**：导入索引原按（名， 参数数）去重 → **跨模块同名导入串号**（层级
命名暴露）；修法 = `FunctionSymbol` 加 `OriginModule` + 导入名模块限定（`"模块!函数"`，
容器格式不变）+ 编码/链接键三元化。遮蔽语义：无限定名 = 绑定层 first-wins；alias =
精确指向来源模块；`MD_AMBIGUOUS_EXPORT` 警告保留。门禁：双态 915/0（917 总）+ 全工程
+ cvmlab 9/9。
**坑**：Parser 是分部类（`ParseImport` 在 Parser.Fn.cs，patch Parser.cs 静默不中）；
`LibTests.WriteLib` 与 `CvmCrossValidationTests.WriteLib` 路径参数语义不同（后者自带
lib/ 前缀与否逐个核对）；测试 30+ 自动加载用例全部改写为显式导入/负测试。

### 7.4 挂账（条件触发，不在计划内）

- **B5** 结构体类型表跨模块规范化——触发 = 真独立编译需求。
- **B6** 宿主容量档案参数化（C 常量 → 可查询档案）——触发 = 硬件批次。
- **LSP 绑定级诊断**——触发 = 用户需求明确（需 per-doc 增量绑定，成本大）。

## 8. 门禁与环境速查

```
sh ci/build-vm.sh                                      # C 零警告（cc -O2 -std=c99 -Wall -Wextra）
dotnet build EasyCon2.slnx -c Release && dotnet build-server shutdown
dotnet test test/EasyCon.Tests -c Release --no-build   # 915/0
dotnet test test/EasyCon.Tests -c Debug   --no-build   # 915/0
# 其余工程：EasyCon.Lsp.Tests 67 / EasyCon2.Avalonia.Core.Tests 106 / UiTests 41 / SDLInput 18
# C VM 端到端对拍：bigsieve=9592 / midsieve=1229 / loop_sieve=25,36 / callhot=-858471104
#   + corpus .expected + proj 链路 210 + add(answer(), 1)（已固化为 FullChain_ModuleProject_TwoImportsFuncCalls）
dotnet build-server shutdown                           # 计时前必做（后台负载可致 3× 漂移）
```

- CLI：`dotnet run --project src/EasyCon2.CLI -c Release -- compile x.ecs -o x.ecx [--no-strip] [--print]`。
- 实验区 `/tmp/cvmlab`（14 用例 + countinstr + bench.py）、`/tmp/uvm32lab` **重启即失**；
  重建配方见 baselines/perf.md §4 与本文 §5。
- 仓库工作树领先 HEAD 多轮：**HEAD 不是可靠来源，不 stash 不 reset**（用户自管 git）。
- 尺寸/性能对照物：baselines/perf.md（干净基线，只记现状实测，忘旧数字）。

## 9. 实证坑清单（全部会话实证，勿再踩）

1. **/tmp 拷贝的 ecs_vm.c 会让 quoted-include 静默编译旧版**（`#include "ecs_vm.c"` 先搜文件
   所在目录）——实验副本用不同文件名或删除（曾致「镜像校验失败 ECS_ERR=3」假缺陷，白诊一轮）。
2. **内联重映射槽位性**：`Conv` 的 C=转换种类、`GetF/PutF` 的 C=字段下标、LoadI/ABx 的
   B=立即数、StickSet/StickP 的 B/C=坐标（满 u8 0..255，不按槽位收紧）——错分 → TYPE 运行错误。
3. **「部分旁路」开关不算真回退**：回归二分时开关必须完整恢复旧行为，否则误导。
4. **CvmRunner** workDir 按 Guid 新建（无跨运行缓存问题）；`EnsureBuilt` 幂等复用同目录二进制。
5. **计时前 `dotnet build-server shutdown`**；机器有后台负载时多轮取中位。
6. **corpus .ecs 先复制再编译**（顺序反了白跑一轮）。
7. **主循环/取指重写后先跑含 WHILE/FOR 的用例**（跳转负偏移回边）；`--print` 输出为空 ≠ 通过
   （PRINT 在程序尾部，死循环时全无输出）。
8. **新条件终结符必须同批进 `DeadStoreSweep` 四处**（ops()/targets/fall-through/use-def）；
   ForStep 出边同理——漏边 = 循环携带值误删（素数筛两度实证）。
9. **FoldEmptyTrampolines 重复边守卫**：重定向产生 P→S 直连 + 经 T 重复边必须跳过。
10. **MCU 池边界测试须非尾形态**：`RETURN f($n-1)` 会被 SSA 消除成循环，压不出池边界。
11. **全量 CRC 下打补丁必须重封**（`EcsContainer.ResealCrc32`）。
12. **`.err` 陈年回放会掩盖真相**（M1 NIE 级联坑）——诊断快照测试锁定。
13. **s.index 标记切割大文档时同名小节先命中辅助区块**（编辑 VM2.md 等长文档的定位坑）。

## 10. 挂账与定案不做（全景）

**挂账（按需另批）**：M3/M4/N3（§7.2）、B5/B6/LSP 绑定诊断（§7.4）、
每函数宽窄双布局（宽槽恢复路径，§3.1）、P3c 加载期表入池（§4）、结构体数组按 sid 定宽
（扩 ABI 时，§4）、HUNG 看门狗语义（宿主层策略，§2.2）、状态单 struct 化文档表述（§2.2）。

**定案不做（勿再立项）**：computed goto 直分派（-20%）；调用约定/CallInfo 重构（实测无收益 +
S-17 契约成本）；C VM 内 C 侧槽类型重分析（复杂度不可控）；ISA 模拟改道（uvm32 路线）；
CPython 式特化/内联缓存/JIT（与 XIP/固定池/双端锁步冲突）；Lua 式 GC/堆（S-19 确定性语义）；
0x80-0xB4 专用操作码与 ISA 扩张（L2/L3 承载等价）；三期 UART 新协议栈与四期烧录工具
（EzDv + ESPConfig 承接）；R5 可见性关键字（待新关键字另立）；导入规则迁移层（直接替换）；
解码缓存（被 v3 定长取代）；TLV 容器回归与 ELF-like .ecm（平铺定案）。

---

## 附录：本文替代的原文件清单（2026-10-06 删除）

| 原文件 | 处置 |
|---|---|
| `CVM_LUA_ALIGNMENT_ANALYSIS.md` | 实测定论并入 §5，解码缓存方案过程剔除（被 v3 取代） |
| `UVM32_VS_LUA_ANALYSIS.md` | 结论并入 §2.2 |
| `INTERPRETER_COMPARISON_ECX_CPYTHON_LUA.md` | 对比要点并入 §2，v3 需求分析并入 §3（已实施） |
| `V23_VM_REDESIGN.md` | 收束状态并入 §6，格式决策并入 §3.2，对比过程剔除 |
| `HANDOFF_PROMPT.md` | 门禁/环境并入 §8，坑清单并入 §9；其「43 红未修缺陷」已定案为误诊（读取侧缺陷当日修复，写入侧无错），全文剔除 |
| `MODULE_IMPORT_REDESIGN.md` | 方案并入 §7.3（N1/N2 已于 2026-10-06 落地） |
| `SCRIPT_MODERNIZATION_PLAN.md` | 状态并入 §7 |
| `docs/SingleStreamFormat.md` | 现行形态并入 §3（位级权威仍为 EcmEcxFormat.md） |
| `docs/ZeroAllocVm.md` | 设计与坑位并入 §4（契约锚点仍为 VmSemanticContract.md） |
