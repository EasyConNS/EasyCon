# ECM / ECX 二进制格式规范

> **版本 2.0（精炼重写）**。ECM（模块缓存产物）与 ECX（可执行镜像）的权威位级规格，
> 与实现逐字段同步：C# 写侧 `src/EasyCon.Script/Bytecode/EcxWriter.cs` / `EcmFormat.cs`，
> C 读侧 `src/EasyCon.Vm/native/ecs_vm.c`（`ecs_vm_load`）。改格式必须三处同步并升版本。
> 指令语义见 `docs/VM2.md` §4；双端语义契约见 `docs/VmSemanticContract.md`。

## 1. 通用编码规则

- 小端。元数据字符串 = `u16 字节长 + UTF-8`；脚本常量字符串 = `u16 code unit 数 + UTF-16LE`（长度与 C# `string.Length` 同义）。
- ECX 整体加载地址须 4 字节对齐；各 u32 字段相对镜像起点天然 4 字节对齐（头部定长 0x24，表条目均为 4 的倍数内字段）。
- ECX2 计数上限（写出器与加载器双向强制）：槽位/参数数量 ≤255，Bx ≤65535，
  单函数指令 ≤2²⁴，常量 ≤65535。桌面内存镜像与 ECM v5 的宽槽旁表不受槽位 255 限制。

## 2. ECX —— 链接后可执行镜像（format_ver = 2）

```
偏移   大小  内容
0x00   4    magic = "ECX2"
0x04   2    format_ver = 2
0x06   2    flags           §2.1
0x08   1    max_slots       全程序最大帧槽（= max(funcs.nslots)，宿主预分配帧区依据）
0x09   1    max_depth       静态调用图最长链（含 <main>；宿主预分配深度）
0x0A   2    feats           特征需求掩码（EcsImageFeatures：IL/CAPTURE/FFI/FILE/VISION；IL 亦由 flags.I 投影；VISION=0x10，C 参考桩不提供 → 加载期拒跑）
0x0C   4×6  const_count / struct_count / global_count / native_count / func_count / debug_count
0x24   ...  常量池 → 类型表 → 全局表 → 原生名表 → 函数表 → 代码区 → 调试区（可选）→ entry:u32（收尾 4 字节）
```

代码区总字节数 = Σ code_words（无独立长度字段）；`EcxWriter.Write(image, stripDebug: true)` 剥离调试区（flags.D=0、debug_count=0），供 MCU 发布版减容。

uvm32 式纯净镜像：核心全局表项定长 2 字节，全局名与函数名同住调试区，运行期按索引访问；
MCU 发布镜像（stripDebug）零名字重量。

### 2.1 flags (u16)

| 位 | 名 | 含义 |
|----|----|------|
| 0 | D | 调试区存在（函数名表 + 全局名表） |
| 1 | K | KeyAction——程序含按键指令，宿主须创建手柄 |
| 2 | I | NeedIL——程序引用图像标签；无 IL 能力的平台加载即拒（`ECS_ERR_IL`） |


### 2.2 各表条目

```
常量池: tag:u8 + 载荷（INT/UINT i32；UINT64/PTR i64；DOUBLE f64；STRING units:u16 + UTF-16LE）
        镜像级去重；加载后不可变（实现可置只读段，rc 标记 pinned）。

类型表: StructDef := name:utf8 nfields:u8 { Field }
        Field := name:utf8 kind:u8 base:u8 elem:u8 ext:u16
          kind: 0=Scalar(1 槽) 1=FixedArray(ext=元素数, elem 槽)
                2=NestedStruct(ext=嵌套 sid, 槽数=嵌套 nslots) 3=Boxed(1 槽, 动态数组声明, 访问→ECS_ERR_TYPE)
        加载器按声明序计算 slot_offset 与总 nslots（嵌套递归展开，环 → ECS_ERR_IMAGE）。

全局表: module_idx:u8 type:u8                定长 2 字节，运行期按索引访问；名字在调试区
原生名表: name:utf8                            仅 L3：采集洞 "__xxx__" / EXTERN FFI "库!导出名" / ENCODE / JQ
                                              （L2 文件族/平台 syscall 编号直传 CallN ext，bit31=1，不进本表）

函数表: 11 字节定长 ×N —— nparams:u8 nslots:u8 attrs:u8(bit0=hasret) code_off:u32 code_words:u32
        code_off 单位=指令字、相对代码区起始；函数索引 = 表序 = Call ext。

调试区（flags.D=1；debug_count = func_count + global_count）:
        函数名表（按 fid 序）→ 全局名表（按全局槽序）。名字纯诊断用，加载器可读后即弃。
```

### 2.3 加载校验清单（全部通过才可运行，任一失败 → `ECS_ERR_IMAGE`）

magic/版本 → 特征校验（feats & FEAT_IL → ECS_ERR_IL；feats & ~host->feats → ECS_ERR_FEAT）→ 各节计数与剩余长度一致（末尾恰剩 entry 4 字节）→ entry < func_count →
每个 FuncDef：nslots ≤ max_slots、code_off+code_words ≤ 代码区总字数 → 指令流静态抽查
（操作码合法、槽位 <255、Jmp 目标在本函数内、Call ext < func_count、CallN ext < native_count、
LoadK Bx < const_count）→ 类型表嵌套展开无环。校验宁可严格：格式是编译器产物而非手写体。

## 3. 运行时值标签与类型码

| tag | 名 | 载荷 | | 码 | 类型 | 码 | 类型 |
|-----|----|------|-|----|------|----|------|
| 0/1/2/3/4 | VOID/BOOL/BYTE/INT/UINT | i32 | | 0–5 | void/bool/byte/int/uint/uint64 |
| 5/6 | UINT64/DOUBLE | i64/f64 | | 6/7 | double/string |
| 7/9/12 | STRING/ARRAY/STRUCT | i64 低 32 位=堆句柄 | | 8/9/10/11 | array/ptr/struct/any |

## 4. ECM —— 模块产物（"ECM2" format_ver = 5）

开发链路上的编译缓存（ModuleSystem.md §6），与 ECX 同族但带名字与接口区（不必最省）。
当前布局与 `EcmFormat.Write/Read` 逐字段一致：

```
"ECM2" ver:u16(=5) flags:u16(HasEval=bit0；与下方标志字节 bit0 同值冗余，读侧以标志字节为准)
链接标志字节:u8   bit0=HasEval bit1=HasInit bit2=KeyAction bit3=NeedIL   ← v3 新增：跨缓存存活
InitFid:u32       <init:module> 函数的模块局部 fid（无 init 时 0）
module_name:utf8
接口区长度:u32 + ModuleInterfaceFormat 负载（无接口 = 长度 0）
                  内容：接口哈希、依赖表(name, ifaceHash)、导出函数表(签名/默认值载荷/extern)、
                  类型表、IL 名表——绑定消费者所需的全部可观察信息（ModuleSystem.md §3）
struct_count { StructDef }        # 本模块声明集（与 ECX Field 同构）
global_count { name:utf8 type:u8 }# 模块私有全局
native_count { name:utf8 }
func_count { name:utf8 nparams:i32 nslots:i32 hasret:u8 code_off:u32 code_words:u32 }
code_bytes:u32 { u32... }
const_count { EcsConst }
import_count { name:utf8 nparams:i32 hasret:u8 }    # Call ext = 0x80000000 | importIdx
export_count { name:utf8 local_fid:u32 nparams:i32 hasret:u8 }
il_count { name:utf8 }
line_table_func_count:u32
  { value_count:u32 { value:i32 } }                  # v4：每函数 [pc,line,...] 桌面源码行映射
wide_operand_func_count:u32
  { entry_count:u32 { pc:i32 mask:u8 a:i32 b:i32 c:i32 } }
                                                     # v5：每函数桌面宽槽/宽调用参数旁表
```

### 4.1 引用规则（编码期闭合矩阵；链接期唯一重写点）

| 指令 | 编码期写 | 链接期重写为 |
|------|----------|--------------|
| `Call` | 本模块=局部 fid；跨模块=`0x80000000\|importIdx` | 全局 fid |
| `CallN` | 模块局部原生 idx | 镜像原生 idx（合并去重） |
| `LoadK` | 模块常量池 idx | 镜像常量池 idx（去重合并） |
| `LoadG/StoreG` | 模块局部全局槽 | + 模块全局基址 |
| `Jmp/Jpt/Jpf` | 函数内相对偏移 | 不变（PC 相对，平移不变） |

### 4.2 导入解析

导出索引键 = `(name, nparams)`，按链接序（std → vision → 拓扑序依赖 → main）首匹配
（镜像 Binder 遮蔽语义）；同名同签名跨模块导出已在编译期经 `FunctionSymbol` 值相等合并，
不产生二义，仅报 `MD_AMBIGUOUS_EXPORT` 警告。未解析 → `LC_MISSING_FUNC`。

## 5. 版本与演进

- `format_ver` 单调递增，加载器拒绝更高版本；语义不兼容变更必须升版本（旧缓存经严格版本校验自然失效）。
- ECX v1 冻结项：32 位定长指令、8 位槽、16 位常量索引、UTF-16 常量串。ECM 当前版本 v5；
  v4 增加桌面行号表，v5 增加桌面宽槽旁表和 32 位函数参数/槽位计数。ECM 是编译缓存格式，
  这些字段不会进入 MCU ECX2。

### 5.1 格式 v2 窗口设计备注（统一链路遗留项，实施须与 C VM 协商升版本）

| # | 项 | 方向 | 前置 |
|---|-----|------|------|
| 1 | 调用约定寄存器化 | 标量实参 `CallR` 直传，句柄维持深拷贝；FuncDef 增参数类别位图 | 双端 ABI 同步；函数表定长扩容 → 升版本 |
| 2 | 镜像 CRC | 尾部 CRC-32，`ecs_vm_load` 末段校验 | 新错误码 `ECS_ERR_CRC` 双端同步；XIP 流式计算 |
| 3 | Link 三遍合一 | 单遍哈希表完成导入解析 + 索引重写 + `<main>` 合成 | 无格式变更，可独立做；首匹配遮蔽语义逐字保持 |
| 4 | 模块并行编译 | 拓扑分层内并行（temp+rename 原子写已备） | per-key 原子填充防重复编译 |

## 6. 与实现的对应

| 规格条目 | 实现 |
|----------|------|
| ECX 写（含 stripDebug） | `EcxWriter.Write` |
| ECM 写/读 | `EcmFormat.Write/Read` |
| 接口区负载 | `ModuleInterfaceFormat`（Bytecode/ModuleInterface.cs） |
| 链接（§4.1 重写矩阵 + `<main>`/init 合成 + 死函数消除） | `EcxPipeline.Link`（Bytecode/EcxLinker.cs） |
| 加载校验（§2.3） | `ecs_vm_load`（C） |
| 反汇编（调试） | `EcxDisassembler` |
