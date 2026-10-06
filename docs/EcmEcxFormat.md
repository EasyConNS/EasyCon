# 产物容器格式规范：ECX1 平铺镜像 + ECM1 平铺模块缓存

> **版本 2.0**（v2.3 零期容器平铺化，2026-10-05）。`.ecx` 镜像 = **ECX1** 平铺容器，
> `.ecm` 模块缓存 = **ECM1** 平铺容器。定案准则 = **不考虑兼容、清晰紧凑尽可能小**
> （决策记录见仓库根 PROJECT_OUTLINE.md §3.2）：无逐段框架、无段级协商——格式演进 = 版本断代 + 全量重编。
> 实现即规格：C# 读写 `src/EasyCon.Script/Bytecode/EcsContainerFormat.cs`，
> C 读侧 `src/EasyCon.Vm/native/ecs_vm.c`（`ecs_vm_load`，仅 ECX1——.ecm 不上单片机）。
> 指令语义见 `docs/VM2.md` §4；双端语义契约见 `docs/VmSemanticContract.md`。
> 旧 ECSC TLV（v1.0）/ ECX v2 / ECM v4 均已断代：加载器以 magic 拒绝并提示重新编译
> （ECSC kind=module 缓存按未命中自然失效重编），历史规格见 git 历史。

## 0. 共同纪律（ECX1 与 ECM1）

- 小端。元数据字符串 = `u16 字节长 + UTF-8`；常量字符串 = `u16 code unit 数 + UTF-16LE`。
- **36B 定长头 + 自计数表依序排列**，无对齐填充、无段框架；严格等长校验（读毕 `pos == len`，
  违例 = `ECS_ERR_IMAGE`——C 侧；C# 侧抛 `BytecodeException`，缓存路径按未命中重编）。
- **全量 CRC-32**（IEEE 0xEDB88320）：覆盖整个镜像（CRC 字段自身清零后参与计算），
  存于头内 `u32@32`；失败 = `ECS_ERR_CRC`。镜像字节被外部工具改写后须重封
  （C# `EcsContainer.ResealCrc32`）。
- **容量不是格式字段**：产物只携带关于自身静态极值的事实（u16×3@24，
  仓库根 PROJECT_OUTLINE.md §4；只记录不判定）。「能否装进设备」由宿主容量档案（C 侧 `ECS_MAX_SLOTS`
  等固件常量，加载期逐函数校验 `nslots ≤` 上限，超限 = `ECS_ERR_SLOT`）与烧录前预检判定。
- 指令流 = v3 定长投影（`InstructionCodec.Project`）：4B/8B 指令字；跳转 offset 为相对
  **字节**偏移（基准 = 指令起始 + 4，加载期位图校验，违例 = `ECS_ERR_OPCODE`）；
  内存表示（`EcsInstruction.Jump`）为相对指令下标，换算单处收口在 `InstructionCodec`。
- **C# 解释器不消费投影代码**（桌面从源码/缓存编译直接得到解码形态）；C VM 取指期直接定长取字。

## 1. ECX1（.ecx 平铺镜像）

```
┌ Header (36 B) ─────────────────────────────────────────────┐
│ 0x00 u32 magic = "ECX1" (0x31584345)                       │
│ 0x04 u16 format     = 1                                    │
│ 0x06 u16 abi_rev    = EcsSyscall.AbiRevision = 3（不符拒载）│
│ 0x08 u16 feats      特征需求掩码（IL 缺位 → ECS_ERR_IL，    │
│                     其余缺位 → ECS_ERR_FEAT）               │
│ 0x0A u16 entry      入口 fid                               │
│ 0x0C u8  flags      bit0 D=调试块在 / bit1 K=KeyAction /   │
│                     bit2 I=NeedIL                          │
│ 0x0D u8  name_len   主模块名长度；0x24 起 UTF-8            │
│ 0x0E u16 func_count                                        │
│ 0x10 u32 code_size  .text 字节数                           │
│ 0x14 u32 dbg_size   调试块字节数（0 = stripDebug 省略）     │
│ 0x18 u16 max_literal_string_units  ┐ 静态极值（只记录）    │
│ 0x1A u16 max_literal_array_elems   │                       │
│ 0x1C u16 max_struct_slots          ┘                       │
│ 0x1E u16 rsvd = 0                                          │
│ 0x20 u32 all_crc32 （全量 CRC，计算时本字段清零）           │
├ Body ─────────────────────────────────────────────────────┤
│ UTF-8  主模块名（name_len B）                              │
│ u32 count + 常量条目×  （tag u8：Int3/UInt4=i32、           │
│        UInt64 5/Double 6/Ptr 10 = 64 位、String 7）         │
│ u32 count + 类型条目×   {u16 name; u8 nfields;             │
│        repeat(u16 name; u8 kind; u8 type; u8 elem; u16 ext)}│
│        ext：FixedArray=元素数 / Nested=sid；布局读侧递归展开│
│ u32 count + {u8 module_idx; u8 type}×   （全局表）          │
│ u32 count + UTF-8 名×   （原生名表，仅 L3）                 │
│ u32 count + {u16 nslots; u32 code_off}× （函数表，与头      │
│        func_count 交叉校验；code_off 相对 .text 起点）      │
│ .text（code_size B，v3 定长指令流）                         │
│ 调试块（dbg_size B，可选）：[u32 linesLen][lines]           │
│        [u32 namesLen][names]——lines = u32 funcCount +      │
│        repeat(u32 pairs + (i32 函数内相对字节偏移, i32 行))；│
│        names = u32 count + UTF-8（函数名 + 全局名，表序）   │
└────────────────────────────────────────────────────────────┘
```

- **stripDebug** = 省略调试块 + `dbg_size=0` + flags.D 清零 + CRC 重封——其余字节逐字节不变
  （`BytecodeSizeAnalysisTests.Guangshu_DebugStrip_CoreUnchangedAndSmaller` 字节级锁定）。
- midsieve strip 实测 226 B（TLV 440 B → −49%）。
- CLI `compile` 缺省 stripDebug；`--no-strip` 保留调试块。

## 2. ECM1（.ecm 平铺模块缓存）

仅 PC 侧编译缓存消费（进程缓存 `ProcessModuleCache` + obj/ 磁盘缓存 `ModuleCache`）；
反序列化等价即契约（`IntegrityCheck` 接口哈希自洽 + 逐字段往返测试）。

```
┌ Header (36 B) ─────────────────────────────────────────────┐
│ 0x00 u32 magic = "ECM1" (0x314D4345)                       │
│ 0x04 u16 format     = 1                                    │
│ 0x06 u16 abi_rev    = EcsSyscall.AbiRevision = 3           │
│ 0x08 u8  flags      bit0 HasEval / bit1 HasInit /          │
│                     bit2 KeyAction / bit3 NeedIL           │
│ 0x09 u8  name_len   模块名长度；0x24 起 UTF-8              │
│ 0x0A u16 func_count                                        │
│ 0x0C u32 code_size  .text 字节数                           │
│ 0x10 i32 init_fid   HasInit 时有效（否则 -1）               │
│ 0x14 u32 meta_size  元数据尾块字节数                        │
│ 0x18 u16 max_literal_string_units  ┐ 静态极值（与 ECX1 同位）│
│ 0x1A u16 max_literal_array_elems   │                        │
│ 0x1C u16 max_struct_slots          ┘                        │
│ 0x1E u16 rsvd = 0                                           │
│ 0x20 u32 all_crc32 （全量 CRC）                              │
├ Body ──────────────────────────────────────────────────────┤
│ UTF-8  模块名                                               │
│ u32 count + 常量条目×   （ModulePool 重建去重索引）          │
│ u32 count + 类型条目×   （与 ECX1 同布局）                   │
│ u32 count + {u16 name; u8 type}×   （全局表，模块味：名字）  │
│ u32 count + UTF-8 名×   （原生名表）                         │
│ u32 count + 交错函数记录×（与头 func_count 交叉校验）：      │
│        {u16 name; u8 nparams; u8 hasret; u16 nslots;        │
│         u32 code_off（相对 .text 起点）}                     │
│ .text（code_size B，v3 定长指令流）                          │
│ 元数据尾块（meta_size B）：                                  │
│   [u32 linesLen][lines]   行号块（布局同 ECX1 lines；        │
│                           缓存命中路径保留错误行号映射）      │
│   u32 count + {u16 name; u8 nparams; u8 hasret}×  （imports）│
│   u32 count + {u16 name; u32 localFid; u8 nparams;          │
│                 u8 hasret}×                        （exports）│
│   u32 count + UTF-8×   （IL 图像标签名）                     │
│   [u32 ifaceLen][iface]  ModuleInterfaceFormat 原样字节      │
│                          （0 = 无接口区；当前管线产物恒携带， │
│                          IntegrityCheck 依赖此区自洽）        │
└─────────────────────────────────────────────────────────────┘
```

- 模块不携带入口/链接结果：Call 局部 fid、导入标记 `0x80000000|导入表索引`、LoadK/全局槽/原生
  idx 均为模块局部，链接期在内存 `ModuleArtifact` 上隐式改写（不物化 .rela，V23 §6.3）。
- 旧 ECSC kind=module 缓存：magic 不符 → `BytecodeException` → 缓存按未命中重编（自动迁移）。

## 3. 错误码（加载/运行全集见 `ecs_vm.h`）

`IMAGE(3)`（magic/格式断代/严格等长违例）、`SLOT(5)`（槽位越界，含 `nslots ≤ ECS_MAX_SLOTS`）、
`OPCODE(4)`（操作码/跳转落点非法）、`IL(13)`/`FEAT(14)`（特征缺位）、`CRC(17)`、`POOL(18)`
（固定池耗尽）。TLV 时代的 `SECTION(15)/UNSUPPORTED(16)` 随段框架一并停用（编号保留不复用）。

## 4. 尺寸与演进

- 尺寸基线与门槛：[baselines/perf.md](../baselines/perf.md)；逐表核算测试 `BytecodeSizeAnalysisTests`
  （光速过帧 ECX1 全量 674 B / strip 446 B，门槛 ≤560 B 达标；TLV 基线 95-100 B 容器框架
  开销归零，ECSC 基线 532/536 B → 446 B）。
- **无多版本加载器**：magic/format/abi 不符 = 拒载（提示重新编译）；演进 = 版本断代 +
  全量重编，不做前向兼容读取。
