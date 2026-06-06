# EasyCon.Script 模块

## 模块概述

Script 模块实现自研 ECS 脚本语言的完整编译管线。管线为 `SourceText → Lexer → Parser → Resolution → Binder → SSA IR → SSA Optimize`，最终产出优化后的 SSA 中间表示。另有 `Assembly/` 子系统用于生成单片机 HEX 字节码。

## 编译管线

```
源代码 → Lexer（词法分析）→ Parser（语法分析）→ SyntaxTree
→ Resolution（文件加载+声明收集）→ Binder（绑定+类型检查）→ BoundProgram
→ SsaCodeGenerator（SSA 生成）→ SsaProgram → SsaOptimizer（SSA 优化）→ SsaProgram
```

### 各阶段职责

- **Lexer** — 将源文本转为 Token 流，支持关键字、标识符、字面量、位运算符、复合赋值运算符、特殊变量前缀（`_` 常量 / `$` 变量 / `@` 外部变量）等
- **Parser** — 递归下降语法分析，生成以 `CompilationUnit` 为根的语法树。为 partial 类，函数声明解析在 `Parser.Fn.cs` 中
- **Resolution** — 编排文件加载和顶层声明收集，包含两个子阶段：
  - `ImportResolver`：解析 import 声明、加载 lib 文件、检测循环导入、自动加载 lib/ 目录
  - `DeclarationCollector`：收集所有 SyntaxTree 的顶层声明（函数签名、extern、结构体），构建 `GlobalScope` 和 per-alias `ModuleScope`
- **Binder** — 将语法树绑定到类型化 Bound 树，同时完成类型检查、作用域管理（`BoundScope`）和符号解析。分为 `Binder.cs`（入口）、`Binder.Declarations.cs`、`Binder.Expressions.cs`、`Binder.Statements.cs`、`Binder.Calls.cs`
- **SSA 生成** — `SsaCodeGenerator` 将 Bound IR 转换为 SSA 形式的三地址码（`SsaBlock` + `SsaValue`），支持 Phi 节点、短路求值拆分、类型特化操作码
- **SSA 优化** — `SsaOptimizer` 驱动多遍优化至不动点：
  - 函数间：移除不可达函数、内联 trivial 函数、内联 intrinsic 包装函数
  - 函数内：SCCP 常量传播 → 代数化简 → 拷贝传播 → 全局 CSE → 死代码消除 → CFG 简化 → 常量去重（迭代至收敛）
- **Assembly**（可选）— `Assembler` 将绑定后的程序转换为 HEX 格式字节码，用于烧录到单片机独立执行

### 编译入口

```csharp
var compilation = Compilation.Create(SyntaxTree.Load(path));
var result = compilation.Compile(extVars); // CompileResult: Diagnostics + SsaProgram?
```

`CompileResult` 包含诊断信息、优化后的 `SsaProgram`、以及 `KeyAction` / `NeedIL` 标记。

### 编译性能分析

`CompilationTiming` 记录管线各阶段耗时（文件加载、词法分析、Import 解析、声明收集、绑定、SSA 生成、SSA 优化），支持子阶段细粒度追踪。

## 类型系统

### ScriptType 层次

```
ScriptType (abstract)
├── VoidType
├── ScalarType    — bool, byte, int, uint, uint64, double, string, ptr
├── ArrayType     — T[] 或 T[N]，元素类型 + 可选固定长度
├── StructType    — 用户定义的结构体，持有 EcsStructDef 引用
└── AnyType       — 多态内置函数的参数占位符
```

### Value（运行时值）

零装箱设计：`[StructLayout(LayoutKind.Explicit)]` 结构体（32B），通过 tag byte 区分类型，int/double/long 共享偏移量避免装箱。支持 `bool` / `byte` / `int` / `uint` / `uint64` / `double` / `string` / `ptr` / `array` / `struct` 共 10 种值类型。

### 符号层次

```
Symbol → VariableSymbol → GlobalVariableSymbol / LocalVariableSymbol → ParamSymbol
Symbol → FunctionSymbol（含 LibraryName/ExternalName，支持 extern 函数）
Symbol → ModuleSymbol（命名空间模块）
```

`SlotDesc` + `FrameLayout` 实现类型化帧布局（Int / Long / Double / Handle 四类 slot），避免统一 object 数组的装箱开销。

### 强类型数组

`ScriptArray` 抽象基类派生出 `IntArray` / `BoolArray` / `ByteArray` / `UIntArray` / `UInt64Array` / `DoubleArray` / `StringArray` / `LongArray` / `ValueArray` 共 9 种特化实现，支持原地修改（`SetItem`）和函数式操作（`Append` / `AddRange` / `GetRange`）。

### EcsStruct（结构体运行时）

用户定义的结构体通过 `EcsStructDef`（字段定义 + `StructLayout.Calculate` 自动计算对齐和偏移）+ `EcsStruct`（`Marshal.AllocHGlobal` 原生内存，支持嵌套结构体、数组字段、跨平台字符串封送）实现。`TypeLayout` 统一处理各类型的原生大小和对齐。

## SSA IR

### SsaValue

三地址码形式：`Result = Op(Arg0, Arg1)`。每个 SSA 值持有操作码（`SsaOp`）、类型、常量载荷（`ConstPayload`，显式布局零装箱）、辅助信息（`Aux`）、引用计数（`Uses`）和所属基本块。

### SsaOp 操作码

类型特化设计，每个操作的输入输出类型在编译期确定：
- 常量：`ConstBool` / `ConstByte` / `ConstInt` / `ConstUInt` / `ConstUInt64` / `ConstDouble` / `ConstString` / `ConstPtr`
- 加载/存储：`LoadLocal` / `StoreLocal` / `LoadGlobal` / `StoreGlobal`
- 算术：按类型特化（`AddInt` / `AddUInt` / `AddDouble` / `AddUInt64` 等）
- 比较：按类型特化（`EqInt` / `LtUInt` / `GtDouble` 等）
- 类型转换：`ConvBoolToInt` / `ConvIntToDouble` / `ConvDoubleToInt` 等
- 控制流：`Phi` / `Return`
- 调用：`Call`（外部函数）/ `StaticCall`（内置+用户定义）
- 复合数据：`ArrayInit` / `LoadIndex` / `StoreIndex` / `Slice` / `Concat` / `StructInit` / `LoadField` / `StoreField`
- 领域操作：`KeyAction` / `KeyPress` / `StickAction` / `StickPress` / `Wait` / `Rand`
- 视觉：`Capture` / `Ocr` / `Roi`

### SsaBlock

基本块，包含指令列表、Phi 节点列表、分支条件（`BranchCondition`）、True/False 后继或无条件跳转目标（`JumpTarget`）。

## 标准库

内嵌 `StdLib` 提供两个标准库脚本（`GetStdTree()` / `GetVisionTree()`），定义文件句柄常量、`PRINT` / `TIME` / `FRAME` 等基础函数。标准库在 Resolution 阶段最先加载。

## 输出接口

脚本通过接口与外部交互：
- **IIoAdapter** — 文本输入输出（Print / Alert / ReadLine）
- **ICGamePad** — 手柄控制（按键点击/释放、摇杆、Amiibo）
- **委托** — `OcrDelegate` / `FrameDelegate` / `RoiDelegate` / `LabelMatchDelegate`（视觉和标签匹配）

## 辅助子系统

- **Formatter** — 语法树格式化，管理运行时常量（特殊变量前缀到类型的映射）
- **Syntax/Visitor** — 语法树访问者模式
- **ControlFlowGraph** — 控制流图构建（Binding 阶段内部使用）
- **BuildinFuncs** — 内置函数注册（`PRINT` / `WAIT` / `RAND` / `INT` / `STR` / `LENGTH` / `APPEND` / `CAPTURE` / `OCR` / `ROI` 等），标记为 intrinsic 的函数在 SSA 生成阶段直接展开为原子操作

## 诊断系统

`DiagnosticBag` 收集词法 / 语法 / Resolution / 绑定各阶段的错误和警告，编译失败时返回诊断集合而非抛异常。

## 目录结构

```
EasyCon.Script/
├── Assembly/          单片机字节码生成
│   └── Instructions/  汇编指令实现
├── Binding/           绑定阶段（类型检查、作用域、Bound 树）
├── IO/                TextWriter 扩展
├── Parsing/           AST 节点定义（语句、表达式）
├── Resolution/        文件加载与声明收集
├── Runtime/           运行时类型（EcsStruct、ScriptArray）
├── Ssa/               SSA IR 生成与优化
├── Symbols/           符号系统（Value、ScriptType、Symbol）
├── Syntax/            词法分析、语法分析、格式化
└── Text/              源文本基础设施（SourceText、TextLine、TextLocation）
```

## 依赖项

- 无外部 NuGet 依赖
- 上层通过 `Compilation` 类和委托接口使用
- `InternalsVisibleTo` 暴露给 `EasyCon.Lsp`、`EasyCon.Lsp.Tests`、`EasyCon.Tests`、`BenchTest`

---

**版本**: 3.0
