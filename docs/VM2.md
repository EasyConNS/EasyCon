# VM2 — ECS 字节码虚拟机规范

> **定位**：ECS 脚本的编译目标与双端执行规范——C# `EcxInterpreter`（桌面）与 C `ecs_vm`（单片机）
> 消费同一 ECX 镜像。本文是**现状规范**：指令集、二进制格式、宿主 ABI、分层模型。
> 权威源：`EcsOpcode.cs` / `EcsFormat.cs`（C# 侧）与 `ecs_vm.h`（C 侧）逐项对齐；
> 双端语义契约与测试矩阵见 `docs/VmSemanticContract.md`；编译链路见 `docs/Pipeline.md`；
> 模块系统见 `docs/ModuleSystem.md`。冲突时以代码为准并修本文。

## 1. 分层架构

宿主交互按四级分层，每层有独立的机制与宿主姿态：

| 层 | 内容 | 机制 | MCU 参考宿主姿态 |
|----|------|------|------------------|
| **L0 机器** | 算术/比较/控制流/容器/结构体/调用 | VM 核指令 | 天然支持 |
| **L1 基础能力** | `WAIT`、按键、摇杆、`RAND` | 专用指令（`WaitI/KeyI/Stick*/Rand`），直连宿主回调 | 全支持 |
| **L2 平台 syscall** | `FWRITE/FREAD/文件族/ALERT/ARG/ENV/APP/TIME/BEEP/AMIIBO/OCR_CONF` | `CallN` EXT 旗标 + **编号**（§7.3），语义在宿主参考实现 | 参考桩：静默/默认值（TIME=0、FWRITE 返 len、串返空、AMIIBO 槽 0–19 越界静默） |
| **L3 动态原生** | 采集洞 `__CAPTURE__` 系、EXTERN FFI、`ENCODE/JQ`、推理实验 `NET_*` | `CallN` 名表**按名**分发 | S-21 缺省值表降级（OCR/ROI/ENCODE/JQ→""、NET_LOAD→-1、NET_RUN→0、NET_OUT→0.0、FFI→0）；strict_caps 恢复响亮 |

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
| `ci/build-vm.sh` | CI 构建入口（无 C 工具链则跳过） |

---

## 2. 值模型

### 2.1 值表示：16 字节 tagged union

C# `TaggedValue` 与 C `ecs_value` 同构：

```c
typedef struct ecs_value {
    uint8_t tag;  uint8_t _pad[7];
    union { int32_t i32; int64_t i64; double f64; };
} ecs_value;
```

| Tag | 值 | 载荷 | 备注 |
|-----|----|------|------|
| VOID=0 | — | — | 句柄 0 同时表示 null |
| BOOL=1 / BYTE=2 / INT=3 / UINT=4 | i32 | BOOL 规范化 0/1；UINT 无符号解释 |
| UINT64=5 | i64 | |
| DOUBLE=6 | f64 | |
| STRING=7 | i64 低 32 位 = 句柄 | UTF-16LE code unit 存储；LEN 按 code unit |
| ARRAY=9 | 句柄 | 元素为定长 `ecs_value` 数组 |
| PTR=10 | i64 | FFI 指针 |
| STRUCT=12 | 句柄 | 字段按布局展开的槽区 |

**标签可观察性**：运行时只有 STRING/ARRAY/STRUCT 的标签被解引用检查；BOOL/BYTE/INT/UINT 的标签差异不可观察（读取方式由编译期类型特化的指令决定）。编码器可用 `LoadI`（INT 标签）装 BYTE/UINT 常量，仅 BOOL 用 `LoadBool` 保持 0/1 规范化。

### 2.2 堆：句柄表 + 引用计数

- 句柄从 1 起，0 = null；句柄表 + 空闲链复用，恒不移除/移动元素。
- **字符串驻留（intern）**：常量池字符串**首次执行时驻留**（缓存持常驻引用，句柄永不归零）；
  仅 C# `EcxInterpreter.InternedString` 实现，**C 端不驻留**（每次 `LoadK` 新建串，MCU 宿主
  如需可自行缓存）；运行期字符串（CAT/切片/转换）新建句柄。
  字符串不可变，复制一律共享。
- 数组：`{elem_type, items[]}`；结构体：`{layout*, slots[]}`，槽区按 StructDef 布局展开。
- **引用计数不回收环**：ECS 无闭包，实践值为树形；停机随 VM 销毁全量释放。
- 自洽不变量：`alloc − release = live = 非空槽位数`（`HeapCountsConsistent` 锁定）。

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

### 3.1 编码格式（小端；线格式 = v3 定长指令（ECX1 平铺容器），见下）

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
> 本指令字节数 + delta）。操作数位宽由格式锁定：槽位 u8 ≤254（**C=255 = 无接收槽哨兵**，
> 内存 `NoSlot=-1`）；AsBx=s16；ABx=Bx16（≤65535，常量池 16 位索引是 pinned 句柄编码前提）；
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

### 3.3 槽位模型

```text
[0 .. nParams)                        参数符号槽（恒等映射：Call 实参窗 + TRE 回边 store）
[nParams .. nLocals)                  存活局部符号槽（编码期压缩重编号；优化删除的局部不占槽）
[nLocals ..)                          专用槽：phi 结果（死 φ 免分配）与跨块值
(..)                                  保留区：Neq 中间槽、并行副本 scratch、实参 staging、返回值 receive
(.. nSlots)                           块内槽池：块内值与常量，定义/物化时取用，末次读取归还
nSlots ≤ 宿主容量档案（参考宿主 255；格式字段宽 u16，无格式限制）
```

- **编码期槽位回收**：非参数 `StoreLocal` 不发射——局部读值全走 SSA（构建期 mem2reg），
  帧槽无读取者，对无人读取槽的写入不可观察；参数 store 保留（Call ABI 播种 + TRE 回边是
  真实读取者）。仍被引用的局部符号槽压缩重编号到参数窗之后（`AssignSlots` 步骤 0）。
- **池化判据**：phi 恒专用（**全函数零读取的死 φ 不分配槽、前驱边不产生副本**）；其余值全部读取
  都在定义块内 → 池化；跨块 → 专用槽。phi 臂读取按臂↔前驱对齐计入前驱块。
- **常量块首惰性物化 + 循环不变量外提**：常量在每个使用块的块首物化（`LoadI/LoadBool/LoadK`），
  末次读取归还；零使用不物化。`HoistLoopInvariantConsts`（编码期 AssignSlots 步骤 1.5）把「全部使用块
  都被某循环 preheader 支配、且循环体内使用块在脊柱上（header→回边源每轮必经）」的常量改挂 preheader
  块首物化一次（FOR 尾融合后每轮省 2-3 条块首 LoadI）；外提常量按共享组定槽——两常量共享一槽的守卫是
  双向 BFS（从任一物化点出发不经另一物化块可达对方使用块即拒绝，防覆写读陈值），稀有分支内的常量不外提
  （无每轮收益，徒占槽）。立即数操作数（`KeyI/WaitI/StickP` 常量时长）不占槽。
- **链接期死存储清扫**（`DeadStoreSweep`）：反向活跃性删除落槽/边副本残留的死
  `Move/SetVar/LoadI/LoadBool/LoadK/LoadG`（防线 3），pc 重映射保持跳转合法。活跃性 CFG 的终结符集含
  `ForStep`（exit 目标与 fall-through 两条出边都入图）——漏边会让循环回边副本被误判死指令（嵌套 FOR
  死循环实证）；use/def 按 `ForStep 读 A/B、写 C` 登记；**`CmpJ` 必须同入终结符集**（读 A/B、
  C=kind 码非槽位、无 def）——漏入会让比较跳转的两条边不进活跃性图，循环携带值被误删（素数筛
  回归实证）。无终结符的块（P1a 尾跳转消除后的直落块）
  必须补对下一块的 fall-through 边——漏边会让直落块 liveOut 为空、块内定义被误删（corpus arith
  入口初始化实证）。
- **取指通路局部化 + 比较族直写**（P0，`EcxInterpreter.Step`）：frame/指令流 span/R/pc 为循环局部
  （换帧显式重载、YIELD/宿主后取消写回 pc）；比较族 21 case 直写不走委托、浮点比较与 C VM 的
  C 语义对齐（NaN → false）。纯 C# 侧，字节码形态不变。
- **发射期跳转瘦身**（P1a，`NextIsBlock`）：块尾跳转目标为下一发射块时直落；Jpt+Jpf 双跳转在
  单侧可直落时只发另一侧。mid-shape 跳转（skip 标签前）不可消除——直落会误入另一臂。
- **phi 臂二地址合并**（P1b，`TryCoalescePhiArm`）：唯一读取为某 phi 臂的跨块指令与 φ 共槽，
  边副本 dst==src 自消；守卫 = X 后无读 φ、终结符条件与其他臂不引用 φ（覆写后副本会拿到新值）。

### 3.4 调用约定

实参连续暂存调用者 `R[base..base+n)`，`Call` 深拷贝进被调帧槽 `0..n-1`；返回值经接收槽 C 深拷贝
交接（NoSlot = -1）。递归天然支持；深度上限 `ECS_MAX_CALL_DEPTH`（512）→ `ECS_ERR_DEPTH`；尾递归
由 SSA 优化器（TRE）变循环回边，VM 无 tail-call 指令。

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

原生名表只承载 L3（采集洞/FFI/ENCODE/JQ）；L2 syscall 编号直传 EXT，不占名表
（纯内建脚本名表为空）。

---

## 6. C 虚拟机（ecs-vm）

### 6.1 文件与构建

```text
src/EasyCon.Vm/native/  ecs_vm.h（唯一头文件）/ ecs_vm.c（加载器+堆+解释器）/ ecs_main.c（CLI harness）
构建：cc -O2 -std=c99 -Wall -Wextra -o ecs-vm ecs_vm.c ecs_main.c -lm（单翻译单元可合并）
嵌入式：两文件加入工程；malloc/free 经宏重定向到池分配器即可
CLI：ecs-vm run image.ecx [--trace] [--print] [-- arg0...]
  stdout = PRINT 输出；stderr = --trace 域事件 TSV（KEY/KEYST/STICK/STICKC/WAIT/AMIIBO/BEEP，
  与 C# EcxHost.EnableRecording 同格式）；--print 为 PC 验证通道（缺省 = MCU 精确语义）
```

### 6.2 结构与主循环

- 加载期：解析表指针直指 image 缓冲（零拷贝可 XIP）+ 全量校验 + 特征校验（§8.3）+ 结构体布局
  展开（slot_offset 计算，嵌套递归 + 环检测）。v3 定长编码：validate 即唯一安全层，
  **镜像零变换零解码 RAM**——执行期从镜像定宽直取（4B/8B，格式类查表），XIP 原地执行。
- 堆：固定对象池（单块 = 一对象，空闲链，PROJECT_OUTLINE.md §4）；帧 = arena bump + 整帧清零，
  弹帧全槽释放后水位回退（PROJECT_OUTLINE.md §3）。
- 主循环（v3 定长直取）：fr/code/end/R/pc/idx/steps 循环局部化，格式类查表 + switch 取指
  （WJ 跳转字延迟读），ret_pc（字节）/ret_idx（指令下标）仅在调用边界与让出点写回；非递归调用
  （压帧/改 PC/弹帧）；步数预算 `ECS_DEFAULT_BUDGET`（100 万步）耗尽 → `ECS_YIELD`（宿主轮询/
  心跳后重入续跑）；取消 → `ECS_CANCELLED`；错误带 `error_func/error_pc` 现场（pc = 指令下标）。
- CallN：`bit31=1` → `host.syscall(id, args, nargs, &ret)`；否则 `host.native(name, args, ...)`；
  回调缺失/非 0 返回 → `ECS_ERR_NOSUCHNATIVE`。

### 6.3 特征元信息与能力降级（S-21）

```text
feats = 头部掩码 | (flags.I ? FEAT_IL : 0)     ← 元信息：缺省不拒载
运行期 miss（宿主回调缺失/未实现）：
  L2/L3 按缺省值表降级（C# EcsCapabilityDefaults ≡ C ecs_cap_*_default，四路对拍锁定）：
    FWRITE→len  FREAD/READFILE/ARG/ENV/APP→""  FOPEN→-1  FEof/FILE_EXISTS/TIME/OCR_CONF→0
    FClose/WriteFile/AppendFile/Alert/Beep/Amiibo→no-op（Void）
    __CAPTURE__/__ROI__/__OCR__/ENCODE/JQ→""  __OCR_INIT__→0  NET_LOAD→-1  NET_RUN→0  NET_OUT→0.0
    EXTERN FFI（名含 !）→0；表外（未知编号/未知名）不降级 → ECS_ERR_NOSUCHNATIVE
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
| ENCODE JQ | NativeName | CallN 名表 → 宿主 `Native`（EcxVm BuiltinMap） |
| NET_LOAD NET_RUN NET_OUT | NativeName | CallN 名表 → 宿主 `Native`（`IInference` 能力；VISION 特征位；纯标量协议——原生边界不携带数组） |
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
`VmSemanticContract` 自行验证。

---

## 8. 资源模型（ESP32 量级参考）

| 项 | 预算 |
|----|------|
| ECX 镜像 | flash/XIP（光速过帧 540 B，含调试区；stripDebug 更小） |
| 指令流 | **零 RAM**：v3 定长 4B/8B，镜像 XIP 原地取指（无解码缓存、无解码段） |
| 帧区 | `max_depth × max_slots × 16B`（光速过帧 608 B） |
| 堆 | 句柄表 + 对象，池大小可配（4–64 KB） |
| VM 本体 | ecs_vm.c ~2200 行 C99 单翻译单元，`cc -O2` 一条命令构建 |
| 调用深度 | `ECS_MAX_CALL_DEPTH` 512；帧 = arena bump（PROJECT_OUTLINE.md §3），用户递归不耗宿主栈 |

---

## 9. 测试矩阵（现状）

| 层 | 载体 |
|----|------|
| 语料对拍 | `CorpusCrossValidationTests` + `Bytecode/corpus/`（.ecs/.expected/.events 数据驱动，解释器 → C VM 逐行/逐事件） |
| 全链路 | `FullChainVerificationTests`（光速过帧/BDSP/nqueens 真实例程 + 嵌套模块项目双端对拍） |
| ABI 契约 | `AbiContractTests`（编号稳定、L2 编号化名表为空、特征掩码、C VM 加载期拒跑） |
| 语义协议 | `InterpreterProtocolTests`（YIELD/取消/错误现场）、`InterpreterHeapRefcountTests`（RC 自洽）、`CowSemanticsTests`（COW 双态） |
| 链接优化 | `LinkOptimizationTests`（死函数/死存储/死元素）、`BytecodeSizeAnalysisTests`（体积剖析） |
| 门禁 | 每次语义/结构改动：全量 735 双态 + LSP 66 + `dotnet format --verify-no-changes` + corpus 双端对拍 |

双端不一致时先判定哪端错：修实现而非改期望（corpus 期望即规格）。语义条目 S-01..S-19 与
RC 协议的权威清单见 `docs/VmSemanticContract.md`。

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
