# ECS 模块系统 —— 接口式独立编译与缓存

> **版本 2.0**。本文档描述模块系统现状：每个 `.ecs` 文件是一个模块（编译单元），
> 产物为 `.ecm`（接口区 + 代码区）；消费者只读依赖的接口区，接口内容寻址缓存，源码或依赖接口不变则永不重编。
> 理论对应物：C++20 Named Modules（BMI/IFC）、Rust rmeta、Go build cache、OCaml `.cmi`。
> 关联：`docs/VM2.md`、`docs/EcmEcxFormat.md`（.ecm 位级布局）、`docs/Pipeline.md`（统一链路）。

## 1. 编译模型

```
main.ecs ─► ModuleGraphBuilder（IMPORT 递归展开 + 环检测 DFS + lib/ 自动加载）
             │  依赖模块只做 Lexer 令牌级导入扫描（建图不 parse；全量 parse 推迟到缓存未命中）
             ▼  拓扑序（std → vision → 依赖序 → main；DAG 强制，环 = 编译错误）
ModuleCompilePipeline（逐模块）：cacheKey 三路查找（disk .ecm / 进程缓存 / .err 重放）
             │  未命中 → parse → InterfaceScopeSynthesizer（依赖以接口区提供）
             │        → Binder 急切绑定 → SSA → 优化（导出为根）→ EcxModuleEncoder → 原子写回 obj/
             ▼
EcxPipeline.Link ─► EcxImage（桌面可带宽槽旁表；MCU 由 EcxWriter 校验后写为 ECX2）
```

- **无源码级合并**：任何编译只 parse 自己那份源码；stdlib（std/vision）内嵌源码随编译器发布，
  首次编译任一脚本时与用户模块同走 obj/ 缓存自然产出 `.ecm`，此后零解析。
- `lib/` 自动加载：main 同目录 `lib/*.ecs` 注册为隐式模块（显式 import 之后、main 之前编译，全局可见无 alias，按文件名序，跳过显式 import）。
- 关键接缝：接口合成的函数符号 `Declaration = null`，Binder 的 `EnsureFunctionBodyBound`
  天然跳过绑体、调用点直接生成 Call（体由链接期提供）——绑定器零改动消费接口。

## 2. 接口数据模型（`Bytecode/ModuleInterface.cs`）

| 项 | 内容 | 消费者 |
|----|------|--------|
| 导出函数表 | name、每参数（类型/序/默认值**载荷** int-double-string-bool）、返回类型、extern（DLL/导出名）、DeclLine | 调用解析、FFI |
| 类型表 | 结构体 + 字段（name/type/数组长度） | 字段绑定、布局重算 |
| ILNames / HasInit | 图像标签名 / 顶层语句标记 | 运行时标签校验 / `<init>` 合成 |
| Dependencies[] | (模块名, 接口哈希) | 失效校验、链接闭包 |
| 接口哈希 | SHA-256（§4） | 缓存键 + 完整性自检 |

**刻意排除项**：行号（不触发下游失效）、函数体、全局变量（模块私有，进 `<init:module>`）、语法节点。

## 3. 接口完备性清单（正确性论证 + 测试依据）

绑定消费者时依赖会读取的可见信息（逐项审计 Binder 得出；`InterfaceCompletenessSnapshotTests` 锁定）：

| # | 信息 | 读取点 | 进接口 |
|---|------|--------|--------|
| ① | 函数名与重载（参数个数） | TryLookupFuncs / ResolveOverload | ✅ 导出表 |
| ② | 参数/返回类型（含结构体） | 隐式转换插入、类型推导 | ✅ |
| ③ | 默认值**载荷** | 调用点物化为字面量 | ✅ 每参数 |
| ④ | 结构体字段名/类型/数组长度 | 字段绑定、LoadField | ✅ 类型表 |
| ⑤ | extern 的 DLL 名/导出名 | FunctionSymbol.ExternalName | ✅ |
| ⑥ | IL 标签名 | 运行时标签校验 | ✅ |
| ⑦ | lib 顶层语句 | — | ❌ 编译进该模块 `<init:module>`（其全局模块私有，消费者不可见） |
| ⑧ | 函数体 | 链接期 | ❌ 代码区 |
| ⑨ | Declaration 语法节点 | 惰性绑体/诊断 | ❌ 接口符号恒 null，零改动跳过 |

①–⑥ 进接口即满足 Cardelli 分离编译一致性；唯一系统性差异是跨模块诊断精度
（`模块名:行号`，无源码上下文）——可接受。**每次 Binder 新增依赖可见信息，必须对照本表更新接口模型。**

## 4. 接口哈希

```
interface_hash = SHA-256(CompilerVersion
  ⊕ sort(导出函数: name ⊕ paramTypes ⊕ hasDefault ⊕ defaultValue ⊕ returnType ⊕ isExtern)
  ⊕ sort(结构体: name ⊕ fields(name,type)) ⊕ ILNames ⊕ HasInit)
```

实现体改动若不改变接口哈希 → 仅该模块重编，下游缓存全命中（Merkle 收益，`Cache_MerkleInvalidation` 锁定）。

## 5. 缓存系统（`Modules/ModuleCache.cs`）

- **cacheKey** = SHA256(源码 ⊕ Σ直接依赖接口哈希 ⊕ 编译器版本 ⊕ `CompileOptions.ProductFingerprint()`)。
  影响产物的选项集中在 ProductFingerprint（Optimize / LegacySyntax / ExtVars）；其余选项不进键。
  文件名 = `<模块名>-<key 前 8 位>.ecm`，不同版本并存免锁竞争。
- **读**：全量校验 interface_hash（短码碰撞防御）+ `ModuleArtifact.IntegrityCheck()`（disk 与进程缓存两条 TryLoad 共享）；损坏按未命中重编。
- **写**：`temp + rename` 原子替换；成功后删除同键 `.err`。
- **错误缓存**：编译失败诊断持久化为 `.err` sidecar，同键命中即原样重报快速失败（ErrorHits 计数）；
  修复后源码变 → 新键自然失效。重放诊断统一经 `DiagnosticBag.FromMessage` 构造。
- **进程缓存**：仅 `UseDiskCache=false` 现编路径，存 EcmFormat 字节、命中反序列化出新实例（杜绝别名共享）。
- **GC**：运行末扫描 obj/，删除引用闭包之外且 mtime 超龄（默认 30 天，可配）的 .ecm/.err。

## 6. 语义保持要点

| 语义 | 行为 |
|------|------|
| 同签名跨模块导出 | 按拓扑/导入序注册导出，first-wins + `MD_AMBIGUOUS_EXPORT` 警告（同名同签名函数可共存） |
| alias 限定 `ns.func()` | 接口合成 scope，查表逻辑不变 |
| 循环导入 | 图解析阶段 DFS 检测，报环路径 |
| lib 全局变量 | 模块私有 + `<init>` 本地化，main 不可见 |
| lib 嵌套导入 / 顶层常量 | 允许（依赖图递归展开）；常量编译期折叠，模块内可见（`NestedLibImport_ConstAndCrossCalls`、`LibConstant_Parity`） |
| lib 顶层语句先于 main | 链接器在 `<main>` 头部按拓扑序前插 `<init>` 调用（无合成壳） |
| LSP/编辑器语义 | 源码级实时分析，不经模块管线（明确出界） |

## 7. 测试锚点

`ModuleProjectTests`（管线/诊断/警告/GC，M7 区段）、`Cache_MerkleInvalidation` 等缓存组
（`ModuleCacheOptimizationTests`）、`ModuleInterfaceTests`（round-trip）、
`InterfaceCompletenessSnapshotTests`（§3 清单快照）、`NestedLibImport_ConstAndCrossCalls`、
`LibConstant_Parity`、`Cache_StdVisionSharedAcrossProjects`。
