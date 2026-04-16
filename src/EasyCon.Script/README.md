# EasyCon.Script 模块

## 模块概述

Script模块用来实现ECP脚本的解析和执行。该模块实现了一个完整的脚本语言编译器和运行时系统，支持高级编程功能，为EasyCon2项目提供强大的自动化脚本能力。

### 核心组件
- **词法分析**: 将源代码文本转换为标记流
- **语法解析**: 构建抽象语法树(AST)
- **语义分析**: 类型检查和作用域分析
- **代码生成**: 生成中间代码或字节码
- **虚拟机执行**: 运行时脚本执行引擎

## 语言特性

### 基本语法
```ebnf
main       ::= { decl | stmt }
decl       ::= importdecl | constdecl | arrdecl | funcdecl
stmt       ::= assignment | augassign | ifstmt | forstmt | callstmt | keyaction | stickaction
constdecl  ::= const '=' expr
assignment ::= suffexpr '=' expr
suffexpr   ::= var | indexvar
expr       ::= expr binop expr | unop expr | primary
primary    ::= number | string | bool | parenexpr | funccall
```

### 数据类型
- **基本类型**: 整数、浮点数、字符串、布尔值
- **复合类型**: 数组、结构体
- **函数类型**: 支持一等函数和闭包

### 控制结构
- **条件语句**: if-else, switch-case
- **循环语句**: for, while, do-while
- **跳转语句**: break, continue, return

## 词法分析器 (Lexer)

### Token类型定义
```csharp
public enum TokenType
{
    // 关键字
    If, Else, For, While, Func, Return, Import, Const,
    
    // 运算符
    Plus, Minus, Star, Slash, Percent,
    Equal, EqualEqual, BangEqual, Less, LessEqual, Greater, GreaterEqual,
    And, Or, Not,
    
    // 标识符和字面量
    Identifier, Number, String, Boolean,
    
    // 分隔符
    LeftParen, RightParen, LeftBrace, RightBrace,
    LeftBracket, RightBracket, Semicolon, Comma,
    
    // 特殊
    EOF, Error
}
```

### 词法分析器实现
```csharp
public class Lexer
{
    private readonly string _source;
    private int _position;
    private int _line;
    private int _column;
    
    public Lexer(string source)
    {
        _source = source;
        _position = 0;
        _line = 1;
        _column = 1;
    }
    
    public IEnumerable<Token> Lex()
    {
        while (!IsAtEnd())
        {
            var token = ReadNextToken();
            if (token.Type != TokenType.Error)
            {
                yield return token;
            }
        }
        yield return new Token(TokenType.EOF, "", null, _line, _column);
    }
    
    private Token ReadNextToken()
    {
        SkipWhitespace();
        
        if (IsAtEnd()) return new Token(TokenType.EOF, "", null, _line, _column);
        
        char current = Peek();
        
        // 处理数字
        if (char.IsDigit(current)) return ReadNumber();
        
        // 处理标识符和关键字
        if (char.IsLetter(current) || current == '_') return ReadIdentifier();
        
        // 处理字符串
        if (current == '"') return ReadString();
        
        // 处理运算符
        return ReadOperator();
    }
}
```

## 语法分析器 (Parser)

### AST节点类型
```csharp
public enum SyntaxKind
{
    Program, 
    Statement, 
    Expression, 
    Declaration,
    // 具体语句类型
    IfStatement, ForStatement, WhileStatement,
    AssignmentStatement, ExpressionStatement,
    // 表达式类型
    BinaryExpression, UnaryExpression, LiteralExpression,
    IdentifierExpression, CallExpression,
    // 声明类型
    FunctionDeclaration, VariableDeclaration, ConstantDeclaration
}
```

### 语法分析器实现
```csharp
public class Parser
{
    private readonly List<Token> _tokens;
    private int _current;
    public List<ParseError> Errors { get; }
    
    public Parser(IEnumerable<Token> tokens)
    {
        _tokens = tokens.ToList();
        _current = 0;
        Errors = new List<ParseError>();
    }
    
    public ProgramNode Parse()
    {
        var declarations = new List<DeclarationNode>();
        var statements = new List<StatementNode>();
        
        while (!IsAtEnd())
        {
            if (CheckDeclaration())
            {
                declarations.Add(ParseDeclaration());
            }
            else
            {
                statements.Add(ParseStatement());
            }
        }
        
        return new ProgramNode(declarations, statements);
    }
    
    private ExpressionNode ParseExpression()
    {
        return ParseAssignment();
    }
    
    private ExpressionNode ParseAssignment()
    {
        var expr = ParseLogicalOr();
        
        if (Match(TokenType.Equal))
        {
            var value = ParseAssignment();
            return new AssignmentExpressionNode(expr, value);
        }
        
        return expr;
    }
    
    private ExpressionNode ParseLogicalOr()
    {
        var expr = ParseLogicalAnd();
        
        while (Match(TokenType.Or))
        {
            var op = Previous();
            var right = ParseLogicalAnd();
            expr = new BinaryExpressionNode(expr, op, right);
        }
        
        return expr;
    }
}
```

## 语义分析器 (Semantic Analyzer)

### 符号表
```csharp
public class SymbolTable
{
    private readonly Dictionary<string, Symbol> _symbols = new();
    private readonly SymbolTable? _parent;
    
    public SymbolTable(SymbolTable? parent = null)
    {
        _parent = parent;
    }
    
    public bool TryAddSymbol(Symbol symbol)
    {
        if (_symbols.ContainsKey(symbol.Name))
            return false;
        
        _symbols[symbol.Name] = symbol;
        return true;
    }
    
    public bool TryLookupSymbol(string name, out Symbol? symbol)
    {
        if (_symbols.TryGetValue(name, out symbol))
            return true;
        
        return _parent?.TryLookupSymbol(name, out symbol) ?? false;
    }
}

public abstract class Symbol
{
    public string Name { get; }
    public Type Type { get; }
    public SymbolKind Kind { get; }
}

public enum SymbolKind
{
    Variable,
    Function,
    Parameter,
    Constant
}
```

### 类型检查
```csharp
public class TypeChecker
{
    private readonly DiagnosticBag _diagnostics;
    
    public TypeChecker(DiagnosticBag diagnostics)
    {
        _diagnostics = diagnostics;
    }
    
    public Type CheckExpression(ExpressionNode expr)
    {
        switch (expr.Kind)
        {
            case SyntaxKind.LiteralExpression:
                return CheckLiteral((LiteralExpressionNode)expr);
            
            case SyntaxKind.BinaryExpression:
                return CheckBinary((BinaryExpressionNode)expr);
            
            case SyntaxKind.CallExpression:
                return CheckCall((CallExpressionNode)expr);
            
            default:
                return Type.Error;
        }
    }
    
    private Type CheckBinary(BinaryExpressionNode expr)
    {
        var leftType = CheckExpression(expr.Left);
        var rightType = CheckExpression(expr.Right);
        
        if (leftType == Type.Error || rightType == Type.Error)
            return Type.Error;
        
        // 类型兼容性检查
        if (!AreTypesCompatible(leftType, rightType, expr.Operator))
        {
            _diagnostics.ReportError($"类型不匹配: {leftType} {expr.Operator} {rightType}");
            return Type.Error;
        }
        
        return GetBinaryResultType(leftType, rightType, expr.Operator);
    }
}
```

## 编译器 (Compilation)

### 编译流程
```csharp
public class Compilation
{
    private readonly string _source;
    private readonly DiagnosticBag _diagnostics = new();
    
    public Compilation(string source)
    {
        _source = source;
    }
    
    public Evaluation Compile()
    {
        // 1. 词法分析
        var lexer = new Lexer(_source);
        var tokens = lexer.Lex().ToList();
        
        // 2. 语法分析
        var parser = new Parser(tokens);
        var syntax = parser.Parse();
        
        // 3. 语义分析
        var analyzer = new SemanticAnalyzer(_diagnostics);
        var program = analyzer.Analyze(syntax);
        
        // 4. 检查错误
        if (_diagnostics.HasErrors)
        {
            return new Evaluation(_diagnostics);
        }
        
        // 5. 生成可执行代码
        return new Evaluation(program, _diagnostics);
    }
}
```

## 虚拟机 (Virtual Machine)

### VM架构
```csharp
public class VM
{
    private readonly Chunk _chunk;
    private readonly Stack<object?> _stack = new();
    private readonly Dictionary<string, object?> _globals = new();
    private int _ip; // Instruction Pointer
    private bool _running = true;
    
    public VM(Chunk chunk)
    {
        _chunk = chunk;
        _ip = 0;
    }
    
    public void Interpret()
    {
        while (_running && _ip < _chunk.Code.Count)
        {
            var instruction = ReadInstruction();
            ExecuteInstruction(instruction);
        }
    }
    
    private void ExecuteInstruction(Instruction instruction)
    {
        switch (instruction.Opcode)
        {
            case OpCode.LoadConstant:
                var constant = _chunk.Constants[instruction.Operand];
                Push(constant);
                break;
                
            case OpCode.LoadGlobal:
                var name = (string)_chunk.Constants[instruction.Operand];
                Push(_globals[name]);
                break;
                
            case OpCode.StoreGlobal:
                var storeName = (string)_chunk.Constants[instruction.Operand];
                _globals[storeName] = Pop();
                break;
                
            case OpCode.Add:
                var b = Pop();
                var a = Pop();
                Push(Add(a, b));
                break;
                
            case OpCode.JumpIfFalse:
                var condition = (bool)Pop();
                if (!condition)
                {
                    _ip = instruction.Operand;
                }
                break;
                
            // ... 其他指令处理
        }
    }
}
```

### 操作码定义
```csharp
public enum OpCode
{
    // 常量和变量操作
    LoadConstant, LoadLocal, StoreLocal, LoadGlobal, StoreGlobal,
    
    // 算术运算
    Add, Sub, Mul, Div, Mod, Negate,
    
    // 比较运算
    Equal, NotEqual, Less, Greater, LessEqual, GreaterEqual,
    
    // 逻辑运算
    Not, And, Or,
    
    // 控制流
    Jump, JumpIfFalse, JumpIfTrue, Loop, Call, Return,
    
    // 特殊操作
    Print, Input, Delay
}
```

## 输出适配器 (Output Adapter)

### 接口定义
```csharp
public interface IOutputAdapter
{
    void SendKeyPress(NSKeys key);
    void SendKeyRelease(NSKeys key);
    void SendStickCommand(StickType stick, float x, float y);
    void Delay(int milliseconds);
    void Log(string message);
}

public enum NSKeys
{
    A, B, X, Y, L, R, ZL, ZR,
    Plus, Minus, Home, Capture,
    Up, Down, Left, Right,
    LStick, RStick
}

public enum StickType
{
    LeftStick, RightStick
}
```

### 游戏手柄适配器
```csharp
public class GamePadAdapter : IOutputAdapter
{
    private readonly IGamePad _gamePad;
    
    public GamePadAdapter(IGamePad gamePad)
    {
        _gamePad = gamePad;
    }
    
    public void SendKeyPress(NSKeys key)
    {
        _gamePad.PressKey(MapToKeyCode(key));
    }
    
    public void SendKeyRelease(NSKeys key)
    {
        _gamePad.ReleaseKey(MapToKeyCode(key));
    }
    
    public void SendStickCommand(StickType stick, float x, float y)
    {
        if (stick == StickType.LeftStick)
        {
            _gamePad.SetLeftStick(x, y);
        }
        else
        {
            _gamePad.SetRightStick(x, y);
        }
    }
    
    public void Delay(int milliseconds)
    {
        Thread.Sleep(milliseconds);
    }
    
    public void Log(string message)
    {
        Console.WriteLine(message);
    }
}
```

## 使用示例

### 基本脚本执行
```csharp
// 创建脚本编译器
var compilation = new Compilation(scriptSource);
var evaluation = compilation.Compile();

// 检查编译错误
if (evaluation.Diagnostics.HasErrors)
{
    foreach (var diagnostic in evaluation.Diagnostics)
    {
        Console.WriteLine($"Error: {diagnostic.Message}");
    }
    return;
}

// 执行脚本
evaluation.Execute();
```

### 带输出适配器的执行
```csharp
// 创建输出适配器
var adapter = new GamePadAdapter(device);

// 创建并配置脚本
var compilation = new Compilation(scriptSource);
var evaluation = compilation.Compile();
evaluation.SetOutputAdapter(adapter);

// 执行脚本
evaluation.Execute();
```

## 错误处理

### 诊断系统
```csharp
public class DiagnosticBag
{
    private readonly List<Diagnostic> _diagnostics = new();
    
    public IEnumerable<Diagnostic> Diagnostics => _diagnostics;
    public bool HasErrors => _diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);
    
    public void ReportError(string message, TextLocation location)
    {
        _diagnostics.Add(new Diagnostic(DiagnosticSeverity.Error, message, location));
    }
    
    public void ReportWarning(string message, TextLocation location)
    {
        _diagnostics.Add(new Diagnostic(DiagnosticSeverity.Warning, message, location));
    }
}

public class Diagnostic
{
    public DiagnosticSeverity Severity { get; }
    public string Message { get; }
    public TextLocation Location { get; }
    public DiagnosticCode Code { get; }
}
```

## 调试支持

### 调试器接口
```csharp
public interface IDebugger
{
    void OnLineStart(int line, string sourceFile);
    void OnLineEnd(int line, string sourceFile);
    void OnFunctionCall(string functionName);
    void OnFunctionReturn(string functionName, object? returnValue);
    void OnVariableChange(string variableName, object? newValue);
    bool ShouldBreak(int line);
}

public class Debugger : IDebugger
{
    private readonly HashSet<int> _breakpoints = new();
    
    public void SetBreakpoint(int line)
    {
        _breakpoints.Add(line);
    }
    
    public bool ShouldBreak(int line)
    {
        return _breakpoints.Contains(line);
    }
}
```

## 性能优化

### 编译优化
```csharp
public class Optimizer
{
    public ProgramNode Optimize(ProgramNode program)
    {
        // 常量折叠
        program = ConstantFold(program);
        
        // 死代码消除
        program = EliminateDeadCode(program);
        
        // 内联函数
        program = InlineFunctions(program);
        
        return program;
    }
}
```

## 依赖项

- **.NET**: System.Text, System.Collections
- **项目依赖**: EasyCon.Core, EasyCon.Device

## 未来发展

### 计划功能
- [ ] JIT编译器
- [ ] 更强大的类型系统
- [ ] 模块化导入系统
- [ ] 异步/等待支持
- [ ] 类和面向对象编程

---

**模块维护者**: EasyCon.Script开发团队  
**最后更新**: 2026年4月16日  
**版本**: 1.0