# VM2 — ECS 字节码虚拟机规范

> **定位**：ECS 脚本的编译目标与双端执行规范——C# `EcxInterpreter`（桌面）与 C `ecs_vm`（单片机）
> 消费同一 ECX 镜像。本文是**现状规范**：指令集、二进制格式、宿主 ABI、分层模型。
> 权威源：`EcsOpcode.cs` / `EcsFormat.cs`（C# 侧）与 `ecs_vm.h`（C 侧）逐项对齐；
> 双端语义一致性由 corpus/FullChain/ABI 对拍测试锁定（`test/EasyCon.Tests/Bytecode/`）；编译链路见 `docs/Pipeline.md`；
> 模块系统见 `docs/ModuleSystem.md`。冲突时以代码为准并修本文。

## 1. 分层架构

宿主交互按四级分层，每层有独立的机制与宿主姿态：

| 层 | 内容 | 机制 | MCU 参考宿主姿态 |
|----|------|------|------------------|
| **L0 机器** | 算术/比较/控制流/容器/结构体/调用 | VM 核指令 | 天然支持 |
| **L1 基础能力** | `WAIT`、按键、摇杆、`RAND` | 专用指令（`WaitI/KeyI/Stick*/Rand`），直连宿主回调 | 全支持 |
| **L2 平台 syscall** | `FWRITE/FREAD/文件族/ALERT/ARG/ENV/APP/TIME/BEEP/AMIIBO/OCR_CONF` | `CallN` EXT 旗标 + **编号**（§7.3），语义在宿主参考实现 | 参考桩：静默/默认值（TIME=0、FWRITE 返 len、串返空、AMIIBO 槽 0–19 越界静默） |
| **L3 动态原生** | 采集洞 `__CAPTURE__` 系、EXTERN FFI、std 加速洞 `__ENCODE__/__JQ__/__CTC_FOLD__`、推理 `NET_*`（含 `NET_NORM`/标签表）、YOLO 解码 `__YOLO_DECODE__` + `YOLO_*` 访问器、det 找框 `__DET_BOXES__` + `DET_*` 访问器 | `CallN` 名表**按名**分发 | 桌面缺能力按 C# 缺省值表降级（OCR/ROI/CTC_FOLD/ENCODE/JQ/NET_LABEL→""、NET_LOAD→-1、NET 句柄族→0、NET_ARGMAX/NET_LABELS→-1、NET_OUT→0.0、YOLO/DET 洞/COUNT→0、访问器→-1/-1.0、FFI→0）；**C/MCU 无名表降级与 FFI——native 缺失一律 NOSUCHNATIVE**；strict_caps 双端恢复响亮 |

> **L3 名表面准入规则**：只收两类——(a) 能力洞（语义由 CapabilitySet 定义：采集/OCR/视觉/推理）；(b) std 加速洞（std 定 API 与语义，宿主仅作性能加速器，如 `__CTC_FOLD__`/`__JQ__`/`__ENCODE__`）。实用工具的 API 与语义一律归 std，不得直接占用本面。

组件清单（现状）：

| 文件 | 职责 |
|------|------|
| `src/EasyCon.Script/Bytecode/EcsOpcode.cs` | 指令集枚举、`EcsSyscall` 编号 ABI、`EcsImageFeatures` 特征位 |
| `src/EasyCon.Script/Bytecode/EcsFormat.cs` | 编码格式/EXT/结果槽权威表（漏登自检） |
| `src/EasyCon.Script/Bytecode/BytecodeEncoder*.cs` | SSA → 指令序列（槽位分配、phi 降级、常量惰性物化） |
| `src/EasyCon.Script/Bytecode/SlotAllocator.cs` | 槽位分类/池化/结算（含死 φ 免分配，§4.3） |
| `src/EasyCon.Script/Bytecode/EcxLinker.cs` | 模块合并、死函数/死存储/死元素消除、特征掩码、入口合成 |
| `src/EasyCon.Script/Bytecode/DeadStoreSweep.cs` | 链接期死存储清扫（反向活跃性，防线 3） |
| `src/EasyCon.Script/Bytecode/EcxWriter.cs` | ECX 序列化（stripDebug 产 MCU 发布版） |
| `src/EasyCon.Script/Bytecode/EcxInterpreter.cs` | C# 解释器 + `EcxHost`（桌面参考宿主） |
| `src/EasyCon.Script/Modules/*` | 模块图/独立编译/接口缓存（`docs/ModuleSystem.md`） |
| `src/EasyCon.Vm/native/ecs_vm.h|.c` | C 虚拟机公共 API + 实现（加载器/堆/解释器，纯调度） |
| `src/EasyCon.Vm/native/ecs_main.c` | CLI harness = MCU 参考宿主（L2 参考桩 + L1 回调） |
| `ci/build-vm.sh` | 普通 / budget 构建入口；budget 与 CI 强制模式不允许缺编译器时跳过 |

---

## 2. 值模型

### 2.1 值表示：C# 16 字节，C 8 字节

C# `TaggedValue` 保持 16 字节内联载荷。C `ecs_value` 改为 8 字节：

```c
typedef struct ecs_value {
    uint8_t tag;  uint8_t _pad[3];
    union { int32_t i32; uint32_t u32; };
} ecs_value;
```

| Tag | 值 | 载荷 | 备注 |
|-----|----|------|------|
| VOID=0 | — | — | 句柄 0 同时表示 null |
| BOOL=1 / BYTE=2 / INT=3 / UINT=4 | i32 | BOOL 规范化 0/1；UINT 无符号解释 |
| UINT64=5 | C# i64 / C 对象池箱句柄 | 数组内为裸存 8B 位模式 |
| DOUBLE=6 | C# f64 / C 对象池箱句柄 | 数组内为裸存 8B IEEE754 |
| STRING=7 | 32 位句柄 | UTF-16LE code unit 存储；LEN 按 code unit |
| ARRAY=9 | 句柄 | C 按元素 tag 定宽打包；宽元素 8B，内联标量和引用 4B |
| PTR=10 | C# i64 / C 对象池箱句柄 | 原始指针位模式经宿主辅助 API 读写 |
| STRUCT=12 | 句柄 | 字段按布局展开的槽区 |

**标签可观察性**：运行时只有 STRING/ARRAY/STRUCT 的标签被解引用检查；BOOL/BYTE/INT/UINT 的标签差异不可观察（读取方式由编译期类型特化的指令决定）。编码器可用 `LoadI`（INT 标签）装 BYTE/UINT 常量，仅 BOOL 用 `LoadBool` 保持 0/1 规范化。

双端共享标签编号、指令和 ECX1 格式，不共享原生值槽布局。C 原生宿主必须重新编译，
宽值使用 `ecs_vm_arg_wide` / `ecs_vm_ret_wide`，DOUBLE 使用
`ecs_vm_arg_double` / `ecs_vm_ret_double`；不得读写已删除的 `i64/f64` 字段。
返回辅助函数创建出生引用，由 CallN 接管。池耗尽返回 `ECS_ERR_POOL`，非法宽返回 tag 返回
`ECS_ERR_TYPE`；宿主忽略返回码时 VM 仍传播错误。内存档案与迁移说明见 [VmBudget.md](VmBudget.md)。

### 2.2 堆：句柄表 + 引用计数

- 句柄从 1 起，0 = null；句柄表 + 空闲链复用，恒不移除/移动元素。
- **常量字符串 pinned**：双端使用带 bit31 标记的常量池索引，不占运行对象池，不参与引用计数；
  C 字符数据直接借用镜像，因此镜像必须在 VM 生命周期内有效。运行期字符串（CAT/切片/转换）新建句柄。
  字符串不可变，复制一律共享。
- 数组：`{elem_type, items[]}`；结构体：`{layout*, slots[]}`，槽区按 StructDef 布局展开。
- **引用计数不回收环**：ECS 无闭包，实践值为树形；停机随 VM 销毁全量释放。
- 自洽不变量：`alloc − release = live = 非空槽位数`（`HeapCountsConsistent` 锁定）。
- C 宽标量箱不可变，参与引用计数。数组读取宽元素产生出生箱，复制/拼接时直接复制 8B 位模式；
  membership / TOSTR 的临时箱必须释放。对象池耗尽是可诊断错误，不能静默返回 0。

### 2.3 拷贝语义（S-01，对拍权威）

| 场景 | 行为 |
|------|------|
| Move / phi 副本 / 实参暂存 | 浅共享 + rc+1 |
| SetVar / StoreG / CALL 实参 / RET 返回 | 数组/结构体一层深拷贝、字符串共享（**COW**：源 rc==1 时共享移交跳过拷贝，`ECS_COW=0` 可关） |
| GetF 读 | Scalar 位拷贝；FixedArray 物化新堆数组；Nested 新结构体 |
| SetI / PutF 写 | 原地写（旧元素 rc−1，新值 rc+1） |

值语义由两个不变量构成：SSA 值不可变（浅共享无别名）+ 变量写路径全部深拷贝（COW 只发生在
不可见的出生临时槽与接收槽之间）。

### 2.4 运算语义（要点）

| 运算 | 语义 |
|------|------|
| 四则 | INT/UINT 32 位回绕；UINT64 64 位回绕；DOUBLE IEEE754 |
| `/` DivI | 向零取整；`\` RDivI = `(a+b/2)/b` |
| 除零 | 所有 Div/Mod → `ECS_ERR_DIVZERO` 停机（S-02） |
| 移位 | 显式 `&31`（64 位 `&63`）掩码（S-03） |
| D2I | 饱和（越界取极值，NaN→0）（S-04） |
| 比较 | 结果 BOOL；EqS 字符串 code unit 精确相等 |
| `in`（Cont） | 字符串子串 Ordinal；元素 tag 不匹配→false 非错误；非法容器→`ECS_ERR_TYPE`（S-09） |
| Cat | 任侧 STRING→双侧 TOSTR 顶层拼接；数组→元素 tag 一致才拼（S-11） |
| TOSTR | 两层上下文：顶层 PTR=十进制；容器内 PTR=`0x{X}`、STRUCT=`struct:名`（S-07/08） |
| Rand | n=0→0；n<0→错误；[0,n) 宿主注入 RNG（S-12） |
| GetI/Slice | 越界不 clamp → `ECS_ERR_INDEX`（S-10/S-15） |

Conv 种类（C 字段）：`IntToDouble DoubleToInt IntToUInt UIntToInt IntToByte BoolToInt IntToUInt64
UIntToUInt64 UInt64ToInt IntToPtr PtrToInt UInt64ToPtr PtrToUInt64 DoubleToUInt64 UInt64ToDouble
ToStr ToInt`。
       ToInt：数值类直转；STRING 输入为 PC 端十进制解析（失败 0），MCU 端静默返回 0。

---

## 3. 指令集

### 3.1 编码格式（小端；定长指令，ECX1 平铺容器）

| 格式 | 布局（v3 定长，即线格式） | 用于 |
|------|------|------|
| iABC | `op:8 \| A:8 \| B:8 \| C:8`（4B） | 三地址运算、域操作 |
| AsBx | `op:8 \| A:8 \| sBx:16`（4B） | LoadI（s16 立即数；超界编译期归一 LoadK）、Jpt/Jpf（s16 **字节**偏移） |
| ABx | `op:8 \| A:8 \| Bx:16`（4B） | LoadK / NewArrE / NewSt / Img |
| IsJ | `op:8 \| sJ:24`（4B，**字节**偏移） | Jmp |
| Ext | iABC + 后随 `data:32`（8B） | Call/CallN 目标、Slice end 槽、GetFI/PutFI 索引槽、StickP/StickPv、NewArrV 元素类型码、**WaitI/KeyI 时长**（v3 起） |
| IabcJ | iABC + 后随 `jump:32`（8B） | **ForStep/CmpJ** 第 4 操作数（跳转**字节**偏移；v3 起） |

> **单流化 + v3 定长（已实施）**：内存唯一表示为解码形态 `EcsInstruction`（完整 int32 操作数，
> Jump = 相对指令下标）；文件的 **v3 定长指令流**（ECX1 `.text`）与内存按上表一一对应，
> 投影/抬升由 `InstructionCodec` 单处换算（线格式跳转 = 相对**字节**偏移：目标 = 指令起始 +
> 本指令字节数 + delta）。操作数位宽由格式锁定：槽位 u8 ≤254（寄存器 ≤255 帧长上限，无哨兵值——
> Call/CallN 的 C 恒为接收寄存器号）；AsBx=s16；ABx=Bx16（≤65535，常量池 16 位索引是 pinned 句柄编码前提）；
> IsJ=s24。越界一律**编译期响亮失败**。
> 权威表：C# `EcsFormat.BuildTable`（漏登自检抛出）↔ C `ecs_op_words`（字数 1=4B/2=8B）。

**数据字**：12 条带字指令（Ext 8 条 + ForStep/CmpJ/WaitI/KeyI）的数据字是「数据」——数值可能
恰好等于某个操作码，一切线性扫描必须按 `EcsFormat.WordCount` 步进跳过（历史 F4 缺陷铁律，
v3 随定长布局回归）。

容量上限（R-3）：槽位上限 254 已前移到**编译期**（v3 槽位 u8，与宿主档案 `ECS_MAX_SLOTS=255`
对齐）；加载期校验保留（nslots ≤ 宿主档案）。Jpt/Jpf 偏移越界由编码器跳转反转模板消化，
超 s16 由 `Project` 响亮失败。

### 3.2 指令表

权威枚举 = `EcsOpcode.cs`（C 侧 `ecs_vm.h` 数值一一对应）。分组摘录（`R[a]` = 帧槽）：

```text
# 常量与移动
LoadI    a, sBx          R[a] = sBx（INT 标签）
LoadK    a, Bx           R[a] = 常量池[Bx]；串常量驻留（§2.2）
LoadBool a, b            R[a] = (b!=0)
Move     a, b            R[a] ← R[b]（浅共享，rc+1）
SetVar   a, b            R[a] ← 深拷贝(R[b])（§2.3）
LoadG    a, Bx           R[a] ← 全局槽[Bx]
StoreG   Bx, a           全局槽[Bx] ← 深拷贝(R[a])

# 算术/位/比较（iABC；后缀 I/U/L/D = INT/UINT/UINT64/DOUBLE）
AddI SubI MulI DivI ModI RDivI / AddU..ModU / AddL..ModL / AddD..DivD
BandI BorI BxorI ShlI ShrI；BnotI a, b
EqI LtI LeI GtI GeI / EqU..GeU / EqD..GeD / EqL..GeL / EqS / EqP
Not a, b；NegI NegD a, b；Conv a, b, kind（§2.4）
（!= 无指令：编码器展开 EqX + Not，占一个共享 Neq 临时槽）

# 控制流
Jmp s24；Jpt a, s16；Jpf a, s16        真值 = i32 != 0（S-06）

# 调用（EXT）
Call   a, n, c, fid:32    调用镜像函数 fid；实参 R[a..a+n) 深拷贝进新帧槽 0..n-1（S-17）；
                          c=255 表无接收槽；返回值深拷贝到 R[c]
CallN  a, n, c, target:32 target bit31=1 → L2 syscall 编号；否则 L3 名表索引（§8）
Ret    a / Ret0           返回（深拷贝交接）/ 返回 VOID

# 数组/字符串
NewArrV a, n, first, ext  数组字面量，元素取 R[first..first+n)，ext=元素类型码
NewArrE a, Bx             空数组
GetI a, b, c / SetI b, c, a            索引读/写（S-10）
Slice a, b, c, ext        切片；ext=0xFFFFFFFF 表省略端（S-15）
Cont a, x, c / Append a, c, v / Cat a, b, c / Len a, b

# 结构体
NewSt a, Bx；GetF a, b, f；PutF b, f, a
GetFI a, b, f, ext；PutFI b, f, a, ext（ext=元素索引槽）

# L1 域操作（宿主回调）
WaitI Bx / WaitV a                       延时毫秒（取消感知点）
KeyI a, Bx / KeyV a, b                   点击按键 a（GamePadKey 码）持续毫秒
KeySt a, b                               b=1 按住 / b=0 松开
StickSet a, x, y                         设摇杆 a(0=L,1=R) 坐标（回中 128）
StickP a, x, y, ext:32                   点击摇杆，ext=毫秒
StickPv a, dur, ext:32                   ext 低16 位 x | 高16 位 y（x=ext&0xFF, y=(ext>>16)&0xFF，与编码器 x|(y<<16) 一致），持续 R[dur] 毫秒
Img a, Bx                                R[a] = 图像标签匹配置信度（标签名=常量池[Bx]）
Rand a, b                                R[a] = rand(R[b])（S-12）
```

按键码（`GamePadKey`，u8 直传）：`Y=1 B=2 A=3 X=4 L=5 R=6 ZL=7 ZR=8 MINUS=9 PLUS=10
LCLICK=11 RCLICK=12 HOME=13 CAPTURE=14`；HAT `TOP=16..TOP_LEFT=23`；摇杆 `LS=32 RS=33`
（StickSet/StickP 的 side 参数：0=L, 1=R）。摇杆坐标 ∈ [0,255]，回中 (128,128)；方向→坐标换算
在 C# 编码期完成，VM 不理解角度。

### 3.3 寄存器与槽位模型（每函数栈窗口）

**帧模型**（Dalvik/Lua 式）：帧 = 函数自身的寄存器窗口，帧长 = 函数寄存器高水位（`EcsFunction.NSlots`
≤ 255，指令 A/B/C 域 u8 是唯一上限——项目代码量增长不牵动寄存器堆）。调用时在共享值栈上按被调方
帧长开窗、返回时弹出。帧内布局：

```text
R0                       RET：返回值固定落点（Call 的 C=0，无显式接收时）
R1 .. NParams            ARG 参数窗：仅占用函数声明的参数数（S-17 深拷贝）
NParams+1 起             STAGING：按本函数最大出站调用/数组分块需求保留
STAGING 之后             分配区：独占槽（φ/ForStep/循环携带值/编码器临时）→ 池区
frameLen ≤ 255           帧长 = NSlots = 发射结束后池区高水位
```

- **生命周期分配**（`AssignSlots`，参考线性扫描寄存器分配）：非常量值统一池化——定义发射
  （或首次读取触碰，先到先得）时取槽，**末次静态读取结算后归还**（`ReleaseDying` 全函数读取
  计数归零）；跨迭代安全：循环内的读取点不会在归还后再执行。常量按使用块块首物化（每块独立
  槽 + 块内计数归还）。
- **独占槽**（不入池）：φ 结果与 ForStep 目标（跨迭代被边副本/分支原地写）、**循环携带值**
  （`CollectLoopCarriedValues`：定义块不在其读取所在自然循环体内——线性扫描对回边活性的盲区，
  素数筛语料实证）、编码器临时（Neq/scratch/模板守卫组）。φ 臂合并（下 P1b）后臂须退出池化
  结算，否则会把 φ 独占槽错放回空闲池。
- **参数数没有独立的 7 项上限**：声明上限为 `MaxRegisters - ArgBase = 254`，实际编译还须让
  参数、出站窗口、存活值和临时值一起满足 255 槽帧预算；预算不足报寄存器溢出。无出站调用或
  数组构建的函数不保留 STAGING。零参数调用的 A 取 R0，不读取实参。
- **STAGING 与参数/分配区物理隔离**：出站实参 Move 不会覆写存活值，嵌套调用的结果先放在
  分配区再搬入出站窗口。链接期内联将壳体的整个窗口搬到调用者未使用的区间，先从调用点 A
  复制参数，再统一重映射壳体寄存器；各调用点复用这段窗口。C VM 原生实参栈快照按 B 的实际
  参数数保留，不使用固定 8 项数组，也不增加运行期堆分配。指令/容器 ABI 不变；编译缓存 M2
  使旧布局的产物和旧参数溢出错误缓存失效。
- **编码期槽位回收**：非参数 `StoreLocal` 不发射——局部读值全走 SSA（构建期 mem2reg）；参数
  store 保留（Call ABI 播种 + TRE 回边是真实读取者）。
- **常量块首惰性物化 + 循环不变量外提**：常量在每个使用块的块首物化（`LoadI/LoadBool/LoadK`），
  末次读取归还；零使用不物化。`HoistLoopInvariantConsts`（AssignSlots 步骤 1.5）把「全部使用块
  都被某循环 preheader 支配、且循环体内使用块在脊柱上」的常量改挂 preheader 块首物化一次；
  外提常量按共享组定槽（双向 BFS 守卫防覆写读陈值）。立即数操作数（`KeyI/WaitI/StickP` 常量
  时长）不占槽。
- **链接期死存储清扫**（`DeadStoreSweep`）：反向活跃性删除死 `Move/SetVar/LoadI/LoadBool/LoadK/
  LoadG`（防线 3）。活跃性 CFG 的终结符集含 `ForStep` 与 `CmpJ`（素数筛回归实证）；无终结符的
  直落块必须补 fall-through 边（corpus arith 实证）。
- **发射优化**：块尾跳转消除（P1a）、phi 臂二地址合并（P1b，合并臂 ScrubPooled 退出池化）、
  CmpJ 比较跳转融合（P2′）、常量模板降级（全常量 ≥8 元素数组 → 守卫式模板全局）。
  数组/字符串字面量自语言层即限常量（语法阶段白名单），可在编译期完整固定。

### 3.4 调用约定

实参连续暂存调用者出站窗口 `R[a..a+n)`（A 随调用点编码，位置见 §3.3），`Call` 深拷贝播种进
被调方参数窗 `R[1..1+n)`（S-17）；接收槽 C 恒为寄存器号（无哨兵值），返回值深拷贝交接
（C=0 即 RET 落点）。实参无独立固定上限：编码上限 254，实际与出站窗口、存活值共享 255 槽帧
预算（§3.3；绑定层声明校验 + 编码器预算校验）。递归天然支持；深度上限 `ECS_MAX_CALL_DEPTH`（64，双端对齐）
→ `ECS_ERR_DEPTH`；尾递归由 SSA 优化器（TRE）变循环回边，VM 无 tail-call 指令。

---

## 4. 链接管线（`EcxPipeline.Link`）

```text
模块产物（内存或 .ecm 缓存，ModuleSystem.md）
  → 合并：类型表/全局表/常量池/原生名表（去重重映射）、函数表拷贝 + EXT-aware 全重写
  → 入口合成：init 调用序列前插 $eval 本体，入口命名 <main>
  → 死函数消除：自入口 BFS 调用图（Call 边）
  → 死存储清扫：DeadStoreSweep（§3.3）
  → 死元素消除：常量池/原生名表按保留引用压缩重映射
  → 特征掩码：NeedIL→IL、采集洞可达→CAPTURE、FFI→FFI、文件族 syscall→FILE
  → 资源计算：MaxSlots / MaxDepth → EcxImage
```

Link 在深拷贝副本上进行，不污染调用方产物（`.ecm` 可安全 roundtrip）。

---

## 5. ECX 二进制格式

小端；所有 count 前置；字符串 = `u16 长度 + UTF-8`（常量串为 `u16 units + UTF-16LE`）。
**加载即全量校验**：魔数/版本/计数与剩余长度/槽位与类型码范围/entry 边界，任何不一致 →
`ECS_ERR_IMAGE`。校验通过后指令流只读共享（可 XIP，无重定位——只有表索引与相对跳转）。

```text
偏移  字段
0     magic "ECX2"（4 字节）
4     format_ver:u16 = 2
6     flags:u16   bit0 D=调试名区存在；bit1 K=KeyAction；bit2 I=NeedIL；其余 0
8     max_slots:u8
9     max_depth:u8
10    feats:u16   特征需求掩码（EcsImageFeatures：IL/CAPTURE/FFI/FILE/VISION）
12    const_count:u32 × 常量条目（tag:u8 + payload；STRING = units:u16 + UTF-16LE）
?     struct_count:u32 × StructDef（name:utf8, nfields:u8, Field{name,kind,base,elem,count:u16}）
?     global_count:u32 × Global（module:u8 + type:u8 定长；名字在调试区）
?     native_count:u32 × Native（name:utf8）
?     func_count:u32 × FuncDef（nparams:u8, nslots:u8, hasret:u8, code_off:u32, code_words:u32）
?     代码区（u32 指令字序列，4 字节对齐）
?     调试区（D=1 时：函数名表 + 全局名表，debug_count = func_count + global_count；
      stripDebug 产 MCU 发布版时整区不存在，debug_count=0）
末    entry:u32
```

原生名表只承载 L3（采集洞/FFI/std 加速洞 `__ENCODE__/__JQ__/__CTC_FOLD__`）；L2 syscall 编号直传 EXT，不占名表
（纯内建脚本名表为空）。

---

## 6. C 虚拟机（ecs-vm）

### 6.1 文件与构建

```text
src/EasyCon.Vm/native/  ecs_vm.h（唯一头文件）/ ecs_vm.c（加载器+堆+解释器）/ ecs_main.c（CLI harness）
构建：cc -O2 -std=c99 -Wall -Wextra -o ecs-vm ecs_vm.c ecs_main.c -lm（单翻译单元可合并）
嵌入式：两文件加入工程；malloc/free 经宏重定向到池分配器即可
CLI：ecs-vm run image.ecx [--io] [--trace] [--print] [--strict-caps] [-- arg0...]
  --io = 经 ecs_io 读取器加载（模拟 EEPROM/NVS 等位置的字节码来源）；缺省 = RAM 缓冲直载（可 XIP）
  stdout = PRINT 输出；stderr = --trace 域事件 TSV（KEY/KEYST/STICK/STICKC/WAIT/AMIIBO/BEEP，
  与 C# EcxHost.EnableRecording 同格式）；--print 为 PC 验证通道（缺省 = MCU 精确语义）
```

### 6.2 结构与主循环

- 加载期（信任面）：解析表指针直指 image 缓冲（零拷贝可 XIP）+ 结构边界防护（r_* 越界即拒，
  宿主内存安全）+ strict_caps 特征校验（§6.3）+ 结构体布局展开（slot_offset 计算，嵌套递归 +
  环检测）。**相信字节码**（Lua 式最小验证面）：不做 CRC、不做指令流内容校验（validate_stream
  已随信任面收缩删除）——执行期从镜像定宽直取（4B/8B，格式类查表），XIP 原地执行。
- 字节码来源（`ecs_io`，uLua zIO 式随机访问读取器）：RAM 缓冲（`ecs_io_mem` 直载，可 XIP）、
  内部 flash（内存映射 → 直载零拷贝）、EEPROM/NVS（read 回调分块拉入 VM 内部副本，
  `ecs_vm_load_io`，副本由 VM 持有并在 `ecs_vm_free` 释放）——**读取这些位置的字节码解释执行**。
- 堆：固定对象池（单块 = 一对象，空闲链）；帧 = arena bump + 整帧清零，
  弹帧全槽释放后水位回退。
- 主循环（定长直取）：fr/code/end/R/pc/idx/steps 循环局部化，格式类查表 + switch 取指
  （WJ 跳转字延迟读），ret_pc（字节）/ret_idx（指令下标）仅在调用边界与让出点写回；非递归调用
  （压帧/改 PC/弹帧）；步数预算 `ECS_DEFAULT_BUDGET`（100 万步）耗尽 → `ECS_YIELD`（宿主轮询/
  心跳后重入续跑）；取消 → `ECS_CANCELLED`；错误带 `error_func/error_pc` 现场（pc = 指令下标）。
- CallN：`bit31=1` → `host.syscall(id, args, nargs, &ret)`；否则 `host.native(name, args, ...)`；
  回调缺失/非 0 返回 → `ECS_ERR_NOSUCHNATIVE`。

### 6.3 特征元信息与能力降级（S-21）

声音检测是明确的例外：新增 `AUDIO = 0x20` 特征位，`AUDIO` / `AUDIO_WAIT` / `AUDIO_WAIT_SCORE` 使用 L3 名表路由，不能降级成 0/false。桌面执行桥在任何脚本操作之前要求 `IAudioService` 并建立独立运行会话；C VM 即使没有启用 `strict_caps`，缺少 `ECS_FEAT_AUDIO` 仍在加载期返回 `ECS_ERR_FEAT`（14）。没有原生实现的直接解释器调用保持 `ERR_NOSUCHNATIVE`，不进入缺省值表。具体函数语义见 `docs/Functions.md`。

```text
feats = 头部掩码 | (flags.I ? FEAT_IL : 0)     ← 元信息：缺省不拒载
运行期 miss（宿主回调缺失/未实现）：
  L2 syscall：双端锁步降级（C# EcsCapabilityDefaults.GetSyscallDefault ≡ C ecs_cap_syscall_default，对拍锁定）：
    FWRITE→len  FREAD/READFILE/ARG/ENV/APP→""  FOPEN→-1  FEof/FILE_EXISTS/TIME/OCR_CONF→0
    FClose/WriteFile/AppendFile/Alert/Beep/Amiibo→no-op（Void）
  L3 名表：降级是桌面专属（C# GetNativeDefault：OCR/ROI/CTC_FOLD/ENCODE/JQ/NET_LABEL→""、
    NET_LOAD→-1、NET 句柄族→0、NET_ARGMAX/NET_LABELS→-1、NET_OUT→0.0、YOLO/DET 洞→0、访问器→-1/-1.0）。
    C/MCU 侧无名表判断、无 FFI——native 缺失一律 ECS_ERR_NOSUCHNATIVE 响亮失败（MCU 不支持桌面原生）。
  EXTERN FFI：桌面 C# 缺能力 → int 0；C/MCU 无 FFI → NOSUCHNATIVE。
  表外（未知编号/未知名）双端都不降级 → ECS_ERR_NOSUCHNATIVE
  图像标签（Img）→ 目标槽 ← -1
strict_caps 宿主（ecs-vm --strict-caps / C# EcxHost.StrictCaps）恢复响亮：
  加载期 feats & FEAT_IL → ECS_ERR_IL（13，先判）；feats & ~host->feats → ECS_ERR_FEAT（14）；
  运行期 miss → ECS_ERR_NOSUCHNATIVE
错误码全集：OK/YIELD/CANCELLED/IMAGE/OPCODE/SLOT/TYPE/INDEX/DIVZERO/DEPTH/
            NOSUCHNATIVE/HOST/OOM/IL/FEAT/CRC/POOL；错误现场 error_func/error_pc
            （pc = 函数内指令下标，与 C# ErrorPc 同单位）
```

宿主通过 `host->feats` 声明提供的能力（元信息 + strict 档校验依据）；烧录前预检
（McuBytecodeDelivery §4）仍消费该掩码。L1 域操作（wait/key/stick/rand）不降级。

### 6.4 引用计数协议（S-19）

| 操作 | rc 规则 |
|------|---------|
| Move/phi 副本/实参暂存 | src rc+1 |
| SetVar/传参/RET 深拷贝 | 新句柄 rc=1；COW 时共享移交 |
| 槽覆写（move_to/store_fresh） | 原值 rc−1（归零递归释放）；差异 = 新值是否 retain |
| SetI/PutF 元素写 | 旧 rc−1、新 rc+1 |
| 弹帧 | 全槽按覆写规则释放 |
| 驻留常量串 | 缓存持 ≥1，不参与释放 |

---

## 7. 宿主 ABI

### 7.0 内置函数路由总表

脚本内置函数实现在 `Binding/BuiltinFunctions.Manifest` **能力清单单一登记**
（条目 = 符号 + 路由 + `EcsImageFeatures` 特征位 + MCU 参考桩可用标记；
`Routes`/`IsIntrinsic` 由清单派生；链接器特征扫描直接消费清单 FeatureBit）：

| 脚本面 | 路由 | 落点 |
|--------|------|------|
| WAIT RAND LEN APPEND STRING INT `__CAPTURE__` 系 | Intrinsic | SSA 内联 → 域指令/纯指令，无宿主调用 |
| PRINT TIME | StdlibSource | stdlib ECS 源码（PRINT 体调 FWRITE syscall；TIME 体引用 `__TIME__` RuntimeValue → TIME syscall） |
| ALERT ARG ENV APP BEEP AMIIBO OCR_CONF + 文件族 FOPEN..FILE_EXISTS | Syscall | CallN 编号（§7.3）→ `EcxHost.ReferenceSyscall` / C harness 桩 |
| `__ENCODE__` `__JQ__` `__CTC_FOLD__` | NativeName | CallN 名表 → 宿主 `Native`（EcxVm BuiltinMap；std JQ/ENCODE/CTC_FOLD 的加速洞） |
| `__YOLO_DECODE__` | NativeName | CallN 名表 → 宿主 `Native`（std YOLO_DETECT 的加速洞：形状守卫 + 置信度过滤 + letterbox 逆映射；lib-only） |
| JQ ENCODE READFILE WRITEFILE APPENDFILE | StdlibSource | stdlib ECS 源码实现（JQ/ENCODE 体调各自加速洞；READFILE/WRITEFILE/APPENDFILE 组合 FOPEN/FREAD/FWRITE/FCLOSE） |
| NET_LOAD NET_RUNH NET_SCALE NET_ARGMAX NET_ROWS NET_FREE NET_UNLOAD NET_OUT | NativeName | CallN 名表 → 宿主 `Native`（`IInference` 能力；VISION 特征位；纯标量协议——原生边界不携带数组） |
| NET_NORM `__NET_IMAGE_FILE__` | NativeName | CallN 名表 → 宿主 `Native`（同上 VISION 特征位；NORM 逐通道归一化、FILE 洞本地图片走同管线；lib-only 洞不进 root scope） |
| NET_LABELS NET_LABEL | NativeName | CallN 名表 → 宿主 `Native`（标签表；纯字符串处理，不挂 VISION 特征位） |
| YOLO_COUNT YOLO_CLS YOLO_SCORE YOLO_X1 YOLO_Y1 YOLO_X2 YOLO_Y2 | NativeName | CallN 名表 → 宿主 `Native`（读 `__YOLO_DECODE__` 命中缓存；VISION 特征位；越界 -1/-1.0） |
| EXTERN "库" FUNC | L3 FFI | CallN 名表 `"库!导出名"` → EcxVm externMap → NativeLoader |

### 7.1 分层接口（`ecs_host`，全部回调可 NULL）

```c
typedef struct ecs_host {
    void *ud;
    uint32_t feats;                    /* 提供的特征位（ECS_FEAT_*）；strict_caps 时加载期校验 */
    int strict_caps;                   /* S-21：0 = 缺省值降级（缺省）；1 = 恢复响亮 */
    const char *const *args; int32_t nargs;        /* ARG */
    const uint16_t *app_dir;                       /* APP */
    /* L1 域操作 */
    void (*wait_ms)(ud, ms);  void (*key)(ud, key, dur);  void (*key_state)(ud, key, down);
    void (*stick_set)(ud, side, x, y);  void (*stick_click)(ud, side, x, y, dur);
    int32_t (*rand)(ud, max);          /* [0,max)；max<=0 → 0 */
    /* L2 平台 syscall（编号；返回 0 成功 / 非 0 → ECS_ERR_NOSUCHNATIVE） */
    int (*syscall)(ud, int32_t id, ecs_value *args, int32_t nargs, ecs_value *ret);
    /* L3 动态原生（按名；返回 0 成功 / 非 0 → ECS_ERR_NOSUCHNATIVE） */
    int (*native)(ud, const char *name, ecs_value *args, int32_t nargs, ecs_value *ret);
} ecs_host;
```

VM 核对 L2/L3 **纯调度**：取号/取名 → 查宿主回调 → 调用；无任何内建语义
（行断协议、caps 门控、getenv、ARG 均在宿主参考实现中）。

### 7.2 数据访问 API（宿主读写 VM 数据的唯一通道，uvm32 `arg_get*` 同构）

```c
int32_t ecs_vm_arg_str(ecs_vm *vm, ecs_value v, const uint16_t **units);  /* 非串/空 → 0 */
void    ecs_vm_ret_str(ecs_vm *vm, ecs_value *ret, const uint16_t *units, int32_t len);
```

镜像堆句柄对宿主不透明：字符串读写必须经上述 API，越界访问不可能逃逸。

### 7.3 syscall 编号表（发布即 ABI：新增只追加，废弃不回收）

| 编号 | 名称 | 语义（宿主参考实现） |
|------|------|----------------------|
| 1 | FWRITE | S-14 行断协议：句柄 1=stdout（`\` 结尾剥尾挂起换行）返 len；0=no-op 返 len；2=print(newline)；>2=文件族 |
| 2 | FREAD | 句柄 0=stdin 一行；其余文件族 |
| 3-9 | FOPEN FCLOSE FEOF READFILE WRITEFILE APPENDFILE FILE_EXISTS | 文件族 |
| 10 | ALERT | 弹窗（行断协议同 FWRITE） |
| 11 | ARG | 命令行参数（越界返空串） |
| 12 | ENV | 环境变量（缺失返空串） |
| 13 | APP | 应用目录 |
| 14 | TIME | 运行毫秒（MCU 参考桩恒 0） |
| 15 | BEEP | 蜂鸣（MCU 静默） |
| 16 | AMIIBO | v2.3 Amiibo 槽位选择：槽 0–19 宿主判越界静默（S-13）；无接收槽——任何宿主不写寄存器 |
| 17 | OCR_CONF | OCR 置信度查询（桌面默认 0） |

### 7.4 参考宿主

| 宿主 | 文件 | 覆盖 |
|------|------|------|
| 桌面参考 | C# `EcxHost.Syscall`（默认装配）+ `EcxVm`（CapabilitySet 能力装配：BuiltinMap/FFI/采集洞） | 全 L2 语义（行断 + Caps 门控 + 文件转发） |
| MCU 参考 | C `ecs_main.c` 桩（`h_syscall` + `feats = FEAT_FILE`） | 文件族静默/默认值；TIME=0；ALERT/BEEP no-op；AMIIBO 槽 0–19 trace（越界静默）；ENV=getenv；ARG=harness args |
| 桌面全量 | C `EcxInterpreter` ↔ `EcxVm`；`ecs-vm --print` 通道 | corpus / FullChain 双端对拍锁定 |

一致性责任面：仓库内两端由 corpus/FullChain 逐字锁定；第三方宿主对照 §7.3 表 + 参考桩 +
`EcxInterpreter.cs`（C# 语义参考实现）自行验证。

---

## 8. 资源模型（ESP32 量级参考）

| 项 | 预算 |
|----|------|
| ECX 镜像 | flash/XIP（光速过帧 540 B，含调试区；stripDebug 更小） |
| 指令流 | **零 RAM**：v3 定长 4B/8B，镜像 XIP 原地取指（无解码缓存、无解码段） |
| 帧区 | `max_depth × max_slots × 16B`（光速过帧 608 B） |
| 堆 | 句柄表 + 对象，池大小可配（4–64 KB） |
| VM 本体 | ecs_vm.c ~2200 行 C99 单翻译单元，`cc -O2` 一条命令构建 |
| 调用深度 | `ECS_MAX_CALL_DEPTH` 64（与 C# 对齐）；帧 = 每函数栈窗口 arena bump，帧长按需（参数窗 + 出站窗 + 分配区，无全局地板——无出站调用的叶子函数可小至数槽），深度预算按活跃路径帧和估算（McuBytecodeDelivery.md §3 C4）；用户递归不耗宿主栈；深递归由 TRE 兜底 |

---

## 9. 测试矩阵（现状）

| 层 | 载体 |
|----|------|
| 语料对拍 | `CorpusCrossValidationTests` + `Bytecode/corpus/`（.ecs/.expected/.events 数据驱动，解释器 → C VM 逐行/逐事件） |
| 全链路 | `FullChainVerificationTests`（光速过帧/BDSP/nqueens 真实例程 + 嵌套模块项目双端对拍） |
| ABI 契约 | `AbiContractTests`（编号稳定、L2 编号化名表为空、特征掩码、C VM 加载期拒跑） |
| 语义协议 | `InterpreterProtocolTests`（YIELD/取消/错误现场）、`InterpreterHeapRefcountTests`（RC 自洽）、`CowSemanticsTests`（COW 双态） |
| 链接优化 | `LinkOptimizationTests`（死函数/死存储/死元素）、`BytecodeSizeAnalysisTests`（体积剖析） |
| 门禁 | 每次语义/结构改动：全量测试双态 + `dotnet format --verify-no-changes` + corpus 双端对拍 |

双端不一致时先判定哪端错：修实现而非改期望（corpus 期望即规格）。RC 槽写协议权威实现见
`EcxInterpreter.cs`（MoveToSlot/StoreFresh/OverwriteItem）与 `ecs_vm.c`（move_to/store_fresh）。

---

## 附录 A：端到端示例

```text
源码  PRINT "hi"
  → stdlib PRINT 函数体：FWRITE $STDOUT, $output（$STDOUT 为 std 顶层全局）
  → SSA：StaticCall PRINT →（跨模块用户函数）Call fid(PRINT)；体内 CallN syscall[1] (FWRITE)
  → ECX <main>：Call <init:std> → ... Call PRINT → PRINT 体内 CallN 0x80000001
执行  桌面：EcxHost.Syscall(1) → 行断协议 → Print 委托 → 控制台
      MCU ：h_syscall(1) → --print 通道开则输出；恒返回 len
```
