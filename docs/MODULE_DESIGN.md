# 伊机控 模块详细设计文档

## 1. Device模块详细设计

### 1.1 模块概述
Device模块负责处理与伊机控下位机的通讯功能，提供抽象接口给上层应用。

### 1.2 核心类设计

#### NintendoSwitch类
```csharp
public class NintendoSwitch
{
    public enum ConnectResult
    {
        Success,
        PortNotFound,
        AccessDenied,
        Timeout,
        DeviceNotResponding
    }
    
    public event Action<Status> StatusChanged;
    public ConnectResult TryConnect(string port);
    public void Disconnect();
    public bool SendCommand(byte[] command);
    public byte[] ReceiveData();
}
```

**主要职责**:
- 管理与任天堂Switch下位机的物理连接
- 处理命令发送和数据接收
- 监控连接状态变化

#### 连接类型支持

1. **串口连接 (Serial)**
   - 支持标准串口通信
   - 可配置波特率、数据位、停止位
   - 自动检测可用串口

2. **蓝牙连接 (Bluetooth)**
   - 支持蓝牙串口配置文件
   - 设备发现和配对
   - 连接状态监控

3. **网络连接 (TCP/IP)**
   - 支持WiFi连接
   - TCP套接字通信
   - 心跳检测机制

### 1.3 通信协议设计

#### Modbus协议实现
```csharp
public class ModbusProtocol
{
    public byte[] BuildReadRequest(byte slaveId, ushort startAddress, ushort count);
    public byte[] BuildWriteRequest(byte slaveId, ushort startAddress, byte[] data);
    public ModbusResponse ParseResponse(byte[] response);
}
```

#### 自定义串口协议
- 帧头标识：`0xAA 0x55`
- 数据长度：2字节
- 校验码：CRC16
- 帧尾标识：`0x0D 0x0A`

### 1.4 设备发现机制

#### 自动设备发现
```csharp
public class DeviceDiscovery
{
    public string[] FindSerialDevices();
    public string[] FindBluetoothDevices();
    public string[] FindNetworkDevices();
    public bool TestDeviceConnection(string devicePath);
}
```

### 1.5 错误处理与恢复

#### 连接异常处理
- 连接超时自动重试
- 异常断开自动重连
- 错误日志记录

#### 状态监控
- 心跳检测机制
- 连接质量监控
- 设备响应时间统计

---

## 2. Capture模块详细设计

### 2.1 模块概述
Capture模块实现UI无关的图像处理功能，基于OpenCV库实现高效的图像采集和处理。

### 2.2 核心类设计

#### OpenCVCapture类
```csharp
public class OpenCVCapture : IDisposable
{
    public int Width { get; }
    public int Height { get; }
    public int FPS { get; }
    
    public bool Open(string sourceName);
    public Mat CaptureFrame();
    public void Release();
    public bool IsConnected { get; }
}
```

**主要职责**:
- 管理视频采集设备
- 提供帧数据获取接口
- 设备生命周期管理

#### ECCapture静态类
```csharp
public static class ECCapture
{
    public static string[] GetCaptureCamera();
    public static IEnumerable<(string, int)> GetCaptureTypes();
    public static Mat CaptureFrame(int cameraIndex);
    public static bool CaptureToFile(int cameraIndex, string filename);
}
```

### 2.3 图像识别功能

#### 模板匹配
```csharp
public class TemplateMatcher
{
    public MatchResult FindTemplate(Mat source, Mat template, double threshold = 0.8);
    public List<MatchResult> FindMultipleTemplates(Mat source, Mat template, double threshold = 0.8);
    public MatchResult FindTemplateWithRotation(Mat source, Mat template, double threshold, float maxRotation = 360f);
}

public struct MatchResult
{
    public bool Found;
    public Point Location;
    public double Confidence;
    public Rect BoundingBox;
}
```

#### OCR识别
```csharp
public class OCREngine
{
    public OCREngine(string tessDataPath, string lang = "eng");
    public string RecognizeText(Mat image);
    public List<TextRegion> RecognizeTextWithRegions(Mat image);
    public void SetWhitelist(string characters);
    public void SetBlacklist(string characters);
}

public class TextRegion
{
    public Rect BoundingBox;
    public string Text;
    public double Confidence;
}
```

#### 图像预处理
```csharp
public class ImagePreprocessor
{
    public static Mat ConvertToGray(Mat source);
    public static Mat ApplyThreshold(Mat source, ThresholdType type, double threshold);
    public static Mat ApplyBlur(Mat source, BlurType type, int kernelSize);
    public static Mat EnhanceContrast(Mat source, double alpha, double beta);
    public static Mat RemoveNoise(Mat source, int kernelSize);
}
```

### 2.4 性能优化

#### 图像缓存机制
```csharp
public class ImageCache
{
    private readonly Dictionary<string, Mat> _cache = new();
    private readonly int _maxCacheSize;
    
    public Mat GetOrLoad(string imagePath);
    public void ClearCache();
    public void PreloadImages(string[] imagePaths);
}
```

#### 异步图像处理
```csharp
public class AsyncImageProcessor
{
    public Task<Mat> CaptureFrameAsync(CancellationToken cancellationToken = default);
    public Task<MatchResult> FindTemplateAsync(Mat source, Mat template, CancellationToken cancellationToken = default);
}
```

### 2.5 图像标签系统

#### ImgLabel类
```csharp
public class ImgLabel
{
    public string Name { get; set; }
    public string ImagePath { get; set; }
    public Mat Template { get; set; }
    public Dictionary<string, object> Metadata { get; set; }
    
    public void Load(string labelPath);
    public void Save(string labelPath);
    public bool Validate();
}
```

#### 标签管理器
```csharp
public class ImgLabelManager
{
    public ImgLabel LoadLabel(string labelPath);
    public List<ImgLabel> LoadLabelsFromDirectory(string directory);
    public void CreateLabel(string name, string imagePath, Dictionary<string, object> metadata);
    public void UpdateLabel(string labelPath, ImgLabel updatedLabel);
}
```

---

## 3. Script模块详细设计

### 3.1 模块概述
Script模块实现ECS脚本的解析和执行，采用编译器设计原理实现完整的脚本语言支持。

### 3.2 脚本语言语法

#### EBNF语法定义
```
main       ::= { decl | stmt }
decl       ::= importdecl | constdecl | arrdecl | funcdecl
stmt       ::= assignment | augassign | ifstmt | forstmt | callstmt | keyaction | stickaction
constdecl  ::= const '=' expr
assignment ::= suffexpr '=' expr
suffexpr   ::= var | indexvar
expr       ::= expr binop expr | unop expr | primary
primary    ::= number | string | bool | parenexpr | funccall
```

### 3.3 词法分析器

#### Lexer类
```csharp
public class Lexer
{
    public Lexer(string source);
    public IEnumerable<Token> Lex();
    
    private Token ReadNextToken();
    private char Peek(int offset = 0);
    private void Advance();
    private bool IsAtEnd();
}

public enum TokenType
{
    // 关键字
    If, Else, For, While, Func, Return, Import, Const,
    // 运算符
    Plus, Minus, Star, Slash, Percent,
    Equal, EqualEqual, BangEqual, Less, LessEqual, Greater, GreaterEqual,
    And, Or, Not,
    // 标识符和字面量
    Identifier, Number, String,
    // 分隔符
    LeftParen, RightParen, LeftBrace, RightBrace,
    LeftBracket, RightBracket, Semicolon, Comma,
    // 特殊
    EOF, Error
}

public class Token
{
    public TokenType Type { get; }
    public string Lexeme { get; }
    public object? Literal { get; }
    public int Line { get; }
    public int Column { get; }
}
```

### 3.4 语法分析器

#### Parser类
```csharp
public class Parser
{
    public Parser(IEnumerable<Token> tokens);
    public ProgramNode Parse();
    
    private ProgramNode ParseProgram();
    private StatementNode ParseStatement();
    private ExpressionNode ParseExpression();
    private DeclarationNode ParseDeclaration();
    
    // 错误处理
    public List<ParseError> Errors { get; }
    private Token Advance();
    private Token Peek();
    private bool Check(TokenType type);
    private bool Match(TokenType type);
}

public abstract class SyntaxNode
{
    public SyntaxKind Kind { get; }
    public TextSpan Span { get; }
}

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

### 3.5 抽象语法树

#### AST节点定义
```csharp
public abstract class AstNode
{
    public AstNodeType NodeType { get; }
    public TextSpan Location { get; }
    public IEnumerable<AstNode> Children { get; }
    
    public T Accept<T>(IAstVisitor<T> visitor);
    public void Accept(IAstVisitor visitor);
}

public interface IAstVisitor<T>
{
    T VisitProgramNode(ProgramNode node);
    T VisitIfStatementNode(IfStatementNode node);
    T VisitForStatementNode(ForStatementNode node);
    T VisitBinaryExpressionNode(BinaryExpressionNode node);
    // 其他访问方法...
}

public class ProgramNode : AstNode
{
    public List<DeclarationNode> Declarations { get; }
    public List<StatementNode> Statements { get; }
}

public class IfStatementNode : AstNode
{
    public ExpressionNode Condition { get; }
    public StatementNode ThenClause { get; }
    public StatementNode? ElseClause { get; }
}

public class BinaryExpressionNode : AstNode
{
    public ExpressionNode Left { get; }
    public ExpressionNode Right { get; }
    public BinaryOperator Operator { get; }
}
```

### 3.6 语义分析器

#### SemanticAnalyzer类
```csharp
public class SemanticAnalyzer
{
    public SemanticAnalyzer(DiagnosticBag diagnostics);
    public AnalyzedProgram Analyze(ProgramNode syntax);
    
    private SymbolTable CreateSymbolTable();
    private void AnalyzeDeclaration(DeclarationNode declaration);
    private void AnalyzeStatement(StatementNode statement);
    private TypeCheckResult AnalyzeExpression(ExpressionNode expression);
}

public class DiagnosticBag
{
    public IEnumerable<Diagnostic> Diagnostics { get; }
    public void ReportDiagnostic(Diagnostic diagnostic);
    public bool HasErrors { get; }
}

public class Diagnostic
{
    public DiagnosticSeverity Severity { get; }
    public string Message { get; }
    public TextSpan Location { get; }
    public DiagnosticCode Code { get; }
}
```

### 3.7 编译器

#### Compilation类
```csharp
public class Compilation
{
    public Compilation(string sourceCode);
    public Compilation(SyntaxTree syntaxTree);
    
    public Compilation Emit();
    public Evaluation GetEvaluator();
    
    public DiagnosticBag Diagnostics { get; }
}

public class Evaluator
{
    public Evaluator(Compilation compilation);
    public object? Evaluate();
    public object? EvaluateFunction(string name, object?[] arguments);
    
    private object? EvaluateStatement(StatementNode statement);
    private object? EvaluateExpression(ExpressionNode expression);
    
    // 执行上下文
    private Dictionary<string, object?> _variables = new();
    private Dictionary<string, FunctionDelegate> _functions = new();
    private readonly Stack<CallFrame> _callStack = new();
}
```

### 3.8 虚拟机设计

#### VM (Virtual Machine)
```csharp
public class VM
{
    private readonly Instruction[] _instructions;
    private readonly Stack<object?> _stack = new();
    private readonly Dictionary<string, object?> _globals = new();
    private int _ip; // Instruction Pointer
    
    public VM(Chunk chunk);
    public void Interpret();
    public object? Run();
    
    // 指令执行
    private void ExecuteInstruction(Instruction instruction);
    private void Push(object? value);
    private object? Pop();
    private object? Peek(int distance = 0);
}

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

### 3.9 绑定器

#### Binder类
```csharp
public class Binder
{
    public BoundProgram BindProgram(ProgramNode program);
    private BoundStatement BindStatement(StatementNode statement);
    private BoundExpression BindExpression(ExpressionNode expression);
    
    // 作用域管理
    private readonly Stack<BoundScope> _scopes = new();
    private void EnterScope();
    private void ExitScope();
    private bool TryLookupVariable(string name, out VariableSymbol variable);
}

public abstract class BoundNode
{
    public BoundNodeKind Kind { get; }
    public Type Type { get; }
}

public class BoundBinaryExpression : BoundExpression
{
    public BoundExpression Left { get; }
    public BoundExpression Right { get; }
    public BoundBinaryOperator Operator { get; }
}
```

### 3.10 输出适配器

#### IOutputAdapter接口
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

---

## 4. VPad模块详细设计

### 4.1 模块概述
VPad模块实现虚拟手柄UI，通过按键绑定将键盘操作映射到伊机控下位机。

### 4.2 核心类设计

#### IControllerAdapter接口
```csharp
public interface IControllerAdapter
{
    string Name { get; }
    ControllerType Type { get; }
    bool IsConnected { get; }
    
    event Action<ControllerEvent> OnInput;
    bool Connect();
    void Disconnect();
    ControllerState GetCurrentState();
}

public enum ControllerType
{
    Keyboard,
    Gamepad,
    VirtualController
}

public class ControllerEvent
{
    public ControllerInputType InputType { get; }
    public int Code { get; }
    public float Value { get; }
    public DateTime Timestamp { get; }
}
```

#### JCPainter类
```csharp
public class JCPainter
{
    public void DrawJoyCon(DrawingContext context, Point position, JoyConColor color, ControllerState state);
    public void DrawProController(DrawingContext context, Point position, ProControllerColor color, ControllerState state);
    public void DrawButton(DrawingContext context, ButtonType buttonType, Point position, bool isPressed);
    public void DrawStick(DrawingContext context, Point position, Point stickPosition, StickColor color);
    
    private void DrawControllerBody(DrawingContext context, Rect bounds, IBrush brush, IPen pen);
    private void DrawButtonHighlight(DrawingContext context, Rect bounds, bool isPressed);
}

public enum ButtonType
{
    A, B, X, Y,
    L, R, ZL, ZR,
    Plus, Minus, Home, Capture,
    DPadUp, DPadDown, DPadLeft, DPadRight,
    LeftStick, RightStick
}
```

### 4.3 按键映射系统

#### KeyMappingProfile类
```csharp
public class KeyMappingProfile
{
    public string Name { get; set; }
    public Dictionary<KeyCode, ControllerAction> Mappings { get; set; }
    
    public ControllerAction GetAction(KeyCode key);
    public void SetMapping(KeyCode key, ControllerAction action);
    public void RemoveMapping(KeyCode key);
    public void ClearMappings();
    public void Save(string filePath);
    public static KeyMappingProfile Load(string filePath);
}

public class ControllerAction
{
    public ActionType Type { get; }
    public NSKeys Button { get; }
    public StickType Stick { get; }
    public Point StickValue { get; }
    public float Duration { get; }
}

public enum ActionType
{
    ButtonPress,
    ButtonRelease,
    StickMove,
    StickReset,
    Combination
}
```

#### KeyMappingManager类
```csharp
public class KeyMappingManager
{
    public KeyMappingManager();
    public List<KeyMappingProfile> GetProfiles();
    public KeyMappingProfile CreateProfile(string name);
    public void DeleteProfile(string name);
    public KeyMappingProfile LoadProfile(string name);
    public void SaveProfile(KeyMappingProfile profile);
    public void SetActiveProfile(KeyMappingProfile profile);
    
    public ControllerAction? MapKey(KeyCode key);
    public void RegisterMapping(KeyCode key, ControllerAction action);
    public void UnregisterMapping(KeyCode key);
}
```

### 4.4 手柄状态管理

#### ControllerState类
```csharp
public class ControllerState
{
    public bool A { get; set; }
    public bool B { get; set; }
    public bool X { get; set; }
    public bool Y { get; set; }
    public bool L { get; set; }
    public bool R { get; set; }
    public bool ZL { get; set; }
    public bool ZR { get; set; }
    public bool Plus { get; set; }
    public bool Minus { get; set; }
    public bool Home { get; set; }
    public bool Capture { get; set; }
    
    public bool DPadUp { get; set; }
    public bool DPadDown { get; set; }
    public bool DPadLeft { get; set; }
    public bool DPadRight { get; set; }
    
    public Point LeftStick { get; set; }
    public Point RightStick { get; set; }
    
    public bool[] GetButtonStates();
    public void SetButtonState(ButtonType button, bool state);
    public bool GetButtonState(ButtonType button);
}
```

### 4.5 输入处理

#### InputHandler类
```csharp
public class InputHandler
{
    public event Action<ControllerAction>? OnAction;
    
    public void HandleKeyDown(KeyEvent keyEvent);
    public void HandleKeyUp(KeyEvent keyEvent);
    public void HandleGamepadButton(GamepadButtonEvent buttonEvent);
    public void HandleGamepadStick(GamepadStickEvent stickEvent);
    
    private ControllerAction? MapToAction(KeyEvent keyEvent);
    private void ProcessAction(ControllerAction action);
}
```

### 4.6 配置文件格式

#### JSON配置示例
```json
{
  "name": "默认配置",
  "description": "默认的按键映射配置",
  "version": "1.0",
  "mappings": {
    "KeyA": {
      "type": "ButtonPress",
      "button": "A",
      "duration": 50
    },
    "KeyB": {
      "type": "ButtonPress",
      "button": "B",
      "duration": 50
    },
    "KeyW": {
      "type": "StickMove",
      "stick": "LeftStick",
      "x": 0.0,
      "y": -1.0
    }
  }
}
```

---

## 5. 跨平台支持

### 5.1 平台抽象层

#### PlatformServices类
```csharp
public static class PlatformServices
{
    public static IPlatformService Current { get; }
    
    public static void SetPlatform(IPlatformService platform);
}

public interface IPlatformService
{
    PlatformType Platform { get; }
    string[] GetSerialPorts();
    string[] GetBluetoothDevices();
    INativeWindow CreateNativeWindow();
    IGamepadManager GetGamepadManager();
}

public enum PlatformType
{
    Windows,
    Linux,
    MacOS,
    Android
}
```

### 5.2 SDL2集成

#### SDL2GamepadManager类
```csharp
public class SDL2GamepadManager : IGamepadManager
{
    public event Action<IGamepad, GamepadEventType> OnGamepadEvent;
    
    public void Initialize();
    public void Shutdown();
    public List<IGamepad> GetConnectedGamepads();
    public IGamepad? GetGamepad(int index);
}
```

---

## 6. 测试与调试

### 6.1 单元测试框架

#### MockDevice类
```csharp
public class MockDevice : IDeviceService
{
    public List<string> CallLog { get; }
    public Queue<byte[]> ResponseQueue { get; set; }
    
    public void SetupMockResponses(params byte[][] responses);
    public void VerifyCall(string expectedCall);
    public void VerifyNoCalls();
}
```

### 6.2 性能监控

#### PerformanceMonitor类
```csharp
public class PerformanceMonitor
{
    public void StartMonitoring(string operationName);
    public void StopMonitoring(string operationName);
    public PerformanceReport GetReport();
    
    public void LogMemoryUsage();
    public void LogCPUUsage();
}

public class PerformanceReport
{
    public Dictionary<string, TimeSpan> OperationTimes { get; }
    public Dictionary<string, long> MemoryUsage { get; }
}
```

---

**文档版本**: 1.0  
**最后更新**: 2026年4月16日  
**维护者**: EasyCon2开发团队