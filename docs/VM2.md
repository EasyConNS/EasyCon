# VM2 设计文档 —— ECS 字节码编译器与纯 C 精简虚拟机

> **版本 2.1（详细设计）**。本文档是 VM2 的权威规格，与实现代码保持同步：
> C# 编码器 `src/EasyCon.Script/Bytecode/`，C 虚拟机 `src/EasyCon.Vm/native/`。
> 修改指令集、二进制格式或运行时语义时，必须同步更新本文档。

---

## 0. 摘要

VM2 把 ECS 脚本语言的执行后端从"SSA 树解释器（C#）"替换为"字节码 + 链接器 + 纯 C 虚拟机"：

```
                       ┌────────────────── C# 编译器（现有，保持不动）──────────────────┐
main.ecs ─┐            │  Lexer → Parser → ImportResolver → Binder → Lowerer          │
lib/*.ecs ─┼───────────►│        → SsaProgramBuilder → SsaOptimizer → SsaProgram       │
stdlib    ─┘            └──────────────────────────────┬───────────────────────────────┘
                                                       │ SSA（公共 IR，已有）
                       ┌────────────────── VM2 后端（新建）───────────────────────────┐
                       │  ModuleSplitter ──► BytecodeEncoder ──► EcxLinker ──► ECX    │
                       │  （按源文件拆模块）（SSA→指令选择）（多模块合并+改写）        │
                       └──────────────────────────────┬───────────────────────────────┘
                                                      │ ECX 镜像（二进制）
                       ┌────────────────── 纯 C 虚拟机（新建）────────────────────────┐
                       │  ecs_vm.c：镜像加载 → 解释执行 ← 宿主 vtable（按键/延时/IO） │
                       └──────────────────────────────────────────────────────────────┘
```

**一句话定位**：语言前端与优化器全部复用；VM2 只负责"SSA 之后的最后一步"——把类型特化的 SSA IR 翻译成紧凑字节码，让一个 ~1600 行的 C99 虚拟机在任何平台执行。

---

## 1. 背景与现状分析

### 1.1 现有执行路径盘点

> 本节为 VM2 设计启动时的现状快照。VM2 落地后实际走向：`SsaEvaluator` 与 `EasyCon.Script.Jit`
> （SsaJitCompiler）已删除，桌面执行改由 `EcxInterpreter` 承担；`ImportResolver`/`Resolver` 被
> `ModuleGraphBuilder` + `InterfaceScopeSynthesizer` 取代（见 `docs/Pipeline.md`）。

| 组件 | 位置 | 状态 | 本设计中的角色 |
|------|------|------|----------------|
| Lexer / Parser / ImportResolver | `EasyCon.Script/Syntax` `Resolution` | 健壮 | 不变。IMPORT 目前是**源码级合并**：所有 lib 树 + stdlib 每次运行重新走完整编译 |
| Binder / Lowerer | `EasyCon.Script/Binding` | 健壮，类型特化完善 | 不变（本设计的语义权威）。lib 全局变量在文件作用域内（模块私有），函数跨文件可见 |
| SsaProgramBuilder | `EasyCon.Script/Ssa` | 健壮 | 不变。产出三地址 SSA + 块/phi + 局部槽位分配（`FrameLayout`） |
| SsaOptimizer（SCCP/DCE/CFG 简化/TRE 等） | `EasyCon.Script/Ssa` | 健壮 | 不变。字节码体积与质量由它保证 |
| SsaEvaluator | `EasyCon.Core/Runner` | 健壮 | **保留为对拍金标准**。每次指令执行走 `switch(SsaOp)` + `SsaValue` 对象图遍历 |
| SsaJitCompiler | `EasyCon.Script.Jit` | 实验性 | 不动。VM2 落地后可退役 |
| VM1（`Assembly/`，2 字节单片机指令） | `EasyCon.Script/Asm` | **已废弃**（`Assembler.Assemble` 直接抛异常） | 由 VM2 取代（VM1 全部用户可见能力在 VM2 有等价或超集实现，无迁移缺口） |

### 1.2 为什么重建后端

1. **解释器开销**：`SsaEvaluator` 逐指令对 `SsaValue` 做字段访问 + 装箱边界转换（`CacheToValue`/`ValueToCache`），热循环（图像识别轮询）每次迭代付出对象图遍历成本。
2. **全局 SSA ID 缓存不可移植**：值存放在按 SSA ID 索引的全局数组，递归靠整份缓存拷贝解决（`CallUserFunction` 中 `savedCache`）——这是解释器私有技巧，无法产出可分发制品。
3. **无可分发制品**：无法把编译结果落盘、在无 .NET 运行时的环境（ESP32 下位机、第三方主机）执行。
4. **无增量编译**：stdlib + VisionSource 内嵌源码每次编译；大 lib 目录每次全量。
5. **VM1 已死**：2 字节定长指令集无法承载现代语言特性（字符串/数组/结构体/函数调用），且编码器已不可用。

### 1.3 设计目标

| # | 目标 | 度量 |
|---|------|------|
| G1 | 纯 C 虚拟机，C99，仅依赖 libc（malloc/free/memcpy/memset） | 单翻译单元，`cc -O2` 一条命令构建，可移植 ESP32-IDF |
| G2 | 精简 | VM 核心 ≤ 2000 行 C；不含任何词法/语法/链接/优化代码 |
| G3 | 语义与 `SsaEvaluator` 严格一致 | 对拍测试逐字节一致（§10） |
| G4 | 多模块编译 + 显式链接 | 按源文件拆模块；链接器独立于编码器；格式版本化 |
| G5 | 可分发 | ECX 镜像可落盘、可校验、可在无源码环境执行 |
| G6 | 安全 | 镜像加载即全量校验；运行时任何错误返回错误码而非崩溃 |

### 1.4 非目标（明确不做）

- 不改语言：语法、类型系统、Binder 语义全部冻结。
- 不在 VM 内做优化：寄存器分配复用 SSA 槽位 + 顺序临时槽，不做活性分析（G3 正确性优先；体积交给 SSA 优化器）。
- 不做 JIT、不做线程、不做异常表（错误即停机并报错误码）。
- v1 不做 ECM 磁盘导入的前端支持（需要 Binder 面向接口绑定，见 §6.5 路线图）。

---

## 2. 总体架构

### 2.1 两条路径

**内存路径（v1 全量交付）**：

```
Compilation.BuildSsa() → SsaProgram
  → ModuleSplitter：按函数声明处源文件拆分为 ModuleUnit 列表（导入序：stdlib → libs → main）
  → BytecodeEncoder：逐函数 SSA → 指令序列（槽位分配、phi 降级、常量池）
  → EcxLinker：合并模块 → 全局函数表/类型表/常量池/全局槽 → EcxImage
  → EcxWriter：序列化为 .ecx 文件（或直接内存交给 VM）
```

**磁盘路径（v2 路线，格式先定义）**：模块可单独序列化为 `.ecm`（含导入/导出表），链接器支持从 `.ecm` 集合链接，实现 stdlib 预编译缓存。前置条件见 §6.5。

### 2.2 组件与文件清单

| 文件 | 职责 | 状态 |
|------|------|------|
| `src/EasyCon.Script/Bytecode/EcsOpcode.cs` | 指令集枚举、值标签、类型码、转换种类、字段种类 | ✅ 已建 |
| `src/EasyCon.Script/Bytecode/BytecodeModule.cs` | EcxImage / EcsFunction / EcsConst / EcsStructLayout / EcsGlobal / 诊断 | ✅ 已建 |
| `src/EasyCon.Script/Bytecode/BytecodeEncoder.cs` | SSA → 指令序列（§5 全部算法） | 待实现 |
| `src/EasyCon.Script/Bytecode/ModuleSplitter.cs` | SsaProgram → ModuleUnit 列表（§6.1） | 待实现 |
| `src/EasyCon.Script/Bytecode/EcxLinker.cs` | 多模块合并（§6.3） | 待实现 |
| `src/EasyCon.Script/Bytecode/EcxWriter.cs` | ECX 序列化 + 反汇编（§7、§10.3） | 待实现 |
| `src/EasyCon.Vm/native/ecs_vm.h` | 公共 API：值、镜像、宿主 vtable、运行控制 | 待实现 |
| `src/EasyCon.Vm/native/ecs_vm.c` | 解释器 + 堆 + 镜像加载（§8） | 待实现 |
| `src/EasyCon.Vm/native/ecs_main.c` | CLI harness：`ecs-vm run image.ecx` | 待实现 |
| `test/EasyCon.Tests/Bytecode/*.cs` | 编码器/链接器单测 + 对拍（§10） | 待实现 |

---

## 3. 值模型与运行时语义（权威）

### 3.1 值表示：16 字节 tagged union

与 C# `TaggedValue` 完全同构：

```c
typedef struct { double f64; } ecs_f64;
typedef struct ecs_value {
    uint8_t tag;            /* EcsTag_*，见下表 */
    uint8_t _pad[7];        /* 载荷 8 字节对齐 */
    union {
        int32_t i32;        /* BOOL(0/1) / BYTE / INT / UINT（位模式共用） */
        int64_t i64;        /* UINT64 / PTR / 堆句柄（低 32 位） */
        double  f64;        /* DOUBLE */
    };
} ecs_value;
```

| Tag | 值 | 载荷 | 备注 |
|-----|----|------|------|
| VOID | 0 | — | 空值；句柄 0 同时表示 null |
| BOOL | 1 | i32 (0/1) | |
| BYTE | 2 | i32 | |
| INT | 3 | i32 | |
| UINT | 4 | i32（无符号解释） | |
| UINT64 | 5 | i64 | |
| DOUBLE | 6 | f64 | |
| STRING | 7 | i64 低 32 位 = 句柄 | UTF-16LE code unit 存储；长度按 code unit（与 C# `string.Length` 一致，中文 LEN 语义正确） |
| ARRAY | 9 | i64 低 32 位 = 句柄 | 元素为定长 `ecs_value` 数组 |
| PTR | 10 | i64 | FFI 指针 |
| STRUCT | 12 | i64 低 32 位 = 句柄 | 字段为按布局展开的 `ecs_value` 槽区 |

**标签可观察性**（决定编码器可以偷懒的地方）：运行时只有 `STRING/ARRAY/STRUCT` 的标签被解引用检查；`DOUBLE` 靠载荷 union 区分；`BOOL/BYTE/INT/UINT` 的标签差异**不可观察**（所有算术/比较由编译期类型特化的指令决定读取方式，字符串化由 SSA 静态类型决定格式）。因此编码器可用 `LoadI`（INT 标签）装 BYTE/UINT 常量，仅 BOOL 用 `LoadBool` 保持 0/1 规范化。

### 3.2 堆：句柄表 + 引用计数

- 句柄从 1 起，0 为 null；句柄表 = `objs[]`（`{kind, rc, payload}`），空闲链复用。
- 字符串驻留（intern）：**常量池字符串**加载时驻留；运行期产生的字符串（CAT/切片/转换）新建句柄。字符串不可变，复制一律共享。
- 数组：`{elem_type, len, ecs_value items[]}`，元素统一 `ecs_value` 存储（C# 侧 ScriptArray 有类型化存储优化，语义等价）。
- 结构体：`{layout*, ecs_value slots[]}`，槽区按 §7 StructDef 布局：Scalar/Dynamic 占 1 槽，FixedArray 占 Count 槽（内联）。字段读取返回副本（§3.3），保证值语义。
- **引用计数不回收环**。ECS 无闭包、无不变量破坏的容器 API，实践中值为树形；此限制与 Lua 5.0 前一致，写入用户文档。整体停机时随 VM 销毁全量释放，不泄漏。

### 3.3 拷贝语义表（对拍权威，逐条对齐 `SsaEvaluator`）

| 场景 | 值类型 | 行为 | C# 对应点 |
|------|--------|------|-----------|
| 指令间寄存器移动（Move/phi 副本/实参暂存） | 全部 | 浅拷贝 + 句柄 rc+1 | `_cache[src]` 赋值 |
| 变量赋值 SetVar（局部/全局） | BOOL..DOUBLE/PTR | 位拷贝 | `StoreLocalSlot` |
| | STRING | 共享句柄 | （字符串不可变） |
| | ARRAY / STRUCT | **深拷贝**新句柄 | `CopyHandleForStore`→`DeepCopyHandle`（struct 经 `EcsStruct(def,srcPtr)` 逐字节复制） |
| CALL 传参（复制进被调帧槽 0..n-1） | 同 SetVar | 同 SetVar（数组/结构体深拷贝，字符串共享） | `ValueToSlot` |
| RET（复制回调用者槽） | 同 SetVar | 同 SetVar | `ValueToCache` |
| GetF 字段读 | Scalar | 位拷贝 | `GetField` |
| | FixedArray 字段 | **物化**为新堆数组（整段复制） | `LoadField` ArrayType 分支 |
| | STRUCT 字段 | 新结构体句柄（深拷贝） | `GetNested` |

> **结构体字段种类限制（复核结论）**：`EcsStruct` 为纯 native 内存模型（`Marshal` 逐字节存取），仅支持**标量字段（含 STRING/PTR）、内联 STRUCT 字段、固定长度数组字段**；动态数组字段（`TYPE[]` 无长度）在求值器路径会 `NotSupportedException`——VM 不支持，ECX StructDef 的字段种类相应只有 Scalar/FixedArray/NestedStruct 三种。字符串字段的 native 指针生命周期归结构体所有（读取时物化为 C# 字符串）；VM 中字段槽存 STRING 句柄、读取返回共享句柄，可观察行为等价。
| PutF 字段写 | — | 深拷贝写入字段槽 | `SetField` |
| GetI 数组元素读 | — | 位拷贝/句柄共享 + rc | `container[index]` |
| SetI 元素写 | — | **原地写**（rc 管理：旧元素 rc−1，新元素 rc+1；写入值本身浅共享） | `SetItem` |
| DeepCopy SSA（数组展开语法） | — | 显式深拷贝 | `ExecuteDeepCopyToCache` |

> 设计要点：**"赋值/传参/返回 = 深拷贝（数组、结构体），phi/寄存器 = 浅共享"**。SSA 值不可变，浅共享不产生用户可见别名；别名只经由变量出现，而变量写路径全部走深拷贝——两个不变量合起来构成值语义。
>
> **COW（move-on-unique，已实施）**：四个深拷贝站点（SetVar/StoreG/实参/RET）在源句柄**唯一引用（rc==1，恒为出生临时槽持有）**时改为共享移交（retain + 接管），跳过容器拷贝；rc&gt;1 时维持深拷贝。可观察值语义逐字不变：共享只发生在「出生临时槽 ↔ 接收槽」之间（临时槽对程序不可见），变量/全局槽永不与另一变量/全局共享容器，故 SetI/PutF 等原地写保持安全。双端实现：C# `EcxInterpreter.DeepCopyCopyOnWrite`（开关 `ECS_COW=0` 可关）、C `ecs_vm.c deep_copy_cow`。

### 3.4 运算语义

| 运算 | 语义（与求值器对齐） |
|------|----------------------|
| INT 四则 | 32 位有符号回绕 |
| UINT 四则 | 32 位无符号回绕 |
| UINT64 四则 | 64 位无符号回绕 |
| DOUBLE 四则 | IEEE754 |
| `/`（DivI） | C 语义向零取整 |
| `\`（RDivI） | `(a + b / 2) / b`（`RoundDiv`，注意非向下取整） |
| **除零**（所有 Div/Mod/除法类取余） | **运行时错误，脚本停机**（C# 求值器抛 `DivideByZeroException`，见 `BoundValue.cs` "整数除零"；SSA 常量折叠层也明确"除零不折叠"）。VM 返回 `ECS_ERR_DIVZERO` |
| 移位 | C# 对 32 位整型移位量按低 5 位掩码（`x << 33` ≡ `x << 1`）；C 未定义行为 → **VM 必须显式 `& 31`/`& 63` 掩码**（§14 复核项 SR-07） |
| 移位 | 按源码语义左/右移（C# `<<`/`>>`）；移位量由编译期类型检查约束 |
| 比较 | 结果 BOOL（0/1）；字符串按 UTF-16 code unit 序列精确相等（只有 Eq） |
| `in`（Cont） | 字符串 in 字符串：子串（Ordinal）；标量 in 数组：逐元素值相等，**元素类型不匹配返回 false（不是错误）**；容器既非字符串也非数组 → 运行时错误 |
| CAT | 任一侧 STRING → 双侧 TOSTR 后拼接；两侧数组 → 元素类型一致才拼接（否则错误）；其余 → 运行时错误（对齐 `Value.Concat` "只有数组可以执行 Concat"） |
| TOSTR | **两种上下文，必须分别实现**（C# 两层格式本就不一致，逐字对齐）：①**顶层**（`ConvToString`，即 `STRING()`/显式转换）：按 SSA 静态类型分派——BOOL→"true"/"false"；整型→十进制；DOUBLE→`ToString()`；PTR→**十进制**；handle→转 `Value.ToString()`。②**容器内嵌套**（`Value.ToString()`，数组元素递归/结构体）：PTR→**十六进制 `0x{X}`**；STRUCT→**`struct:类型名`**（不含字段）；ARRAY→`[a, b, ...]` 递归；VOID→"void"。DOUBLE 均为 `ToString()` 当前文化（已知偏差 §8.6） |
| RAND(n) | [0, n) 均匀整数；n=0 → 0；n<0 → 运行时错误（对齐 .NET `Random.Next`：0 返回 0、负数抛参数异常）；宿主注入 RNG |
| AMIIBO(n) | **n > 9 静默忽略**（对齐 `ImplAmiibo` 守卫），n∈[0,9] 切换槽位 |

### 3.5 转换种类（Conv 指令 C 字段）

```
IntToDouble DoubleToInt IntToUInt UIntToInt IntToByte BoolToInt
IntToUInt64 UIntToUInt64 UInt64ToInt IntToPtr PtrToInt UInt64ToPtr
PtrToUInt64 DoubleToUInt64 UInt64ToDouble ToStr ToInt
```

`DoubleToInt`：C# `(int)` 截断语义（向零，越界未定义→饱和处理，取 0x7FFFFFFF/0x80000000）。`ToStr` 走 §3.4 TOSTR。`ToInt` 运行时分派（整数类别取 i32，DOUBLE 截断，其余报错）。

---

## 4. 指令集规格（权威编码表）

### 4.1 编码格式

小端。5 种格式：

| 格式 | 字节布局 | 用于 |
|------|----------|------|
| iABC | `op:8 \| A:8 \| B:8 \| C:8`（4 字节） | 三地址运算、域操作 |
| AsBx | `op:8 \| A:8 \| sBx:16`（4 字节，补码） | LoadI |
| ABx  | `op:8 \| A:8 \| Bx:16`（4 字节） | LoadK / NewArrE / NewSt / Img / WaitI / KeyI |
| IsJ  | `op:8 \| s24:24`（4 字节，补码，单位=指令字） | Jmp |
| EXT  | iABC 4 字节 + 后随 `ext:32`（共 8 字节） | Call/CallN 的目标 ID；Slice 的 end 槽；GetFI/PutFI 的索引槽；StickP/StickPv 的时长/packed 坐标 |

容量上限（编码期强制，越界报诊断）：槽位号 ≤ 255；Bx ≤ 65535；单函数指令数 ≤ 2²⁴；Jpt/Jpf 的 s16 偏移越界时由编码器自动套跳转反转模板（§5.4），不产生用户错误。

### 4.2 指令表

权威枚举 = `EcsOpcode.cs`（C 侧 `ecs_vm.h` 镜像同名）。分组摘录（`R[a]` 表示帧槽）：

**常量与移动**

```
Nop                            无操作
Halt                           入口函数返回后由 VM 隐式处理（镜像不显式生成）
LoadI      a, sBx              R[a] = sBx（INT）
LoadK      a, Bx               R[a] = 常量池[Bx]（INT/UINT/UINT64/DOUBLE/PTR/STRING）
LoadBool   a, b                R[a] = (b!=0)（BOOL）
Move       a, b                R[a] ← R[b]（浅拷贝，rc+1）
SetVar     a, b                R[a] ← 深拷贝(R[b])（§3.3 变量赋值行）
LoadG      a, Bx               R[a] ← 全局槽[Bx]（浅读，全局不在帧槽区，独立数组）
StoreG     Bx, a               全局槽[Bx] ← 深拷贝(R[a])（§3.3 变量赋值行）
```

**算术/位/比较**（iABC，后缀 I/U/L/D = INT/UINT/UINT64/DOUBLE）

```
AddI SubI MulI DivI ModI RDivI      R[a] = R[b] op R[c]
AddU SubU MulU DivU ModU
AddL SubL MulL DivL ModL
AddD SubD MulD DivD
BandI BorI BxorI ShlI ShrI          位运算（INT）
BnotI   a, b                        R[a] = ~R[b]
EqI LtI LeI GtI GeI                 有符号比较 → BOOL
EqU LtU LeU GtU GeU
EqD LtD LeD GtD GeD
EqL LtL LeL GtL GeL
EqS                                 字符串精确相等
EqP                                 PTR 64 位相等
Not     a, b                        R[a] = (R[b]==0)
NegI NegD a, b
Conv    a, b, kind                  §3.5
```

> `!=` 不设指令：编码器展开为 `EqX` + `Not`（额外占一个临时槽）。

**控制流**

```
Jmp  s24                             pc += s24
Jpt  a, s16                          R[a] 非零 → pc += s16
Jpf  a, s16                          R[a] 为零 → pc += s16
```

真值定义：`i32 != 0`（句柄非 0 即真）。条件源自 SSA `BranchCondition` 槽。

**调用**（EXT）

```
Call   a, n, c, funcid:32            调用镜像函数 funcid：
                                     实参 = 调用者 R[a..a+n)，按 §3.3「CALL 传参」复制进新帧槽 0..n-1；
                                     c≠255 → 返回值按「RET」语义复制到 R[c]（显式接收槽，避免 0 参调用的 a-1 边界）
CallN  a, n, c, nativeid:32          调用原生函数表第 nativeid 项（§9）
Ret    a                             返回 R[a]（深拷贝到调用者接收槽，rc 交接）
Ret0                                 返回 VOID
```

**数组 / 字符串**

```
NewArrV  a, n, first                 R[a] = 新数组，元素浅拷贝自 R[first..first+n)
NewArrE  a, Bx                       R[a] = 空数组，元素类型 = 类型码 Bx
GetI     a, c, i                     R[a] = R[c][R[i]]（数组元素；字符串返回单字符 STRING）
SetI     c, i, v                     R[c][R[i]] ← R[v]（原地，rc 维护见 §8.4）
Slice    a, c, s, ext                R[a] = R[c][R[s] .. R[ext])；ext=0xFFFFFFFF 表示省略右端
                                     语义对齐求值器：start/end > len 或 start > end → 运行时错误（不 clamp）
Cont     a, x, c                     R[a] = R[x] in R[c]
Append   a, c, v                     R[a] = 新数组 = R[c] + [R[v]]（浅拷贝元素 + rc）
Cat      a, b, c                     §3.4
Len      a, b                        R[a] = len(R[b])（数组元素数 / 字符串 code unit 数）
```

**结构体**

```
NewSt    a, Bx                       R[a] = 实例化类型表[Bx]（槽区清零：数値 0，句柄 0）
GetF     a, o, f                     §3.3 字段读
PutF     o, f, v（A=o? 编码为 A=obj, B=f, C=v 所在槽——以 EcsOpcode.cs 注释为准）
GetFI    a, o, f, ext                R[a] = R[o].FixedArray[f][R[ext]]
PutFI    o, f, v, ext                R[o].FixedArray[f][R[ext]] ← R[v]（A=obj, B=f, C=v，ext=索引槽）
```

> PutF 的操作数指派以 `EcsOpcode.cs` 注释为权威；本文档表格与注释冲突时以代码为准并修文档。

**域操作**（§9 有宿主映射）

```
WaitI    Bx                          延时 Bx 毫秒（0..65535）
WaitV    a                           延时 R[a] 毫秒
KeyI     a, Bx                       点击按键 a（GamePadKey 值）持续 Bx 毫秒
KeyV     a, b                        点击按键 a 持续 R[b] 毫秒
KeySt    a, b                        b=1 按住 / b=0 松开 按键 a
StickSet a, x, y                     设摇杆 a(0=L,1=R) 坐标 (x,y)（编译期常量直接编码）
StickP   a, x, y, ext:32             点击摇杆 (x,y) 持续 ext 毫秒
StickPv  a, durslot, ext             点击摇杆 ext=parsed(x|y<<16) 持续 R[durslot] 毫秒
Img      a, Bx                       R[a] = 图像标签匹配（标签名=常量池[Bx]）置信度 INT
Rand     a, b                        R[a] = rand(R[b])
Time     a                           R[a] = 宿主运行毫秒
Beep     a, b                        蜂鸣 freq=R[a] Hz，dur=R[b] ms
Amiibo   a                           切换 amiibo 槽位 R[a]
```

按键码表（`GamePadKey`，u8 直传）：`Y=1 B=2 A=3 X=4 L=5 R=6 ZL=7 ZR=8 MINUS=9 PLUS=10 LCLICK=11 RCLICK=12 HOME=13 CAPTURE=14`；HAT：`TOP=16 TOP_RIGHT=17 RIGHT=18 DOWN_RIGHT=19 DOWN=20 DOWN_LEFT=21 LEFT=22 TOP_LEFT=23`；摇杆：`LS=32 RS=33`（side = key−32）。摇杆坐标 x/y ∈ [0,255]，回中 = (128,128)；方向/角度→坐标的换算在 C# 编码期完成（SSA `StickAction`/`StickPress` 的 packed 常量：`x=(packed>>16)&0xFF, y=(packed>>8)&0xFF`），VM 不理解角度。

### 4.3 槽位模型

```
帧槽区布局：
  [0 .. nParams)                       参数（符号槽，SsaProgramBuilder 分配，参数在前）
  [nParams .. FrameLayout.SlotCount)   其余局部变量符号槽
  [SlotCount .. )                      专用槽：phi 结果（边写入，恒跨块）与跨块值，一值一槽
  (.. )                                共享槽：Neq 中间槽（Eq+Not，1 槽）、并行副本 scratch（有 phi 才有）、
                                       实参 staging（maxArity）、返回值 receive（1）
  (.. nSlots)                          块内槽池：块内值与常量，定义/物化时取用，末次读取归还
nSlots ≤ 255
```

- **跨块/块内分类 + 槽池复用**：phi 结果恒专用；其余值当且仅当全部读取都在定义块内 → 块内池化
  （直线上 defs/uses 全序，「定义分配 + 末次读取归还」即精确活性）；跨块值专用槽。
  phi 臂读取按臂↔前驱对齐（A-01）计入对应**前驱块**（并行副本在前驱终结符发射）。
- **常量块首惰性物化（去函数级 prologue）**：常量是纯值，在每个使用块的块首物化到池槽
  （`LoadI`/`LoadBool`/`LoadK`），末次读取后归还；零使用的死常量不物化。与求值器
  "构造期预计算常量缓存"语义一致（常量与控制流无关，循环内重物化安全）。
  立即数形式的常量操作数（`KeyI`/`WaitI`/`StickP` 的常量时长）不物化、不占槽。
- RAM↔flash 取舍：惰性物化使多块引用的常量每块一条物化指令（flash +N），换取常量不再各占
  专用槽（RAM −1 槽/常量）。光速过帧 $eval：51 槽（优化前基线）→ 22 槽，帧区 1.6KB → 704B。
- `LoadLocal` ⇒ `Move v_slot, sym_slot`；`StoreLocal` ⇒ `SetVar sym_slot, arg_slot`；全局变量不在帧槽区，用 `LoadG`/`StoreG`（镜像全局槽表索引，见 §7）。

### 4.4 调用约定

```
调用者                          被调者帧
  实参连续暂存于 R[base..base+n)   槽 0..n-1 = 参数符号槽（Call 执行时深拷贝）
  Call base, n, hasret, fid       nSlots 槽区；VM 分配
  返回值 → R[base-1]（hasret）     Ret/Ret0 触发弹帧 + 值交接
```

- 递归：帧独立分配，天然支持（无需解释器的缓存拷贝技巧）。
- 深度上限 `ECS_MAX_CALL_DEPTH`（默认 512，可配），超限报 `ECS_ERR_DEPTH`。
- 尾递归：SSA 优化器（TRE）已把尾递归变成循环回边，VM 无需 tail-call 指令。

---

## 5. 编码器设计（SSA → 字节码）

### 5.1 主流程

```
EncodeFunction(SsaFunction f):
  1. 使用点收集：操作数/分支条件记在使用块；phi 臂按臂↔前驱对齐记在前驱块
     （立即数形式的常量操作数不计）
  2. 分类分配（§4.3）：phi/跨块值 → 专用槽；块内值/常量 → 池化（发射期分配）；
     保留 Neq 共享槽、scratch、staging、receive；池区在保留区之后生长
  3. 逐块发射：
     a. 块首惰性物化本块引用的常量（池槽）
     b. 逐条指令：先结算操作数末次读取（槽位可被结果复用），再 EmitInst()（§5.5）
     c. 块终止（§5.4 三种出口形态）：条件/返回值/臂读取结算归还
  4. 第二遍 patch：Jmp/Jpt/Jpf 偏移（块目标与 skip 标签两类 fixup）、Call/CallN 的 ext 目标
  5. 校验：nSlots ≤ 255、Bx ≤ 65535、指令数 ≤ 2²⁴；DEBUG 构建校验池计数清零
```

### 5.2 SsaOp → 指令映射（全量）

| SsaOp 组 | 指令序列 | 备注 |
|----------|----------|------|
| ConstBool | `LoadBool slot, 0/1` | prologue |
| ConstByte/ConstInt/ConstUInt | sBx 范围内 `LoadI`，否则 `LoadK` | prologue |
| ConstUInt64/ConstDouble/ConstPtr/ConstString | `LoadK` | prologue；字符串入常量池 |
| LoadLocal | `Move v, sym_slot` | |
| StoreLocal | `SetVar sym_slot, arg` | |
| LoadGlobal | `LoadG v, gslot` | 全局槽 = 镜像槽（§6.2） |
| StoreGlobal | `StoreG gslot, arg` | |
| AddInt..ModInt | `AddI..ModI` | |
| RoundDivInt | `RDivI` | |
| AddUInt..ModUInt / AddUInt64..ModUInt64 / AddDouble..DivDouble | `*U` `*L` `*D` | |
| AndInt..ShrInt | `BandI..ShrI` | |
| NotInt | `BnotI` | |
| EqInt..GeqInt / EqUInt.. / EqDouble.. / EqUInt64.. | `EqI..GeI` `*U` `*D` `*L` | |
| NeqInt/NeqUInt/NeqDouble/NeqUInt64/NeqBool/NeqByte | `EqX t, a, b` + `Not v, t` | t = 临时槽 |
| EqString / NeqString | `EqS` (+`Not`) | |
| EqPtr / NeqPtr | `EqP` (+`Not`) | |
| LogicNot | `Not` | |
| ConvBoolToInt / ConvByteToInt | `Move`（标签不可观察，§3.1） | 零指令优化 |
| ConvIntToUInt / ConvUIntToUInt64 / ConvUInt64ToPtr / ConvIntToPtr / ConvPtrToInt… | `Conv kind` | 载荷重解释 |
| ConvIntToDouble / ConvDoubleToInt / ConvUInt64ToInt / ConvDoubleToUInt64… | `Conv kind` | |
| ConvToString | `Conv ToStr` | |
| ConvToInt | `Conv ToInt` | |
| Phi | （不发射；§5.3） | |
| CondBranch / Branch | （块出口，§5.4） | |
| Return | `Ret arg` / `Ret0` | |
| Call / StaticCall | §5.6（用户函数 → `Call`；builtin/extern → `CallN`） | |
| ArrayInit | `NewArrV`（参数槽连续化，§5.6） | 全常量数组已由求值器路径处理，VM 侧正常执行 |
| LoadIndex / StoreIndex | `GetI` / `SetI` | |
| Slice | `Slice`（end 缺省 → ext=0xFFFFFFFF） | |
| ArrayLen | `Len` | |
| Contains | `Cont` | |
| Concat | `Cat` | |
| ArrayAppend | `Append` | |
| DeepCopy | `SetVar t, src`（深拷贝到临时槽） | 复用 SetVar 语义 |
| StructInit | `NewSt` | |
| LoadField / StoreField | `GetF` / `PutF` | Aux=EcsFieldDef → 字段序号 |
| LoadFieldIndex / StoreFieldIndex | `GetFI` / `PutFI` | |
| KeyPress | dur 常量 → `KeyI`；否则 `KeyV` | Aux=GamePadKeySymbol |
| KeyAction | `KeySt key, 0/1`（Const.GetBool()=release→0） | |
| StickAction | `StickSet side, x, y`（packed 解包） | |
| StickPress | dur 常量 → `StickP`；否则 `StickPv` | |
| Wait | dur 常量 → `WaitI`；否则 `WaitV` | |
| Rand | `Rand` | |
| RuntimeValue(__TIME__) | `Time` | |
| RuntimeValue(__APP__) | `CallN "APP"` | 原生表 |
| ImageLabel | `Img`（标签名入常量池） | |
| Capture / Ocr / Roi / OcrInit | `CallN` "__CAPTURE__/__OCR__/__ROI__/__OCR_INIT__"（参数连续化） | 语义等价：求值器直接调委托 vs VM 调原生再由宿主映射委托 |
| OcrInit | `CallN "__OCR_INIT__"` | |
| Nop | `Nop`（或跳过） | |

### 5.3 phi 降级：并行副本

phi 序列 `{phi_i: dst_i ← arm_i}` 挂在前驱边 P→B 上。同一边的全部 phi 构成一个**并行副本**（所有读发生在所有写之前）。算法（Sessa 风格）：

```
EmitParallelCopy(moves: list<(dst, src)>):
  while moves 非空:
    取 dst 不在任何剩余 src 中的 move → 发射 Move dst, src，移除
    若全是环（每个 dst 都是某 move 的 src）:
      取任一 move (d, s) → 发射 Move SCRATCH, d；把该 move 的 src 替换为 SCRATCH
```

- phi 的 dst 都是新临时槽；环只在 phi 互相引用（swap）时出现，SCRATCH 一函数最多一个。
- 例：循环头 `x' = phi(x, y'), y' = phi(y, x')`，回边上 moves = `(x',y),(y,x')` → 发射 `SCRATCH←x'`? 正确序列：`Move t, x'; Move x', y; Move y, t`。

### 5.4 边副本放置与块出口

块 P 的出口三种形态（后继块记 T/F）：

```
① 无条件（JumpTarget = B）:
     [B 的 phi 副本]  Jmp →B

② 条件两后继均无 phi:
     Jpt cond, →T   （或 Jpf 优先落空的分支以省一条 Jmp）
     Jpf cond, →F / 直落

③ 条件且后继含 phi（完整形态）:
     Jpf cond, +K            ; 假路径跳过真副本
     [T 的 phi 副本]
     Jmp →T                  ; K 落到下面
     [F 的 phi 副本]          ; 直落 F
```

**跳转反转逃生舱**：`Jpt/Jpf` 的 s16 越界时，改为

```
Jp<相反> cond, +2      ; 不成立跳过
Jmp s24 →目标          ; 4 字节大偏移
```

`Jmp` 的 s24 也越界（>2²⁴ 条指令的函数）→ 编码错误诊断（工程上不可达）。

### 5.5 调用发射（§4.4 约定的实现）

```
EmitCall(callValue):
  args = [Arg0, ExtraArgs...]（n = 0..N）
  把各实参 Move 到连续暂存槽 base..base+n-1（实参已在其 SSA 槽，逐个 Move，rc 语义 = Move 浅拷贝）
  发射 Call base, n, hasret, fid   （fid 编码期已由全局函数表分配，先占位后 patch 不需要——内存路径直接已知）
  hasret → 结果 SSA 值的槽固定为 base-1？否：返回值先落 R[base-1]（约定接收槽），
           再 Move 结果槽 ← R[base-1]（浅拷贝）。base-1 编码器保证 ≥ 常量区之后。
```

> 备注：接收槽 + 回 Move 多一条指令，但让"结果槽 = SSA 值槽"的全局不变式成立，简化所有其他指令的发射。实参暂存区复用策略：每函数保留一个调用暂存区（max arity 大小），与临时槽不重叠。

### 5.6 CallN（原生调用）参数

同 §5.5，ext = 原生表 ID。**参数为按位传值 + 句柄 rc+1 的浅拷贝**；原生函数不得长期持有句柄（返回前交还）。返回值：`hasret` 时写入接收槽（原生负责构造合法 `ecs_value`，句柄归 VM 所有）。

### 5.7 诊断

| 诊断 | 触发 |
|------|------|
| BC_SLOT_OVERFLOW | 函数 nSlots > 255 |
| BC_CONST_OVERFLOW | 镜像常量池 > 65535 |
| BC_CODE_OVERFLOW | 函数指令数 > 2²⁴ |
| BC_UNSUPPORTED | 遇到未映射 SsaOp（新前端特性先于 VM2） |
| BC_INTERNAL | SSA 不变量破坏（如未终止块、phi 臂缺失）——视作 bug 报告 |

诊断含 `函数名 + SsaValue.Id`，编码即失败（不产出半成品镜像）。

---

## 6. 模块系统与链接

### 6.1 模块定义与拆分

- **模块 = 一个源文件**。成员判定：
  - 函数：`FunctionSymbol.Declaration.Syntax.Syntax.Text.FileName`（FuncDeclBlock 的源文件）；内建符号（Declaration=null）归 `<builtin>` 模块。
  - `$eval` 主函数：整体归 **main 模块**（其语句混合了各文件顶层代码；v1 不做 per-module `<init>` 拆分——语义等价性由导入顺序保证：Binder 已把 lib 顶层语句按导入序拼进 `$eval`）。
  - 全局变量：**模块私有**（Binder 保证 lib 全局只在文件作用域可见；跨模块可见的只有函数）。
  - 结构体：**共享类型表**（Binder 合并为按名唯一，链接期按名去重校验）。
- 拆分产出 `ModuleUnit { Name, Functions[] }`，顺序 = ImportResolver 树序（stdlib → 显式 import → lib/ 自动加载 → main），保证链接确定性。

### 6.2 符号模型

```
镜像全局函数表 fid:  模块序优先，模块内按声明序连续分配
                     (module, FunctionSymbol) → fid 在编码前完成预分配（编码器直接发射 fid）
镜像全局槽表 gslot:  逐模块串联：module_i 的全局占 [base_i, base_i+n_i)
                     LoadGlobal/StoreGlobal 直接发射绝对镜像槽号（编码期已知）
类型表 sid:          program.StructDefinitions 按 name 排序 → sid（编码期已知，编码期确定性）
原生表 nid:          编码期扫描全部 CallN 目标名，按首现序分配（编码期已知）
常量池:              编码期按模块积累，链接期合并去重 → LoadK 的 Bx 需要链接期 patch
```

> 结论：**内存路径下唯一的链接期 patch 是常量池索引**，其余引用编码期闭合。链接器的主要职责是：模块合并、各表合成、代码偏移计算、ECX 组装、诊断。

### 6.3 链接算法

```
Link(modules[] 按导入序):
  1. 类型表：收集各模块结构体，按名排序去重；同名不同布局 → LC_STRUCT_CONFLICT
  2. 全局槽：逐模块分配镜像槽号，产出全局表（模块名+变量名+类型，诊断用）
  3. 函数表：逐模块登记 → EcsFunction 列表 + (module, symbol)→fid 映射（实际编码前已完成，此处校验一致性）
  4. 原生表：合并各模块原生名（去重），产出 nid 映射；编码器产物中的 nid 随之重映射
  5. 常量池：逐模块条目按 (tag,payload) 全局去重合并 → 重映射所有 LoadK 的 Bx
  6. 代码布局：逐函数串接，计算 codeOff/codeWords；镜像 entry = fid($eval)
  7. 产出 EcxImage + 链接诊断；写 ECX 或直接交付 VM
  6. 入口合成：CALL <init:m1>…<init:mn>（拓扑序）前插 $eval 本体，入口更名 <main>
  7. 死函数消除：自入口 BFS 调用图（Call 边），不可达函数体不进镜像，fid 重映射
```

### 6.4 链接诊断

| 诊断 | 场景 |
|------|------|
| LC_STRUCT_CONFLICT | 同名结构体字段不一致（内存路径不会发生——Binder 已合并；为磁盘路径预留） |
| LC_FUNC_CONFLICT | 同模块同名同参函数重复（v1 内存路径不会发生） |
| LC_MISSING_FUNC | 导入的函数在依赖模块中不存在（磁盘路径） |
| LC_NATIVE_UNKNOWN | 原生函数名不在宿主注册表（**运行期**报错而非链接期——VM 无法验证宿主能力，见 §9.3） |

### 6.5 ECM 磁盘格式与预编译缓存（v2 路线）

ECM = 单模块 ECX 子集 + 接口表：

```
ECM := "ECM2" ver:u16 模块名:utf8
       各表（模块局部：常量/类型引用/全局/原生/函数/代码）
       import_count:u32 { name:utf8 nparams:u8 ret:u8 }     # 需要的外部函数
       export_count:u32 { name:utf8 local_fid:u32 nparams:u8 ret:u8 }
```

模块内 `Call` 目标：本模块函数 = 局部 fid；外部 = `0x80000000 | import_idx`，链接期改写。

**前置条件**：从磁盘加载 `lib.ecm` 要求 Binder 能"面向接口绑定"——即编译 main 时只需 lib 的导出签名而非源码。该能力已由 **`docs/ModuleSystem.md`（接口式独立编译）** 完成设计：接口区持久化绑定可见信息（§4.6 完备性清单），消费端 `Binder` 经 `IModuleProvider` 双后端消费接口；lib 顶层语句进入模块 `<init>`，由链接器合成 `<main>` 调用序列（§5.4）。实施顺序遵循 ModuleSystem.md §9 的 M1–M7。

---

## 7. ECX 二进制格式（v1）

```
偏移  字段
0     magic "ECX2"（4 字节）
4     format_ver:u16 = 1
6     flags:u16（bit0: 含调试名表；bit1: KeyAction——程序含按键指令，宿主需创建手柄；
              bit2: NeedIL——程序引用图像标签，宿主需准备标签匹配器；其余保留，必须 0。
              对齐 CompileResult.KeyAction/NeedIL，由编码器扫描 SSA 写入）
8     const_count:u32
      常量条目 ×N:  tag:u8 + payload
                    INT/UINT:      i32:4
                    UINT64/PTR:    i64:8
                    DOUBLE:        f64:8
                    STRING:        units:u16 + UTF-16LE 字节（units 个 ×2）
?     struct_count:u32
      StructDef:  name:utf8 field_count:u8
                  Field: name:utf8 kind:u8 base_type:u8 elem_type:u8 count:u16
                  （kind: 0=Scalar 1=FixedArray 2=Dynamic；SlotOffset 由 VM 加载时计算）
?     global_count:u32
      Global:  module_idx:u8 name:utf8 type:u8
?     native_count:u32
      Native:  name:utf8
?     func_count:u32
      FuncDef: name:utf8 module_idx:u8 nparams:u8 nslots:u8 hasret:u8
               code_off:u32（字偏移） code_words:u32
?     code_bytes:u32 + 指令字节流（u32 小端序列）
?     entry:u32（$eval 的 fid）
```

- 所有 `count` 前置；字符串长度前置；**加载即校验**：魔数/版本/计数与剩余长度一致/类型码与槽位号范围合法/entry < funcCount。任何不一致 → `ECS_ERR_IMAGE`。
- 校验通过后指令流只读共享（嵌入式可直接 XIP/内存映射，无需重定位——没有绝对地址，只有表索引与函数内相对跳转）。

---

## 8. 纯 C 虚拟机设计

### 8.1 文件与构建

```
src/EasyCon.Vm/native/
  ecs_vm.h     公共 API（唯一头文件）
  ecs_vm.c     实现（加载器 + 堆 + 解释器）
  ecs_main.c   CLI harness（测试/CI/演示）
构建：cc -O2 -std=c99 -o ecs-vm ecs_vm.c ecs_main.c
嵌入式：两文件加入工程即可；malloc/free/memcpy 来自 libc，或经宏重定向到池分配器
```

### 8.2 核心数据结构

```c
typedef struct ecs_func {
    const char *name; uint8_t nparams, nslots, hasret;
    const uint32_t *code; uint32_t code_words;
} ecs_func;

typedef struct ecs_obj {           /* 堆对象 */
    uint8_t kind;                  /* STRING/ARRAY/STRUCT */
    int32_t rc;
    union {
        struct { uint16_t *units; int32_t len; int32_t cap; } str;   /* 常量串只读共享 */
        struct { uint8_t elem_type; int32_t len, cap; ecs_value *items; } arr;
        struct { const ecs_struct_def *def; ecs_value *slots; } st;
    };
} ecs_obj;

typedef struct ecs_frame {
    const ecs_func *fn;
    uint32_t ret_pc; int32_t ret_slot;   /* 返回地址与接收槽（-1=无） */
    int32_t caller_base;                 /* 调用者帧基址（诊断用） */
    ecs_value slots[];                   /* 柔性数组 */
} ecs_frame;

typedef struct ecs_vm {
    const uint8_t *image; size_t image_len;     /* ECX 原始字节（表指针指向其中） */
    /* 解析后的表指针：consts/structs/globals/natives/funcs/entry */
    ecs_obj *heap; int32_t heap_cap, heap_top; int32_t *free_list; int free_top;
    ecs_frame **frames; int32_t depth, frame_cap;
    const ecs_host *host; void *ud;
    int32_t step_budget, steps;          /* yield 协议 */
    volatile int cancel;
    int32_t error_func, error_pc;        /* 诊断现场 */
} ecs_vm;
```

### 8.3 主循环（伪代码）

```c
int ecs_vm_run(ecs_vm *vm) {
    if (vm->depth == 0) push_frame(entry);            /* 首次进入 */
    for (;;) {
        if (++vm->steps >= vm->step_budget) { vm->steps = 0; return ECS_YIELD; }
        frame = vm->frames[vm->depth-1];
        ins = fetch(frame);
        switch (op) {
            case EcsOp_AddI: R[A].i32 = R[B].i32 + R[C].i32; ...  /* 热路径直读 union */
            case EcsOp_Call: {
                fid = ext32();
                if (vm->depth >= ECS_MAX_CALL_DEPTH) return ERR_DEPTH;
                callee = &funcs[fid];
                nf = push_frame(callee);
                for (i = 0; i < n; i++) copy_var(nf->slots[i], R[A+i]);   /* §3.3 深拷贝规则 */
                if (hasret) { frame->ret_slot 记录 = A-1; }               /* 返回接收槽 */
                continue;                                                  /* 进入被调帧 */
            }
            case EcsOp_Ret: {
                if (vm->depth == 1) return ECS_OK;                         /* 入口返回 = 停机 */
                dst = 调用者接收槽; copy_var(调用者槽[dst], R[A]); pop_frame(); continue;
            }
            case EcsOp_Jpt: if (R[A].i32) pc += s16; ...
            case EcsOp_CallN: { ret = host->native(ud, nid, &R[A], n, &recv); ... }
            /* ~70 case，见 §4.2 */
        }
    }
}
```

- 无递归 C 调用：调用=压帧+改 PC，返回=弹帧，用户脚本递归不耗宿主栈（只受 `ECS_MAX_CALL_DEPTH` 限制）。
- `ECS_YIELD`：宿主轮询（取消、UI 心跳）后再次调用 `ecs_vm_run` 继续——状态全在 `vm` 内，天然可重入。
- 取消：宿主置 `vm->cancel=1`，下一次 yield 点返回 `ECS_CANCELLED`；阻塞型域操作（wait/key click）内部由宿主自行中断并返回错误码 `ECS_ERR_HOST`。

### 8.4 引用计数协议

| 操作 | rc 规则 |
|------|---------|
| Move/phi 副本/实参暂存 | src 句柄 rc+1 |
| SetVar/传参/RET（深拷贝路径） | 新句柄 rc=1；src 不变（rc 归属原槽，弹帧时统一释放） |
| 槽位被覆写 | 原值句柄 rc−1，到 0 递归释放（数组元素、结构体槽区句柄各 rc−1） |
| SetI 元素写 | 旧元素 rc−1；写入值 rc+1 |
| 弹帧 | 帧内全部槽按覆写规则释放 |
| 常量池字符串 | rc 固定 ≥1（镜像生命周期），不参与释放 |

> 简化实现注意：`Move` 的 rc+1 与覆写释放成对出现即可保证不双释/不悬垂；scrub 单元测试覆盖（§10.1）。

### 8.5 宿主 ABI（`ecs_host`）

```c
typedef struct ecs_host {
    void *ud;
    /* 核心域操作（VM 域指令直达） */
    void (*wait_ms)(void*, int32_t ms);
    void (*key)(void*, uint8_t key, int32_t dur_ms);
    void (*key_state)(void*, uint8_t key, int down);
    void (*stick_set)(void*, uint8_t side, int32_t x, int32_t y);
    void (*stick_click)(void*, uint8_t side, int32_t x, int32_t y, int32_t dur_ms);
    int32_t (*img_label)(void*, const uint16_t *name, int32_t units);  /* 置信度，无标签→-1 */
    int32_t (*rand)(void*, int32_t max);
    int32_t (*time_ms)(void*);
    void (*beep)(void*, int32_t freq, int32_t dur);
    void (*amiibo)(void*, int32_t slot);
    /* 输出（FWRITE stdout 协议落点） */
    void (*print)(void*, const uint16_t *units, int32_t len, int newline);
    int  (*read_line)(void*, uint16_t *buf, int32_t cap);              /* FREAD stdin */
    /* 原生函数表（CallN）：按镜像 native 名表绑定；返回 0=成功 */
    int  (*native)(void *ud, const char *name,
                   ecs_value *args, int32_t nargs, ecs_value *ret);
    /* 可选文件系统（FOPEN/READFILE/... 由 VM 内置原生实现并转发到此；NULL→ECS_ERR_NOSUCHNATIVE） */
    void* (*f_open)(void*, const uint16_t *path, const uint16_t *mode);
    int   (*f_read)(void*, void *h, uint16_t *buf, int32_t cap);
    int   (*f_write)(void*, void *h, const uint16_t *buf, int32_t len);
    void  (*f_close)(void*, void *h);
    int   (*f_eof)(void*, void *h);
} ecs_host;
```

- 所有回调可为 `NULL` → 对应指令/原生返回 `ECS_ERR_NOSUCHNATIVE`（或域操作 no-op + 诊断），绝不崩溃。
- 字符串跨界一律 UTF-16 指针 + 长度（宿主自行转本地编码）。

### 8.6 错误模型

| 错误码 | 场景 |
|--------|------|
| ECS_OK | 正常停机（入口函数返回） |
| ECS_YIELD | 步数预算耗尽（可继续） |
| ECS_CANCELLED | 宿主取消 |
| ECS_ERR_IMAGE | 镜像非法/版本不符/校验失败 |
| ECS_ERR_OPCODE | 非法操作码 |
| ECS_ERR_SLOT | 槽位越界（镜像损坏） |
| ECS_ERR_TYPE | 类型错误（解引用非句柄、Conv 非法、CAT 非数组/字符串、Cont 容器非法） |
| ECS_ERR_INDEX | 数组/字符串/切片/字段越界 |
| ECS_ERR_DIVZERO | 整数除零（对齐求值器 `DivideByZeroException`） |
| ECS_ERR_DEPTH | 调用深度超限 |
| ECS_ERR_NOSUCHNATIVE | 原生未注册 |
| ECS_ERR_HOST | 宿主回调报错 |
| ECS_ERR_OOM | 堆/帧分配失败 |

错误发生：`ecs_vm_run` 返回错误码，`vm->error_func/error_pc` 记录现场（函数 fid + 指令字偏移），harness 据此打印诊断。脚本错误一律停机——ECS 无 try/catch，与求值器的异常传播语义等价。

DOUBLE→字符串格式化：采用最短往返（Ryū/ sprintf `%.17g` 后修剪），对拍用例固定为可精确表示的值（0.5、3.14、1e10）规避两端格式化器差异；通用格式化差异记录为**已知偏差**（§10.2）。

### 8.7 内存预算（ESP32 可行性）

| 项 | 典型脚本（几百行） | 说明 |
|----|--------------------|------|
| ECX 镜像 | 20–80 KB | flash/XIP，可不进 RAM |
| 堆 | 4–64 KB | 句柄表 + 对象；池大小可配 |
| 帧栈 | 深度 512 × 平均 32 槽 × 16B ≈ 256 KB 上限 | 实际深度 3–5 层 ≈ 3 KB；可按 RAM 调 depth |
| VM 自身代码 | ~20 KB ( Thumb-2 -Os) | |

---

## 9. 内置函数与原生表

### 9.1 三级分类

| 级别 | 成员 | 落点 |
|------|------|------|
| 内联伪函数 | WAIT RAND LEN APPEND STRING INT | Binder/Lowerer 已内联为 SSA 指令 → 域指令（`BuiltinFunctions.IsIntrinsic`） |
| 域操作 | KEY STICK @标签 TIME | KeyI/KeyV/KeySt/Stick*/Img/Time 指令 |
| VM 核心原生（CALLN） | FWRITE FREAD FOPEN FCLOSE FEOF READFILE WRITEFILE APPENDFILE FILE_EXISTS ALERT ARG ENV ENCODE JQ OCR_CONF APP **BEEP AMIIBO** | `CallN`；VM 内置实现或转发宿主。注：BEEP/AMIIBO 不是内联伪函数，SSA 里是 `StaticCall` → 走 CALLN，本设计预留的 `Beep`/`Amiibo` 操作码暂不发射（保留） |
| 宿主专属 | `__CAPTURE__` `__OCR__` `__ROI__` `__OCR_INIT__` 及一切 EXTERN FFI | `CallN` → `host->native` 按名分发 |

### 9.2 FWRITE stdout 行断协议（PRINT 路径，必须逐字对齐）

stdlib 的 `PRINT/ print` 是 ECS 函数：`FWRITE $STDOUT, $output`。VM 内置 FWRITE 对句柄 1 的行为（与 `BuiltinCallable.ImplFWrite` 一致）：

```
state.pending_break 初始 false
s = 参数字符串
output = s 以 '\' 结尾 ? s[0..len-1] : s
host->print(output, newline = !state.pending_break)
state.pending_break = s 以 '\' 结尾
返回 len(s)
```

句柄 0 = FREAD stdin（`read_line`），句柄 2 = print(newline=true)；文件句柄 → `f_*` 回调。

### 9.3 平台能力模型（对齐 MODULE_DESIGN.md §5 平台抽象层）

宿主通过能力位掩码声明**本平台已实现的能力**；VM 只提供已实现能力，未实现的原生/输出
**静默忽略并返回默认值**（不报错）——单片机上 print/alert/beep 无意义，直接忽略：

```
ECS_CAP_PRINT(1<<0)  ECS_CAP_ALERT(1<<2)  ECS_CAP_BEEP(1<<2 组见 ecs_vm.h)
ECS_CAP_FILE  ECS_CAP_CAPTURE  ECS_CAP_FFI  ECS_CAP_STDIN
桌面宿主 = ALL_DESKTOP；单片机 = 0（脚本副作用仅按键/摇杆/延时）
```

| 原生 | 能力缺失时行为 |
|------|----------------|
| FWRITE（stdout/stderr） | 静默忽略输出，仍返回长度（脚本计数语义不变） |
| FWRITE（文件句柄）/ FOPEN 族 | 返回 -1/默认值 |
| ALERT / BEEP | 忽略 |
| OCR_CONF | 返回 0 |
| 采集卡洞 / EXTERN FFI | 返回空串/默认值 |
| ARG / APP / ENV / TIME / RAND | 平台核心，始终可用 |

### 9.4 EXTERN FFI

EXTERN 声明的函数经 NativeLoader 在 C# 侧 P/Invoke。VM2 中它们进原生表按名分发；**C VM 不 dlopen**——宿主在 `host->native` 中自行绑定（桌面宿主可实现 dlopen 转发，嵌入式返回 NOSUCHNATIVE）。DLL 名/导出名在编码期折进原生表名（`"kernel32.dll!Sleep"` 形式），格式无需额外字段。

---

## 10. 测试与验收方案

### 10.1 单元测试矩阵（NUnit，`test/EasyCon.Tests/Bytecode/`）

| 组 | 用例 |
|----|------|
| 编码器 | 逐语句类：算术/比较/逻辑短路（and/or 降级）/赋值/复合赋值；phi 菱形与 swap 环；跳转反转模板触发；常量池去重；诊断（槽位溢出用构造脚本） |
| 拷贝语义 | `SetVar` 深拷贝隔离（改副本源不变）；数组元素原地写；结构体字段读副本性 |
| 序列化 | ECX 写→读 round-trip 逐表一致；损坏注入（截断/坏 magic/坏计数）→ `ECS_ERR_IMAGE` |
| 链接器 | 多模块函数 fid/全局槽/常量重映射断言；`<builtin>` 模块归位；结构体表按名去重 |
| 反汇编 | 黄金文件快照（固定脚本 → 反汇编文本入库） |

### 10.2 对拍金标准（C# 评测器 vs C VM）

- **语料**：`test/EasyCon.Tests/Bytecode/corpus/*.ecs`——按键时序（含变量时长）、循环嵌套 + BREAK n/CONTINUE、递归（阶乘/斐波那契）、字符串（中文 LEN/切片/CAT/in）、数组（init/index/slice/APPEND/LEN）、结构体（嵌套/固定数组字段）、PRINT 行断协议、函数重载、全局变量。**不含** RAND/TIME（不确定性）与 FFI/采集卡（宿主缺失）。
- **方法**：同一脚本 (1) `SsaEvaluator` + mock IoAdapter/ICGamePad 采集输出事件流；(2) ECX → harness（stub host 采集同构事件流）；(3) 逐行比对（按键序列 = (key,dur) 向量、stdout 精确串）。
- **harness**：`ecs-vm run image.ecx [--trace]`；事件流输出为 TSV 到 stdout。
- **CI**：无 cc 环境跳过对拍（仅 C# 单测）；`ci/build-vm.sh|bat` 构建后全量。已知偏差白名单：DOUBLE 字符串化末位（§8.6）。

### 10.3 反汇编快照

`ecs-vm dis image.ecx` 输出文本入库；编码器任何行为变化在 review 中可读。

---

## 11. 实施计划（阶段独立可合并）

| 阶段 | 内容 | 交付判据 | 状态 |
|------|------|----------|------|
| P1 设计 | 本文档 | 评审通过 | ✅ |
| P2 指令/模型骨架 | EcsOpcode + BytecodeModule（镜像对象模型） | 编译通过 | ✅ |
| P3 编码器 | BytecodeEncoder + ModuleSplitter（内存路径） | 单测 §10.1 编码器组全绿 | ✅（槽位复用 + mem2reg 落地） |
| P4 链接与序列化 | EcxLinker + EcxWriter + 反汇编 | round-trip + 快照测试全绿 | ✅（ECM v2 接口区 + 模块链接） |
| P5 C VM | ecs_vm.h/.c + harness | 镜像加载校验 + 纯算术/控制流脚本对拍全绿 | ✅（三方交叉验证通过） |
| P6 全量对拍 | 堆语义（字符串/数组/结构体）+ 域操作 stub | §10.2 语料全绿 | ✅（FullChain 五路径 + 语料组全绿） |
| P7 集成（可选） | C# 侧 P/Invoke 包装（对齐 EzCv 预编译原生模式） | GUI 可选切 VM2 后端 | 待做（VM2 交付后唯一剩余项，可选） |

每阶段不依赖后继阶段即可合并（P5 前的任何中断都留下可用的编译器子集与测试）。

## 12. 取舍记录（ADR）

| 决策 | 选择 | 否决项与理由 |
|------|------|--------------|
| D1 指令编码 | 定长 32 位 + EXT | 变长（Wasm/Lua 前段）：解码分支多、C 代码膨胀 ~30%；代码体积 +15% 在脚本规模下无关紧要 |
| D2 指令风格 | 寄存器（三地址） | 栈式：SSA 本身是三地址，直接映射零重写；栈式需额外栈机语义且热点多 visit |
| D3 内存管理 | 引用计数 | 迁移 GC：VM 体积/复杂度翻倍，嵌入式 GC 抖动不可接受；环泄漏实践不可达（§3.2） |
| D4 字符串 | UTF-16 code unit | UTF-8：字节长度与 C# `string.Length` 语义漂移，中文 LEN/索引/切片全部错位 |
| D5 槽位宽度 | 8 位 | 16 位：iABC 4 字节格式放不下三个 16 位槽；255 槽/函数对脚本语言充分（SSA 无寄存器复用，大函数以诊断兜底） |
| D6 模块化 | 内存拆分 + 链接器先行，磁盘 ECM 接口绑定后置 | 一步到位：需要 Binder 面向接口重写，风险与体量失控（§6.5） |
| D7 `!=` | Eq+Not 展开 | 专用 NEQ 指令 ×5 类型：省一条指令但增 5 个操作码与解码分支 |
| D8 除零 | 运行时错误 `ECS_ERR_DIVZERO` | ~~结果 0~~（初稿错误；复核确认求值器抛 `DivideByZeroException` 脚本停机，必须对齐） |
| D9 phi | 前驱边并行副本 | 寄存器合并/去 phi 数据流分析：正确性风险高，收益属 SSA 优化器 |

## 13. 风险与开放问题

| # | 风险/问题 | 缓解 |
|---|-----------|------|
| R1 | SSA 优化器迭代改变 phi/块结构，编码器不变式失效 | 编码器入口跑不变式断言（块终止、phi 臂=前驱），破坏即 BC_INTERNAL（§5.7） |
| R2 | DOUBLE 字符串化两端格式器差异 | 对拍语料避开不确定值；差异白名单显式化（§8.6/§10.2） |
| R3 | 255 槽上限被超大函数击穿 | 现网脚本审计 + 诊断前置；真超限再引入槽位复用（活性分析）作为 P8 |
| R4 | VisionSource stdlib 的 `OCR(5参)` 自递归（现有前端问题，与 VM2 无关） | 单独 issue 修复；不阻塞本设计 |
| R5 | 引用计数实现 bug（双释/泄漏） | §8.4 协议表 + 专项单测（覆写/弹帧/SetI 路径全覆盖） |
| Q1 | `$eval` per-module `<init>` 拆分时机 | 已解决：`docs/ModuleSystem.md` §5.4——lib 顶层语句在模块编译期进入 `<init:module>`，链接器按拓扑序合成 `<main>` |

---

## 14. 语义复核记录（P0，编码器动工前）

> 对本设计与 `SsaEvaluator`/`ScriptArray`/`EcsStruct`/`BoundValue` 源码逐点核对的结论。
> 分级：**✅ 已验证一致**（可放心实现）/ **🔧 已修正**（初稿错误，本文档已改）/ **⚠️ 待决议**（需拍板，附建议）/ **🛡 实现期断言**（编码器/VM 必须防御）。

### 14.1 已验证一致（✅）

| 编号 | 语义点 | 证据 |
|------|--------|------|
| V-01 | 变量赋值/传参/返回：数组深拷贝（元素标量深、引用浅共享）、结构体逐字节深拷贝、字符串共享 | `CopyHandleForStore`/`ValueToSlot`/`ValueToCache`；`EcsStruct(def,srcPtr)` 字节复制 |
| V-02 | 数组 Clone 语义 = 新容器 + 元素浅拷贝（`IntArray._data` 复制；StringArray/ObjectArray 元素引用共享） | `ScriptArray.Clone()` 各实现 |
| V-03 | SETI 原地写 + 元素按元素类型物化读（`ExecuteLoadIndex` 经 `ValueToCache(elemType)`） | `ScriptArray.SetItem`/`ExecuteLoadIndexToCache` |
| V-04 | CAT：任一侧 STRING → 双侧字符串化拼接；否则仅数组拼接且元素类型必须一致；其余运行时错误 | `ExecuteConcatToCache`/`Value.Concat` |
| V-05 | Cont：字符串子串（Ordinal）；数组元素类型不匹配 → **false**（非错误） | `Value.Contains` |
| V-06 | IF/WHILE 条件经 `BindConversion(→Bool)` 强制为布尔 | `Binder.Statements.cs:75` |
| V-07 | BEEP/AMIIBO 非内联伪函数（`IsIntrinsic` 列表外）→ SSA `StaticCall` → 必须 CALLN | `BuildinFuncs.cs` IntrinsicFunctions |
| V-08 | AMIIBO n>9 静默忽略 | `ImplAmiibo` |
| V-09 | RAND：`Random.Next(0)=0`、负数异常 | .NET 契约 |
| V-10 | 结构体仅标量/内联 STRUCT/固定数组字段；动态数组字段 `NotSupportedException` | `EcsStruct.GetFieldElement` |
| V-11 | extern 调用与用户调用同为 `SsaOp.Call/StaticCall`，以符号是否在 `Functions` 区分 | `SsaBuilder.Expressions.cs:EmitCall` |
| V-12 | SSA 常量折叠遇除零主动放弃（不折叠成错误结果） | `SsaLattice.cs:188` |
| V-13 | **PRINT 不在 `BuiltinFunctions.All`**（符号已定义但未注册 root scope、非 intrinsic）→ `PRINT x` 解析到 stdlib 源码函数 → `CALL fid` → 函数体 `FWRITE $STDOUT, $output` → `CALLN "FWRITE"`。整条链是"跨模块用户函数调用 + 核心原生"，无特殊通路 | `BuildinFuncs.cs:86` `All` 表 + `StdLib.StdSource` |
| V-14 | **TOSTR 两层格式不一致（C# 自身行为）**：顶层 `ConvToString` 的 PTR = 十进制 `I64.ToString()`；容器内 `Value.ToString()` 的 PTR = `0x{X}` 十六进制、STRUCT = `struct:名`。ARRAY = `[..]` 逗号空格分隔递归 | `SsaEvaluator.ConvToString:829` vs `BoundValue.ToString:324` |
| V-15 | `LegacyCompat` 静态默认 **true**：`TIME $x`/`RAND $x` 语句形式被旧语法分支拦截（对拍语料避免这两种书写形式） | `SyntaxTree.cs:12` |
| V-16 | stdlib 内部全局（`$STDIN` 等）与 stdlib 函数体共享同一符号实例（函数体绑定用 `fileBinder._scope`）→ 模块内全局引用闭合，跨模块全局不可见复核成立 | `Binder.BindProgram:112` |
| V-17 | ModuleSystem 论断复核：环检测（visiting 集）、alias 过滤（`aliasedFuncDecls`）、多文件全局语句报错（`OnlyOneFileCanHaveGlobalStatements`）均与设计描述一致 | `ImportResolver`/`Binder.cs:145-195` |
| V-18 | **实证验证**（CLI `ir` 命令，probe.ecs：全局+FOR+IF-ELSE+PRINT+CAT+按键）：① FOR 含端点循环降级为「头判断 BB1→体 BB3→`$i==3` 提前出口 BB2 / 否则 BB4 递增回边」，phi 仅在头、回边唯一——边副本模型完全适用；② stdlib 顶层语句（`$STDIN/$STDOUT/$STDERR`）确实以 `store.global` 出现在 `$eval` 开头（导入序）；③ `PRINT` 解析为 stdlib 用户函数（`staticcall PRINT`），其体内 `FWRITE` 的首参经 `ConvIntToPtr` 传 `STDOUT`；④ `KeyPress` 的 duration 以常量 SSA 值为 Arg0 → 编码器可发 `KeyI` 立即数形式；⑤ 附录 A 的结构化假设（phi 在头/边副本/prologue/参数槽 0 起）与真实 SSA 逐一吻合 | CLI `ir` 实测输出 |

### 14.2 已修正（🔧）

| 编号 | 初稿说法 | 修正 |
|------|----------|------|
| SR-01 | "除零 → 结果 0" | 除零 = 停机错误 `ECS_ERR_DIVZERO`（§3.4/§8.6/D8 已改） |
| SR-02 | BEEP/AMIIBO 为域操作指令 | 改为 VM 核心原生 CALLN；`Beep`/`Amiibo` 操作码保留不发射（§9.1 已改） |
| SR-03 | Slice "两端 clamp" | 越界一律运行时错误（§4.2 已改） |
| SR-04 | 结构体支持 Dynamic 动态数组字段 | 不支持，字段种类改三种（§3.3 已改，`EcsFieldKind` 同步删枚举） |
| SR-05 | RAND(n) "n≤0 → 0" | n=0→0；n<0→错误（§3.4 已改） |
| SR-12 | TOSTR 单一格式（结构体 `{field: v}`、PTR 十进制） | 两层上下文：顶层 PTR=十进制/容器内=十六进制、STRUCT=`struct:名`（§3.4 已改；对应 V-14） |

### 14.3 待决议（⚠️，附建议）

| 编号 | 问题 | 现状证据 | 建议 |
|------|------|----------|------|
| SR-06 | **DOUBLE→INT 溢出**：C# `(int)double` 越界结果未定义（实践 = INT_MIN/饱和），C 转换是 UB | `ConvDoubleToInt` 直接强转 | ✅ **已决议**：VM 定义为饱和（<INT_MIN→INT_MIN，>INT_MAX→INT_MAX，NaN→0）；对拍语料避开溢出值 |
| SR-07 | **移位量掩码**：C# 32 位按低 5 位掩码，C 是 UB | `ShlInt` 直接 `<<` | ✅ **已决议**：VM 显式 `&31`（u64 `&63`），对齐 C# |
| SR-08 | **字符串字段生命周期**（native 指针归属） | `PtrToString`/SetField 实现未审计 | VM 字段槽存句柄（写=深拷贝入、读=共享出）；实现 SETF 字符串字段时补一条单测锁定 |
| SR-09 | **`(int)double` 用于数组索引/域操作时长**：负数/超大值直接进 C# 索引器会抛异常，语义=错误码 | 索引越界抛异常 | VM 统一：所有"期望 INT 的运行时读取"按 `I32` 位读取即可（tag 无关），越界在各自指令处报 INDEX |
| SR-10 | `IF $a`（裸 int 条件）是否被 `BindConversion(→Bool)` 拒绝 | 转换表未审计 | 实现对拍时用例覆盖；若合法则 Jpt 真值定义（i32≠0）天然兼容，无需改动 |
| SR-11 | DOUBLE 的 TOSTR 格式（文化/精度） | `double.ToString()` 当前文化 | VM 最短往返；对拍语料限定精确可表示值；文化差异列入已知偏差白名单 |

### 14.4 实现期断言（🛡 编码器/VM 必须防御）

| 编号 | 断言 | 违反时 |
|------|------|--------|
| A-01 | SSA 不变量：块必终止、phi 臂数=前驱数、臂不指向自身（占位符已被 FillPhiArms 重建） | `BC_INTERNAL` |
| A-02 | `Return` 指令只出现在 `IsReturn` 块（求值器允许 Return 后块内还有指令被继续执行——编码器统一在块末发射 Ret） | `BC_INTERNAL` |
| A-03 | `CondBranch` 的 TrueSuccessor≠FalseSuccessor（若出现：只发射一份边副本） | 防御性单份副本 |
| A-04 | 每个函数 `nSlots ≤ 255`、常量池 ≤ 65535、`Jpt/Jpf` 偏移 ≤ s16（超出报诊断；Jmp s24 兜底大函数） | `BC_SLOT_OVERFLOW` 等 |
| A-05 | 调用/数组字面量参数的暂存区共享（每函数一个 staging 区，覆写释放语义依赖 VM 的"槽位覆写 rc−1"） | VM 单测锁定 |
| A-06 | 全局变量槽位只分配给**被 SSA 引用过的**全局（未被引用的不进镜像） | — |
| A-07 | 函数 fid 映射使用 `FunctionSymbol` **值相等**（与 `SsaEvaluator._functions.ContainsKey` 一致，跨模块同签名函数合并语义保持） | — |
| A-08 | 非 void 函数的 Return 指令 Arg0 必非空（void 函数遇 Return 值 → `BC_INTERNAL`）；`hasret` 由 `ReturnType != Void` 决定，与 Ret 形态必须一致 | `BC_INTERNAL` |
| A-09 | SETI 容器经临时槽浅共享传播时，原地修改对变量可见（与求值器 `SetItem` 同对象语义一致）——VM rc 协议单测锁定 | 单测 |
| A-10 | Slice 的"省略端"判定照抄求值器：`ExtraArgs[0]` 存在**且静态类型为 INT** 才视为 end 槽，否则 ext=0xFFFFFFFF | — |
| A-11 | 全局变量槽按 SSA 首次引用序分配（stdlib 的 `$STDIN` 系列天然占据前几槽）；槽位分配顺序不影响行为，仅要求确定性 | — |
| A-12 | 调用结果 `Uses==0` 时 `hasret=0`（省接收槽与回 Move；副作用调用保留，如 `staticcall FWRITE` 其返回值无人使用） | 可选优化，实现于编码器 |

### 14.5 复核方法说明

每条结论均以当前分支源码为证据（文件+行为引述），不依赖文档记忆。后续 Binder/SSA 优化器行为变化时，本节清单即回归测试的断言清单（A 组直接映射为编码器单测）。

---

## 附录 A：端到端示例

> 目的：把 §3–§8 的抽象规格落到具体指令。SSA 列为**风格化示意**（真实形态取决于 Lowerer/优化器），字节码列为编码精确的产物；操作码数值以 `EcsOpcode.cs` 枚举为准，此处用助记符。

**ECS 源**

```ecs
$count = 0
FOR $i = 1 TO 3
    $count += $i
NEXT
```

**SSA（风格化）**：`$eval` 函数，4 个块（PRINT 等后续语句省略）

```
B0 (entry, 无前驱):
  v1 = ConstInt 0
  v2 = ConstInt 1
  v3 = ConstInt 3
  StoreGlobal $count ← v1
  StoreLocal  $i     ← v2
  Branch → B1
B1 (loop header, preds: B0, B2):
  v6 = Phi[$i]:  (B0 → v2), (B2 → v8)
  v4 = LeInt(v6, v3)                ; $i <= 3
  CondBranch v4 → B2(真), B3(假)
B2 (body):
  v5 = LoadGlobal $count
  v7 = AddInt(v5, v6)
  StoreGlobal $count ← v7
  v8 = AddInt(v6, v2)               ; $i + 1
  StoreLocal $i ← v8
  Branch → B1                       ; 回边
B3 (exit): … 后续语句 …
```

**槽位分配**：局部符号槽 `$i`→0（`$count` 是全局，不在帧内）；SSA 临时槽按值顺序：v1→1, v2→2, v3→3, v4→4, v5→5, v6→6, v7→7, v8→8；nSlots=9。全局镜像槽 `$count`→0。

**字节码**

```
addr  指令                说明
── prologue（常量物化，§4.3）──
 0    LoadI  1, 0          v1 = 0
 1    LoadI  2, 1          v2 = 1
 2    LoadI  3, 3          v3 = 3
── B0 ──
 3    StoreG 0, 1          $count = v1
 4    SetVar 0, 2          $i = v2（深拷贝语义，标量退化为位拷贝）
 5    Move  6, 2           B0→B1 边副本（§5.3）：v6(Phi) ← v2
── B1（header）──
 6    LeI   4, 6, 3        v4 = v6 <= v3
 7    Jpf  4, +7           假 → B3@15（15-(7+1)=7）
── B2（body）──
 8    LoadG 5, 0           v5 = $count
 9    AddI  7, 5, 6        v7 = v5 + v6
10    StoreG 0, 7          $count = v7
11    AddI  8, 6, 2        v8 = $i + 1
12    SetVar 0, 8          $i = v8
13    Move  6, 8           B2→B1 回边副本：v6(Phi) ← v8（单副本无交换环；
                           交换环时经 SCRATCH 槽，§5.3）
14    Jmp  -9              → B1@6（6-(14+1) = -9，s24 负偏移）
── B3（exit）── 15 …
```

展示的要点：prologue 常量物化；`SetVar/StoreG` 变量写（深拷贝语义）；phi 挂在**前驱边**而非块内；回边负偏移 `Jmp`；`Jpf` 的 s16 出循环。本例无交换环——若循环写成 `x, y = y, x+y` 形态，同一边的两个 phi 构成交换，需要 SCRATCH 槽打破。

**ECX 镜像（本例）**

```
"ECX2" ver=1 flags=0
consts:      0 条（无超范围常量、无字符串）
structs:     0
globals:     1  { "$count", module=main, type=int }
natives:     0
funcs:       1  { "$eval", module=main, nparams=0, nslots=9, hasret=0, code_off=0, code_words=16 }
code_bytes:  64（16 × 4 字节指令流）
entry:       0
```

**C VM 执行视角**：`ecs_vm_run` 压入 `$eval` 帧（9 槽）→ prologue 物化常量 → 循环 3 次（`LoadG/AddI/StoreG/Jmp` 纯槽运算，零堆分配）→ 假分支出循环 → `Ret0` 弹帧 → `ECS_OK`。
