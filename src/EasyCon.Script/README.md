# EasyCon.Script 模块

## 模块概述

Script模块实现自研ECS脚本语言的完整编译管线和运行时。管线为 `SourceText → Lexer → Parser → Binder → Evaluator`，直接解释执行绑定树，不经过虚拟机/字节码中间层。另有 `Assembly/` 子系统用于生成单片机字节码。

## 编译管线

```
源代码 → Lexer（词法分析）→ Token流 → Parser（语法分析）→ SyntaxTree
→ Binder（绑定+类型检查）→ BoundProgram → Evaluator（树解释执行）
```

### 各阶段职责

- **Lexer** — 将源文本转为Token流，支持关键字、标识符、字面量、位运算符、复合赋值运算符、特殊变量前缀（`_`常量/`$`变量/`@`外部变量）等
- **Parser** — 递归下降语法分析，生成以 `CompicationUnit` 为根的语法树。为partial类，函数声明解析在 `Parser.Fn.cs` 中
- **Binder** — 将语法树绑定到类型化Bound树，同时完成类型检查、作用域管理（BoundScope）和符号解析
- **Lowerer** — IR降低与优化变换
- **ControlFlowGraph** — 构建控制流图
- **LinearScanRegisterAllocator** — 线性扫描寄存器分配，为单片机字节码优化
- **Evaluator** — 遍历BoundProgram直接执行，维护全局/局部变量表和函数表

### 符号系统
符号层次为 `Symbol → VariableSymbol → Global/Local/Param` 和 `FunctionSymbol`，`Value` 类型包装运行时值。

### 单片机字节码
`Assembler` 将绑定后的程序转换为HEX格式字节码，用于烧录到单片机独立执行。`Instructions/` 目录包含各指令的汇编实现。

## 输出接口

脚本通过两个接口与外部交互：
- **IOutputAdapter** — 文本输出（Print/Alert），用于脚本 `print` 和 `alert` 指令
- **ICGamePad** — 手柄控制（按键点击/释放、摇杆、Amiibo），用于按键和摇杆指令

## 诊断系统

`DiagnosticBag` 收集词法/语法/绑定各阶段的错误和警告，编译失败时返回诊断集合而非抛异常。

## 依赖项

- 无外部NuGet依赖
- 上层通过 `Compilation` 和 `IOutputAdapter`/`ICGamePad` 接口使用

---

**版本**: 2.0
