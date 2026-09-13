# 统一脚本编译链路

> C# 侧单一编译链路：**源码 → Parser → ProjectCompiler（模块图 + lib/ 自动加载）→ 每模块独立编译 →
> EcxImage**；桌面由 **EcxInterpreter** 执行 ECX 镜像，单片机由 **C VM** 执行同一二进制。
> 关联：`docs/ModuleSystem.md`（模块/缓存）、`docs/VmSemanticContract.md`（双端语义契约）、
> `docs/EcmEcxFormat.md`（二进制格式）、`docs/VM2.md`（指令集规格）。

## 1. 架构

```
源码 ─► Parser ─► ProjectCompiler（模块图 + lib/ 自动加载）
                     │  每模块：InterfaceScopeSynthesizer → Binder（模块单树绑定）→ SSA → 优化 → 编码
                     ▼
              EcxPipeline.Link ─► EcxImage（ECX 二进制）
                     ├─ 桌面：EcxVm 桥（IO/手柄/OCR/采集/文件IO/FFI 宿主装配）→ EcxInterpreter
                     └─ MCU ：EcxWriter → .ecx → C VM（ecs-vm）
```

入口函数 `<main>` = 依赖模块 `<init:module>` 调用序列按拓扑序前插 `$eval` 本体，
顶层 RETURN 经 r0 透传；`<init>` 承载各模块顶层语句与模块私有全局初始化。

## 2. 关键语义事实

- **lib/ 自动加载**：main 同目录 `lib/*.ecs` 注册为隐式模块（显式 import 之后、main 之前编译，
  全局可见无 alias；按文件名序，跳过显式 import）。
- **同签名跨模块导出**：链接期首匹配遮蔽 + `MD_AMBIGUOUS_EXPORT` 警告（同名同签名函数可共存）。
- **NeedIL 判定**：链接后在完整镜像上做入口可达性 BFS（采集洞 CallN）∪ 全局 Img 标签，
  据此设置镜像 NeedIL 标志。
- **Link 纯函数化**：在深拷贝副本上重写——不污染调用方产物，`CompileResult.Artifacts` 可安全
  .ecm roundtrip。
- **FWRITE 句柄 0**：写入 no-op 返回写入长度（S-14，见 VmSemanticContract）。
- **EXT 扫描铁律**：EXT 后随字是「数据」，数值可能恰好等于某个操作码——一切线性扫描必须按
  `EcsFormat.ExtWords` 步进跳过（C 侧 `ecs_op_has_ext`），否则把数据误读为指令。

## 3. 链接期行为

- **入口装配**：`EcxPipeline.Link` 在深拷贝副本上合并各模块产物并合成 `<main>`（见 §1），
  不污染调用方产物，`CompileResult.Artifacts` 可安全 .ecm roundtrip。
- **死代码消除**：自入口 BFS 调用图，不可达函数体不进镜像并重映射 fid；常量池/原生名表按
  保留函数实际引用压缩重映射（ECS 无间接调用，编码期闭合矩阵保证消除安全）。
  效果：nqueens_bitwise 1741→1097 B（−37%），15→6 函数；光速过帧 1273→604 B（−53%）。

## 4. 单一事实源落点（改对应知识只动一处）

| 知识 | 落点 |
|------|------|
| 指令格式/EXT 语义/结果槽 | `Bytecode/EcsFormat.cs`（漏登自检）+ C 侧 `ecs_op_has_ext` |
| 代码线性扫描 | `Bytecode/InstructionScanner.cs`（回调式，链接器各 pass 共享） |
| RC 槽写 | C# `MoveToSlot/StoreFresh/OverwriteItem`；C `move_to/store_fresh`（规则见 VmSemanticContract §二） |
| 缓存键选项 | `CompileOptions.ProductFingerprint()`；产物自检 `ModuleArtifact.IntegrityCheck()` |
| 模块诊断构造 | `DiagnosticBag.FromMessage` |
| 测试基建 | `test/EasyCon.Tests/Support/`（CvmRunner/EcsTestHost/CorpusAssert） |

## 5. 后续项（未实施）

- EasyRunner 暴露 `CompileEcx()` 给 GUI 一键产 `.ecx`。
- 桌面路径缓存化（当前桌面现编）。
- 运行错误位置映射：产物无行号表，需 pc→源行映射才能恢复源级定位。
- EcxInterpreter 性能优化（当前正确性优先）。
- 格式 v2 窗口四项（调用约定寄存器化/镜像 CRC/Link 三遍合一/模块并行编译）——设计备注见
  `docs/EcmEcxFormat.md` §5.1，实施须双端协商升版本。
