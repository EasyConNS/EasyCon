# EasyCon.VPad 模块

## 模块概述

VPad模块用来实现一个虚拟手柄UI，通过按键绑定的形式将键盘操作通过Device模块提供的接口映射到伊机控下位机。该模块提供了友好的用户界面，让用户可以方便地配置和使用虚拟手柄功能。

### 主要功能
- **UI 界面及动画效果**: 提供JoyCon和Pro手柄的视觉效果
- **按键映射**: 支持键盘到手柄的映射配置（考虑跨平台方案SDL2）
- **映射配置文件**: 支持配置文件保存和加载

## 核心组件

### IControllerAdapter 接口
控制器适配器的基础接口，支持不同类型的输入设备：

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
```

### JCPainter 类
负责绘制手柄UI和视觉效果：

```csharp
public class JCPainter
{
    public void DrawJoyCon(DrawingContext context, Point position, 
                          JoyConColor color, ControllerState state);
    public void DrawProController(DrawingContext context, Point position, 
                                 ProControllerColor color, ControllerState state);
    public void DrawButton(DrawingContext context, ButtonType buttonType, 
                          Point position, bool isPressed);
    public void DrawStick(DrawingContext context, Point position, 
                         Point stickPosition, StickColor color);
    
    private void DrawControllerBody(DrawingContext context, Rect bounds, 
                                   IBrush brush, IPen pen);
    private void DrawButtonHighlight(DrawingContext context, Rect bounds, 
                                   bool isPressed);
}
```

### KeyMapping 类
按键映射配置管理：

```csharp
public class KeyMapping
{
    public Dictionary<KeyCode, ControllerAction> Mappings { get; set; }
    
    public KeyMapping()
    {
        Mappings = new Dictionary<KeyCode, ControllerAction>();
    }
    
    public void AddMapping(KeyCode key, ControllerAction action)
    {
        Mappings[key] = action;
    }
    
    public void RemoveMapping(KeyCode key)
    {
        Mappings.Remove(key);
    }
    
    public ControllerAction? GetAction(KeyCode key)
    {
        return Mappings.TryGetValue(key, out var action) ? action : null;
    }
}
```

### LowLevelKeyboard 类
底层键盘钩子，用于捕获全局键盘输入：

```csharp
public class LowLevelKeyboard : IDisposable
{
    public event Action<KeyEvent>? OnKeyDown;
    public event Action<KeyEvent>? OnKeyUp;
    
    public bool Start();
    public void Stop();
    
    protected virtual void LowLevelKeyboardHookProc(KeyEvent keyEvent);
}

public class KeyEvent
{
    public KeyCode KeyCode { get; }
    public bool IsPressed { get; }
    public DateTime Timestamp { get; }
    public int ModifierFlags { get; }
}
```

## 手柄类型支持

### JoyCon 手柄
```csharp
public class JoyConController
{
    public enum JoyConSide
    {
        Left,
        Right
    }
    
    public JoyConSide Side { get; set; }
    public JoyConColor Color { get; set; }
    
    // Left JoyCon 按钮
    public bool DPadUp { get; set; }
    public bool DPadDown { get; set; }
    public bool DPadLeft { get; set; }
    public bool DPadRight { get; set; }
    public bool Minus { get; set; }
    public bool L { get; set; }
    public bool ZL { get; set; }
    public bool LStick { get; set; }
    public Point LeftStickPosition { get; set; }
    
    // Right JoyCon 按钮
    public bool A { get; set; }
    public bool B { get; set; }
    public bool X { get; set; }
    public bool Y { get; set; }
    public bool Plus { get; set; }
    public bool R { get; set; }
    public bool ZR { get; set; }
    public bool RStick { get; set; }
    public Point RightStickPosition { get; set; }
}

public struct JoyConColor
{
    public Color BodyColor { get; set; }
    public Color ButtonColor { get; set; }
}
```

### Pro Controller 手柄
```csharp
public class ProController
{
    public ProControllerColor Color { get; set; }
    
    // 标准按钮布局
    public bool A { get; set; }
    public bool B { get; set; }
    public bool X { get; set; }
    public bool Y { get; set; }
    
    // 肩键
    public bool L { get; set; }
    public bool R { get; set; }
    public bool ZL { get; set; }
    public bool ZR { get; set; }
    
    // 系统按钮
    public bool Plus { get; set; }
    public bool Minus { get; set; }
    public bool Home { get; set; }
    public bool Capture { get; set; }
    
    // 方向键
    public bool DPadUp { get; set; }
    public bool DPadDown { get; set; }
    public bool DPadLeft { get; set; }
    public bool DPadRight { get; set; }
    
    // 摇杆
    public Point LeftStickPosition { get; set; }
    public Point RightStickPosition { get; set; }
    public bool LStick { get; set; }
    public bool RStick { get; set; }
}

public struct ProControllerColor
{
    public Color BodyColor { get; set; }
    public Color GripColor { get; set; }
    public Color ButtonColor { get; set; }
}
```

## 按键映射系统

### 映射配置文件
```json
{
  "version": "1.0",
  "name": "默认映射配置",
  "description": "键盘到手柄的默认映射配置",
  "controllerType": "ProController",
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
    "KeyX": {
      "type": "ButtonPress",
      "button": "X",
      "duration": 50
    },
    "KeyY": {
      "type": "ButtonPress",
      "button": "Y",
      "duration": 50
    },
    "KeyW": {
      "type": "StickMove",
      "stick": "LeftStick",
      "x": 0.0,
      "y": -1.0
    },
    "KeyS": {
      "type": "StickMove",
      "stick": "LeftStick",
      "x": 0.0,
      "y": 1.0
    },
    "KeyA": {
      "type": "StickMove",
      "stick": "LeftStick",
      "x": -1.0,
      "y": 0.0
    },
    "KeyD": {
      "type": "StickMove",
      "stick": "LeftStick",
      "x": 1.0,
      "y": 0.0
    }
  }
}
```

### 映射管理器
```csharp
public class KeyMappingManager
{
    private readonly Dictionary<string, KeyMapping> _profiles = new();
    private KeyMapping _activeProfile;
    
    public KeyMappingManager()
    {
        _activeProfile = CreateDefaultProfile();
    }
    
    public List<string> GetProfileNames()
    {
        return _profiles.Keys.ToList();
    }
    
    public KeyMapping CreateProfile(string name)
    {
        var profile = new KeyMapping { Name = name };
        _profiles[name] = profile;
        return profile;
    }
    
    public void SaveProfile(string name, string filePath)
    {
        var profile = _profiles[name];
        var json = JsonSerializer.Serialize(profile, new JsonSerializerOptions
        {
            WriteIndented = true
        });
        File.WriteAllText(filePath, json);
    }
    
    public KeyMapping LoadProfile(string filePath)
    {
        var json = File.ReadAllText(filePath);
        var profile = JsonSerializer.Deserialize<KeyMapping>(json);
        _profiles[profile.Name] = profile;
        return profile;
    }
    
    public void SetActiveProfile(string name)
    {
        _activeProfile = _profiles[name];
    }
    
    public ControllerAction? MapKey(KeyCode key)
    {
        return _activeProfile.GetAction(key);
    }
}
```

## 跨平台支持

### SDL2集成
考虑跨平台的游戏手柄支持，可以使用SDL2库：

```csharp
public class SDL2ControllerAdapter : IControllerAdapter
{
    private IntPtr _gamepadHandle;
    
    public string Name => "SDL2 Gamepad Adapter";
    public ControllerType Type => ControllerType.Gamepad;
    public bool IsConnected { get; private set; }
    
    public event Action<ControllerEvent>? OnInput;
    
    public bool Connect()
    {
        // 初始化SDL2
        if (SDL.SDL_Init(SDL.SDL_INIT_JOYSTICK | SDL.SDL_INIT_GAMECONTROLLER) < 0)
        {
            return false;
        }
        
        // 打开游戏手柄
        _gamepadHandle = SDL.SDL_GameControllerOpen(0);
        if (_gamepadHandle == IntPtr.Zero)
        {
            return false;
        }
        
        IsConnected = true;
        return true;
    }
    
    public ControllerState GetCurrentState()
    {
        var state = new ControllerState();
        
        // 读取按钮状态
        state.A = SDL.SDL_GameControllerGetButton(_gamepadHandle, 
            SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_A) == 1;
        state.B = SDL.SDL_GameControllerGetButton(_gamepadHandle, 
            SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_B) == 1;
        // ... 其他按钮
        
        // 读取摇杆状态
        int xValue = SDL.SDL_GameControllerGetAxis(_gamepadHandle, 
            SDL.SDL_GameControllerAxis.SDL_CONTROLLER_AXIS_LEFTX);
        int yValue = SDL.SDL_GameControllerGetAxis(_gamepadHandle, 
            SDL.SDL_GameControllerAxis.SDL_CONTROLLER_AXIS_LEFTY);
        
        state.LeftStick = new Point(
            NormalizeAxis(xValue), 
            NormalizeAxis(yValue)
        );
        
        return state;
    }
    
    public void Disconnect()
    {
        if (_gamepadHandle != IntPtr.Zero)
        {
            SDL.SDL_GameControllerClose(_gamepadHandle);
            _gamepadHandle = IntPtr.Zero;
        }
        
        SDL.SDL_Quit();
        IsConnected = false;
    }
    
    private float NormalizeAxis(int value)
    {
        return value < 0 ? value / 32768.0f : value / 32767.0f;
    }
}
```

## UI组件

### FormKeyMapping (WinForms)
```csharp
public class FormKeyMapping : Form
{
    private KeyMapping _currentMapping;
    private IControllerAdapter _adapter;
    
    public FormKeyMapping(KeyMapping mapping, IControllerAdapter adapter)
    {
        _currentMapping = mapping;
        _adapter = adapter;
        InitializeComponents();
    }
    
    private void InitializeComponents()
    {
        // 创建按键映射界面
        // 包括键盘选择、手柄按钮选择、映射按钮等
    }
    
    private void OnRecordButtonClick(object sender, EventArgs e)
    {
        // 开始录制按键映射
        var keyboardHook = new LowLevelKeyboard();
        keyboardHook.OnKeyDown += OnKeyDown;
        keyboardHook.Start();
    }
    
    private void OnKeyDown(KeyEvent keyEvent)
    {
        // 记录按下的键，并显示在界面上
        UpdateKeyDisplay(keyEvent.KeyCode);
    }
}
```

### VPadForm (虚拟手柄窗体)
```csharp
public class VPadForm : Form
{
    private ControllerState _currentState;
    private JCPainter _painter;
    private ControllerType _controllerType;
    
    public VPadForm(ControllerType controllerType)
    {
        _controllerType = controllerType;
        _painter = new JCPainter();
        InitializeComponents();
    }
    
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        
        switch (_controllerType)
        {
            case ControllerType.JoyCon:
                _painter.DrawJoyCon(e.Graphics, ClientRectangle, 
                    GetCurrentColor(), _currentState);
                break;
                
            case ControllerType.ProController:
                _painter.DrawProController(e.Graphics, ClientRectangle, 
                    GetCurrentColor(), _currentState);
                break;
        }
    }
    
    public void UpdateState(ControllerState state)
    {
        _currentState = state;
        Invalidate(); // 触发重绘
    }
}
```

## 输入处理

### InputHandler 类
```csharp
public class InputHandler
{
    private readonly IControllerAdapter _adapter;
    private readonly KeyMapping _mapping;
    private readonly IOutputAdapter _output;
    
    public event Action<ControllerAction>? OnAction;
    
    public InputHandler(IControllerAdapter adapter, KeyMapping mapping, 
                       IOutputAdapter output)
    {
        _adapter = adapter;
        _mapping = mapping;
        _output = output;
        
        _adapter.OnInput += HandleInput;
    }
    
    private void HandleInput(ControllerEvent controllerEvent)
    {
        var action = _mapping.GetAction(controllerEvent.Code);
        if (action != null)
        {
            OnAction?.Invoke(action);
            ExecuteAction(action);
        }
    }
    
    private void ExecuteAction(ControllerAction action)
    {
        switch (action.Type)
        {
            case ActionType.ButtonPress:
                _output.SendKeyPress(action.Button);
                Task.Delay(action.Duration).ContinueWith(_ => 
                    _output.SendKeyRelease(action.Button));
                break;
                
            case ActionType.StickMove:
                _output.SendStickCommand(action.Stick, 
                    action.StickValue.X, action.StickValue.Y);
                break;
                
            case ActionType.StickReset:
                _output.SendStickCommand(action.Stick, 0, 0);
                break;
        }
    }
}
```

## 使用示例

### 基本使用
```csharp
// 创建虚拟手柄适配器
var adapter = new KeyboardControllerAdapter();
adapter.Connect();

// 加载按键映射配置
var mapping = KeyMappingManager.LoadProfile("default_mapping.json");

// 创建输出适配器
var output = new GamePadOutputAdapter(device);

// 创建输入处理器
var handler = new InputHandler(adapter, mapping, output);

// 启动虚拟手柄窗体
var vpadForm = new VPadForm(ControllerType.ProController);
vpadForm.Show();

adapter.OnInput += (e) => 
{
    vpadForm.UpdateState(adapter.GetCurrentState());
};
```

### 动态映射配置
```csharp
// 打开映射配置窗口
var mappingForm = new FormKeyMapping(currentMapping, adapter);
if (mappingForm.ShowDialog() == DialogResult.OK)
{
    // 保存新的映射配置
    KeyMappingManager.SaveProfile("custom_mapping.json", currentMapping);
}
```

## 性能优化

### 输入缓冲
```csharp
public class InputBuffer
{
    private readonly Queue<ControllerEvent> _buffer = new();
    private readonly int _maxBufferSize = 100;
    
    public void AddEvent(ControllerEvent evt)
    {
        if (_buffer.Count >= _maxBufferSize)
        {
            _buffer.Dequeue();
        }
        _buffer.Enqueue(evt);
    }
    
    public ControllerEvent[] GetEvents()
    {
        var events = _buffer.ToArray();
        _buffer.Clear();
        return events;
    }
}
```

## 调试工具

### 按键监控器
```csharp
public class KeyMonitor : Form
{
    private ListBox _keyListBox;
    private LowLevelKeyboard _keyboardHook;
    
    public KeyMonitor()
    {
        _keyboardHook = new LowLevelKeyboard();
        _keyboardHook.OnKeyDown += OnKeyDown;
        _keyboardHook.Start();
    }
    
    private void OnKeyDown(KeyEvent keyEvent)
    {
        Invoke((MethodInvoker)delegate
        {
            _keyListBox.Items.Add($"Key: {keyEvent.KeyCode}, Time: {keyEvent.Timestamp}");
        });
    }
}
```

## 依赖项

- **.NET**: System.Windows.Forms, System.Drawing
- **跨平台**: SDL2 (可选)
- **项目依赖**: EasyCon.Core, EasyCon.Device

## 未来发展

### 计划功能
- [ ] 更多手柄型号支持
- [ ] 触摸板支持
- [ ] 体感控制
- [ ] 云端配置同步
- [ ] 宏命令录制

### UI改进
- [ ] 现代化界面设计
- [ ] 实时按键显示
- [ ] 动画效果优化
- [ ] 主题系统

---

**模块维护者**: EasyCon.VPad开发团队  
**最后更新**: 2026年4月16日  
**版本**: 1.0