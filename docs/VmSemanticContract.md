# VM 双端语义契约（VmSemanticContract）

> **定位**：ECS 字节码双端（C# `EcxInterpreter` ↔ C `ecs_vm.c`）语义与引用计数协议的**强制 checklist**
> 与权威定义（S-01..S-19）。任何指令/语义/RC 改动：改前逐条核对双端锚点，改后跑「锁定测试」列
> 全部 + corpus 对拍 + `ECS_COW=0` 双态重跑。锚点格式 `文件:符号`；C 锚点均在
> `src/EasyCon.Vm/native/ecs_vm.c`（单文件）。不变量由全量测试矩阵事实锁定（EasyCon.Tests 731 /
> LSP 66 / corpus 对拍），本表使其可 grep、可审计。

## 一、核心语义条目（S-01..S-19）

| 编号 | 语义定义 | C# 锚点 | C 锚点 | 锁定测试 |
|---|---|---|---|---|
| S-01 | 深浅拷贝边界：`Move`/phi/实参暂存=浅共享；`SetVar`/传参/返回=数组/结构体一层深拷贝、字符串共享 | `EcxInterpreter.DeepCopy` / `DeepCopyCopyOnWrite` | `deep_copy` / `deep_copy_cow` | CowSemanticsTests、CvmCrossValidationTests |
| S-02 | 除零：所有 Div/Mod → `ECS_ERR_DIVZERO` 停机 | `EcxInterpreter.RequireDivisor` | `exec_data_op` DIVZERO 分支 | BytecodeSimulationTests、corpus |
| S-03 | 移位掩码：32 位 `&31`、64 位 `&63` | `EcxInterpreter` 位运算 case | `exec_data_op` OP_ShlI/ShrI | corpus（nqueens_bitwise） |
| S-04 | D2I 饱和：≥INT_MAX→INT_MAX，≤INT_MIN→INT_MIN，NaN→0 | `EcxInterpreter.Conv`（DoubleToInt） | OP_Conv CV_DOUBLE_TO_INT | corpus |
| S-05 | RDivI 四舍五入整除 `(a+b/2)/b`，b=0→S-02 | `EcxInterpreter` case RDivI | OP_RDivI | corpus |
| S-06 | 真值 = `i32 != 0`（句柄非 0） | case Jpt/Jpf | OP_Jpt/Jpf | corpus |
| S-07/08 | TOSTR 两层格式：顶层 PTR=十进制；嵌套 PTR=`0x%X`、STRUCT=`struct:名`、ARRAY=`[a, b]`、VOID=`void`；DOUBLE=ToString() | `ToPublicValue` / C# ToString 链 | `tostring_top` / `tostring_nested` | corpus（print 逐字对拍） |
| S-09 | Cont：字符串子串（Ordinal）；数组元素 tag 不匹配→false 非错误；非法容器→`ECS_ERR_TYPE` | case Cont | OP_Cont | corpus |
| S-10 | GetI 按元素 tag 归一读（BYTE `&0xFF`、BOOL 归一 0/1）；字符串索引返回单字符 STRING | case GetI | OP_GetI | corpus |
| S-11 | Cat：任一侧 STRING→双侧 TOSTR(顶层) 拼接；两侧数组→元素 tag 一致才拼；否则 `ECS_ERR_TYPE` | case Cat | OP_Cat | corpus |
| S-12 | Rand：max=0→0；max<0→错误；[0,max) 宿主注入 RNG | case Rand | OP_Rand | corpus |
| S-13 | Amiibo：n>9 静默忽略 | case Amiibo | OP_Amiibo | corpus |
| S-14 | FWRITE 行断协议：`\` 结尾剥掉并挂起下一行换行（pending_break）；句柄 0 写=no-op 返 len；2=print(newline)；>2→f_write | 宿主 `IIoAdapter` + `EcxNativeContext` | FWRITE native + `pending_break` | CvmCrossValidationTests（事件 TSV） |
| S-15 | Slice/索引越界不 clamp → `ECS_ERR_INDEX` | case Slice | OP_Slice | corpus |
| S-16 | 结构体字段三种类（Scalar/FixedArray/Nested）；Boxed 访问→`ECS_ERR_TYPE`；PutF 写 BYTE 截断 `&0xFF`；嵌套 GetF 视图写穿透 | `WriteStructSlot` / GetF case | OP_GetF/PutF + `sync_view` | CowSemanticsTests、corpus |
| S-17 | 调用约定：实参深拷贝进新帧槽 0..n-1；返回值深拷贝到接收槽 C≠255；深度上限 512→`ECS_ERR_DEPTH` | case Call/CallN/Ret | OP_Call/CallN/Ret + `push_frame` | InterpreterHeapRefcountTests、CvmCrossValidationTests |
| S-18 | 停机协议：入口返回→OK；取消→CANCELLED；预算→YIELD 可续跑 | `EcxInterpreter.Step` | `ecs_vm_run` 返回码 | InterpreterProtocolTests |
| S-19 | 引用计数协议（见第二节） | 见下 | 见下 | InterpreterHeapRefcountTests（HeapCountsConsistent） |

## 二、RC 协议（S-19 展开，双端逐点锁步）

**槽写原语**（任何帧槽/容器元素/结构体槽写入必须经以下原语，双端同名同义；豁免：新帧实参槽
`nf.Slots[i]` 为全新分配槽区，直写 ≡ StoreFresh）：

| 原语 | 语义 | C# 锚点 | C 锚点 |
|---|---|---|---|
| move_to | 覆写：释放旧值 + retain 新值（借用转移） | `EcxInterpreter.MoveToSlot` | `move_to` |
| store_fresh | 覆写：仅释放旧值（新值为出生引用/标量） | `EcxInterpreter.StoreFresh` | `store_fresh` |
| 容器元素覆写 | 旧元素释放 + 新值 retain | `OverwriteItem` | OP_SetI/PutF/PutFI 内联 + `sync_view` |
| 弹帧 | 被弹帧全槽 release | `PopFrame` | OP_Ret/Ret0 全槽释放 |

**COW（move-on-unique）**：四个深拷贝站点（SetVar 局部 / StoreG 全局 / CALL 实参 / RET 返回）在源句柄
rc==1 时 `retain` 共享移交、跳过容器拷贝，rc>1 维持深拷贝。安全性依据：rc==1 ⟹ 对象仅被出生临时槽
持有（临时槽对程序不可见），移交不产生变量间别名，SetI/PutF 原地写安全不变；可观察值语义逐字一致。
开关 `ECS_COW=0` 关闭（双态测试矩阵锁定开/关一致）。锚点：`DeepCopyCopyOnWrite` ↔ `deep_copy_cow`；
站点与锁定测试见上 S-01/S-17 行与 CowSemanticsTests。

## 三、EXT 指令集（编码/扫描/执行三方契约）

- **权威表**：C# `Bytecode/EcsFormat.cs`（每操作码一行的格式/EXT 语义/结果槽登记，漏登在 BuildTable
  完整性自检抛出）；C 侧 `ecs_vm.h` 的 `ecs_op_has_ext`（与 C# 表逐项对齐）。
- **EXT 集合（8 条）**：`Call`（目标 fid/导入标记）、`CallN`（原生 nid）、`NewArrV`（元素类型码）、
  `Slice`（end 槽，0xFFFFFFFF=省略端）、`GetFI`/`PutFI`（元素索引槽）、`StickP`（时长立即数）、
  `StickPv`（packed xy）。
- **铁律**：EXT 后随字是「数据」，数值可能恰好等于某个操作码——一切线性扫描必须按
  `EcsFormat.ExtWords` 步进跳过，否则把数据误读为指令。
- **消费点**：发射自检 `BytecodeEncoder.EmitExt`；扫描 `InstructionScanner`（回调式，链接器各 pass
  共享）与 `EcxLinker`；执行预取 `EcxInterpreter.Step`；反汇编 `EcxDisassembler`；体积分析
  `BytecodeSizeAnalysisTests`。
- **新增 EXT 指令 checklist**（diff 面应为 2 处表 + 各自语义点）：
  1. `EcsOpcode.cs` 加值 + `EcsFormat.BuildTable` 加一行（漏登 → 自检抛出）；
  2. `ecs_vm.h` 的 `ecs_op_has_ext` 加 case + C 执行 case；
  3. 发射（`EmitExt`）/执行（解释器 case 消费预取 `ext`）语义；
  4. `InstructionFormatTests` 与 VM2.md §4 同步。

## 四、C VM 结构与平台契约

- **形态**：单翻译单元 C99（`ecs_vm.h` API 契约 + `ecs_vm.c` + `ecs_main.c` harness），仅依赖 libc；
  `sh ci/build-vm.sh`（cc/gcc/clang 探测，全无则跳过）。VM 核对 L2/L3 **纯调度**（取号/取名 →
  宿主回调），无内建语义；行断协议、caps 门控、getenv、ARG 均在宿主参考实现
  （C# `EcxHost.Syscall` / C `ecs_main.c` 桩）。
- **五区段**：镜像解析（表指针直指 image 缓冲，零拷贝可 XIP，加载即全量校验 + 特征校验 → 堆
  （句柄表 + rc + 空闲链 + §二原语）→ 解释器主循环（压帧/弹帧非递归，YIELD 预算步进可重入）→
  结构体布局展开（加载期算 slot_offset，嵌套递归 + 环检测）→ 宿主回调分发（L2 编号 syscall /
  L3 名表原生，未注册 → `ECS_ERR_NOSUCHNATIVE`）。
- **L2/L3 分流 ABI**（VM2.md §9.1）：`CallN` EXT 字 `bit31=1` → 低 31 位为 syscall 编号
  （FWRITE=1..OCR_CONF=17 全 L2 封闭集，C# `EcsSyscall` ↔ C `ECS_SYSCALL_*`，**不进原生名表**）；
  `bit31=0` → 原生名表索引（仅 L3：采集洞 `__xxx__` / EXTERN FFI "库!导出名" / ENCODE / JQ）。
- **特征需求掩码**（镜像头保留位 u16 @0x0A；IL 由 flags.I 投影）：加载规则
  `feats & FEAT_IL → ECS_ERR_IL(13)`；`feats & ~host->feats → ECS_ERR_FEAT(14)`——FFI/采集洞
  镜像加载期拒跑（不再等到运行期）；文件族由 MCU 参考桩覆盖（`feats = FEAT_FILE`），行为 =
  静默/默认值（FWRITE 返 len、FREAD/串返空、TIME=0、BEEP/ALERT/AMIIBO no-op，S-13）。
- **harness**：`ecs-vm run image.ecx [--trace] [--print]`——stdout=输出；stderr=事件 TSV
  （KEY/KEYST/STICK/STICKC/WAIT/AMIIBO/BEEP，与 `EcxHost.EnableRecording` 同格式）；`--print` 为
  PC 侧验证通道，缺省即 MCU 精确语义。
- **错误码**：OK/YIELD/CANCELLED/IMAGE/OPCODE/SLOT/TYPE/INDEX/DIVZERO/DEPTH/NOSUCHNATIVE/HOST/
  OOM/IL/FEAT（全集见 `ecs_vm.h`）；错误现场 `error_func/error_pc` ↔ `EcxInterpreter` ErrorFunc/ErrorPc。

## 五、验证体系

| 层 | 载体 | 内容 |
|---|------|------|
| 语料对拍 | `CorpusCrossValidationTests` + `Bytecode/corpus/` | 每用例 `.ecs`+`.expected`+`.events` 数据驱动：编译 → 解释器断言 → 同镜像交 C VM（无 cc 自动跳过）逐行/逐事件对拍 |
| 结构断言 | `CvmCrossValidationTests` | 模块管线镜像、错误码传播（除零=8）、深度上限（9）、NeedIL 拒绝（13） |
| 全链路 | `FullChainVerificationTests` | 真实例程（光速过帧/nqueens）+ 嵌套模块项目，解释器 ↔ C VM 输出与事件全量对拍（TIME 墙钟行白名单） |
| 堆语义 | `InterpreterHeapRefcountTests`、`CowSemanticsTests` | HeapCountsConsistent、深拷贝隔离、COW 双态一致 |
| 门禁 | 每次语义/结构改动 | 731 双态 + LSP 66 + format + nqueens 双端逐字一致 |

双端不一致时先判定哪端错：修实现而非改期望（corpus 期望即规格）。

## 维护规则

1. 改动任何 S-条目语义 → 先改本表 → 双端实现同日落地（禁止单端先行合入）→ 跑「锁定测试」列
   全部 + corpus 对拍 + `ECS_COW=0` 重跑；
2. 新增 RC 站点 → 第二节表加行，双端锚点同步；
3. 新增指令 → 第三节 checklist；
4. 锚点失效（重构改名/移动）→ 当次 PR 内同步更新（grep 校验锚点存在）。
