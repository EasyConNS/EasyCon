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
Script模块实现ECS脚本的解析和执行，采用编译器设计原理实现完整的脚本语言支持。编译管线为 `SourceText → Lexer → Parser → Binder → Evaluator`，另有 `Assembly/` 用于生成单片机字节码。

### 3.2 目录结构

```
EasyCon.Script/
├── Text/                    # 源文本处理
│   ├── SourceText.cs        # 源文本表示
│   ├── TextLine.cs          # 文本行
│   ├── SourceSpan.cs        # 文本范围
│   └── TextLocation.cs      # 文本位置
├── Syntax/                  # 词法分析与语法分析
│   ├── Lexer.cs             # 词法分析器（partial）
│   ├── Parser.cs            # 语法分析器（partial）
│   ├── Parser.Fn.cs         # 函数解析（Parser partial）
│   ├── SyntaxTree.cs        # 语法树
│   ├── SyntaxNode.cs        # 语法节点基类
│   ├── Token.cs             # Token与TokenType定义
│   ├── TokenFacts.cs        # 关键字与Token查询
│   ├── Visitor.cs           # 访问者模式
│   └── Formatter.cs         # 代码格式化
├── Parsing/                 # 语法节点定义
│   ├── Statement.cs         # 语句基类
│   ├── ExprBase.cs          # 表达式基类
│   ├── IfStmt.cs            # if语句
│   ├── ForStmt.cs           # for循环
│   ├── FuncStmt.cs          # 函数声明
│   ├── AssignmentStmt.cs    # 赋值语句
│   ├── ConstantDeclStmt.cs  # 常量声明
│   ├── KeyActionStmt.cs     # 按键动作
│   ├── SerialPrint.cs       # 串口打印
│   └── Wait.cs              # 等待指令
├── Binding/                 # 绑定层（语义分析）
│   ├── Binder.cs            # 绑定器（类型检查+作用域）
│   ├── BoundProgram.cs      # 绑定后的程序
│   ├── BoundScope.cs        # 作用域管理
│   ├── BoundExpr.cs         # 绑定表达式节点
│   ├── BoundStmt.cs         # 绑定语句节点
│   ├── BoundFactory.cs      # 绑定节点工厂
│   ├── BoundNodeKind.cs     # 绑定节点类型枚举
│   ├── BoundNodePrinter.cs  # 绑定树打印
│   ├── BoundBinaryOperator.cs  # 二元运算符
│   ├── BoundUnaryOperator.cs   # 一元运算符
│   ├── BuildinFuncs.cs      # 内建函数
│   ├── ControlFlowGraph.cs  # 控制流图
│   ├── LinearScanRegisterAllocator.cs  # 寄存器分配
│   └── Lowerer.cs           # IR降低/优化
├── Symbols/                 # 符号系统
│   ├── Symbol.cs            # 符号层次（Symbol→VariableSymbol→Global/Local/Param, FunctionSymbol）
│   └── BoundValue.cs        # 运行时值类型（Value, ScriptType）
├── Assembly/                # 单片机字节码生成
│   ├── Assembler.cs         # 汇编器
│   ├── HexWriter.cs         # HEX格式写入
│   ├── Instruction.cs       # 指令基类
│   └── Instructions/        # 各指令实现（AsmKey, AsmStick, AsmFor等）
├── IO/                      # IO辅助
│   ├── CustomSleep.cs       # 高精度延时
│   └── TextWriterExtensions.cs  # 文本输出扩展
├── Compilation.cs           # 编译入口
├── Evaluator.cs             # 树解释执行器
├── Diagnostic.cs            # 诊断信息
├── DiagnosticBag.cs         # 诊断集合
├── IOutputAdapter.cs        # 输出适配器接口
├── ICGamePad.cs             # 手柄控制接口+GamePadKey枚举
└── NSKeys.cs                # NS按键定义
```

### 3.3 编译管线

#### Compilation类
```csharp
public sealed class Compilation
{
    public static Compilation Create(SyntaxTree syntaxTrees);
    public ImmutableArray<Diagnostic> Compile(ImmutableHashSet<string>? extVars);
    public EvaluationResult Evaluate(IOutputAdapter output, ICGamePad pad,
        ImmutableDictionary<string, Func<int>> externalGetters, CancellationToken token);
}
```

**管线流程**: `SourceText` → `Lexer.Tokenize()` → `Parser` → `SyntaxTree` → `Binder.BindProgram()` → `BoundProgram` → `Evaluator`

### 3.4 词法分析器

#### Lexer类（Syntax/Lexer.cs）
```csharp
internal sealed partial class Lexer(SyntaxTree syntaxTree)
{
    public DiagnosticBag Diagnostics { get; }
    public ImmutableArray<Token> Tokenize();
}
```

#### TokenType枚举（Syntax/Token.cs）
```csharp
public enum TokenType
{
    BadToken,
    // 空白与注释
    NEWLINE, WhitespaceTrivia, COMMENT,
    // 标识符与字面量
    IDENT, INT, Number, STRING,
    // 特殊变量
    CONST,    // _XXX
    VAR,      // $XXX
    EX_VAR,   // @XXX
    // 算术运算符
    ASSIGN, ADD, SUB, MUL, DIV, SlashI, MOD,
    // 位运算符
    BitAnd, BitOr, XOR, SHL, SHR, BitNot,
    // 复合赋值
    ADD_ASSIGN, SUB_ASSIGN, MUL_ASSIGN, DIV_ASSIGN,
    SlashI_ASSIGN, MOD_ASSIGN, BitAnd_ASSIGN, BitOr_ASSIGN,
    XOR_ASSIGN, SHL_ASSIGN, SHR_ASSIGN,
    // 比较运算符
    EQL, NEQ, LSS, GTR, LEQ, GEQ,
    // 逻辑运算符
    AND, OR, NOT,
    // 分隔符
    LPAREN, RPAREN, LBRACE, RBRACE, LBRACKET, RBRACKET,
    COMMA, COLON, SEMICOLON,
    // 关键字
    IF, ELSE, FOR, WHILE, FUNC, RETURN, BREAK, CONTINUE,
    // 特殊
    EOF
}
```

### 3.5 语法分析器

#### Parser类（Syntax/Parser.cs）
```csharp
internal sealed partial class Parser
{
    public Parser(SyntaxTree syntaxTree);
    public DiagnosticBag Diagnostics { get; }
}
```

Parser为partial类，函数解析逻辑在 `Parser.Fn.cs` 中。解析结果为 `SyntaxTree`，其根节点为 `CompicationUnit`。

### 3.6 绑定器

#### Binder类（Binding/Binder.cs）
```csharp
internal sealed class Binder
{
    public static BoundProgram BindProgram(SyntaxTree syntaxTree, ImmutableHashSet<string>? extVars);
    public DiagnosticBag Diagnostics { get; }
}
```

Binder将语法树绑定到类型化的Bound树，同时完成：
- **类型检查**: 确保表达式类型一致
- **作用域管理**: 通过 `BoundScope` 管理变量作用域
- **符号解析**: 解析变量、函数引用到 `Symbol` 对象

#### Lowerer（Binding/Lowerer.cs）
将高级绑定节点降低为更基础的IR，包含优化逻辑。

#### ControlFlowGraph（Binding/ControlFlowGraph.cs）
构建控制流图，用于分析和优化。

#### LinearScanRegisterAllocator（Binding/LinearScanRegisterAllocator.cs）
线性扫描寄存器分配器，为单片机字节码生成优化寄存器使用。

### 3.7 符号系统

#### Symbol层次（Symbols/Symbol.cs）
```csharp
abstract class Symbol(string name)
{
    public readonly string Name;
}

abstract class VariableSymbol(string name, bool isReadOnly, ScriptType type) : Symbol(name)
{
    public readonly ScriptType Type;
    public readonly bool IsReadOnly;
}

sealed class GlobalVariableSymbol : VariableSymbol
class LocalVariableSymbol : VariableSymbol
sealed class ParamSymbol : LocalVariableSymbol
sealed class FunctionSymbol : Symbol
```

### 3.8 执行器

#### Evaluator类
```csharp
internal sealed class Evaluator
{
    private readonly BoundProgram _program;
    private readonly Dictionary<VariableSymbol, Value> _globals;
    private readonly Stack<Dictionary<VariableSymbol, Value>> _locals;
    private readonly Dictionary<FunctionSymbol, BoundBlockStatement> _functions;
}
```

Evaluator直接遍历绑定树（BoundProgram）执行脚本，不经过虚拟机/字节码中间层。

### 3.9 输出接口

#### IOutputAdapter接口
```csharp
public interface IOutputAdapter
{
    void Print(string message, bool newline);
    void Alert(string message);
}
```

#### ICGamePad接口
```csharp
public interface ICGamePad
{
    abstract DelayType DelayMethod { get; }
    void ClickButtons(GamePadKey key, int duration, CancellationToken token);
    void PressButtons(GamePadKey key);
    void ReleaseButtons(GamePadKey key);
    void ClickStick(GamePadKey key, byte x, byte y, int duration, CancellationToken token);
    void SetStick(GamePadKey key, byte x, byte y);
    void ChangeAmiibo(uint index);
}
```

### 3.10 单片机字节码生成

#### Assembler（Assembly/Assembler.cs）
将绑定后的程序转换为单片机可执行的字节码，通过 `HexWriter` 输出HEX格式文件。`Instructions/` 目录包含各指令的汇编实现（AsmKey、AsmStick、AsmFor等）。

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