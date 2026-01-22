using SharpDX.XInput;

namespace DirectInput;

internal class XboxControllerMonitor
{
    private Controller _controller;
    private bool _isMonitoring = false;
    private Thread _monitorThread;

    // 用于存储按键状态的字典
    private Dictionary<GamepadButtonFlags, bool> _buttonStates = new Dictionary<GamepadButtonFlags, bool>();

    // 事件：当按键状态变化时触发
    public event EventHandler<ButtonStateChangedEventArgs> ButtonStateChanged;

    public XboxControllerMonitor(UserIndex userIndex = UserIndex.One)
    {
        _controller = new Controller(userIndex);
        InitializeButtonStates();
    }

    private void InitializeButtonStates()
    {
        // 初始化所有按钮状态为false
        foreach (GamepadButtonFlags button in Enum.GetValues(typeof(GamepadButtonFlags)))
        {
            _buttonStates[button] = false;
        }
    }

    public bool IsConnected()
    {
        return _controller.IsConnected;
    }

    public void StartMonitoring(int pollIntervalMs = 50)
    {
        if (_isMonitoring) return;

        _isMonitoring = true;
        _monitorThread = new Thread(() => MonitorLoop(pollIntervalMs));
        _monitorThread.IsBackground = true;
        _monitorThread.Start();
    }

    public void StopMonitoring()
    {
        _isMonitoring = false;
        _monitorThread?.Join(1000);
    }

    private void MonitorLoop(int pollIntervalMs)
    {
        while (_isMonitoring)
        {
            if (_controller.IsConnected)
            {
                UpdateControllerState();
            }
            else
            {
                // 手柄断开连接，重置所有按钮状态
                ResetAllButtons();
            }

            Thread.Sleep(pollIntervalMs);
        }
    }

    private void UpdateControllerState()
    {
        try
        {
            _controller.GetState(out State state);
            var gamepad = state.Gamepad;

            // 检查按钮状态变化
            CheckButtonStates(gamepad.Buttons);

            // 检查扳机键和摇杆
            CheckTriggersAndThumbsticks(gamepad);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"获取手柄状态时出错: {ex.Message}");
        }
    }

    private void CheckButtonStates(GamepadButtonFlags currentButtons)
    {
        foreach (GamepadButtonFlags button in Enum.GetValues(typeof(GamepadButtonFlags)))
        {
            bool isPressed = currentButtons.HasFlag(button);
            bool wasPressed = _buttonStates[button];

            if (isPressed != wasPressed)
            {
                _buttonStates[button] = isPressed;
                OnButtonStateChanged(button, isPressed);
            }
        }
    }

    private void CheckTriggersAndThumbsticks(Gamepad gamepad)
    {
        // 检查左扳机键
        CheckTriggerState("LeftTrigger", gamepad.LeftTrigger);

        // 检查右扳机键
        CheckTriggerState("RightTrigger", gamepad.RightTrigger);

        // 检查左摇杆
        CheckThumbstickState("LeftThumbstick", gamepad.LeftThumbX, gamepad.LeftThumbY);

        // 检查右摇杆
        CheckThumbstickState("RightThumbstick", gamepad.RightThumbX, gamepad.RightThumbY);
    }

    private void CheckTriggerState(string triggerName, byte triggerValue)
    {
        bool isPressed = triggerValue > 30; // 阈值，可根据需要调整
        bool wasPressed = _buttonStates.ContainsKey(GamepadButtonFlags.None) &&
                         _buttonStates[GamepadButtonFlags.None]; // 这里简化处理

        if (isPressed != wasPressed)
        {
            OnButtonStateChanged(triggerName, isPressed, triggerValue);
        }
    }

    private void CheckThumbstickState(string thumbstickName, short x, short y)
    {
        // 检查摇杆是否被推动（超过死区）
        bool isMoved = Math.Abs(x) > 8000 || Math.Abs(y) > 8000; // 死区值，可根据需要调整

        if (isMoved)
        {
            OnThumbstickStateChanged(thumbstickName, x, y);
        }
    }

    private void ResetAllButtons()
    {
        foreach (var button in _buttonStates.Keys.ToList())
        {
            if (_buttonStates[button])
            {
                _buttonStates[button] = false;
                OnButtonStateChanged(button, false);
            }
        }
    }

    protected virtual void OnButtonStateChanged(GamepadButtonFlags button, bool isPressed)
    {
        ButtonStateChanged?.Invoke(this, new ButtonStateChangedEventArgs
        {
            Button = button.ToString(),
            IsPressed = isPressed,
            Timestamp = DateTime.Now
        });
    }

    protected virtual void OnButtonStateChanged(string buttonName, bool isPressed, byte value = 0)
    {
        ButtonStateChanged?.Invoke(this, new ButtonStateChangedEventArgs
        {
            Button = buttonName,
            IsPressed = isPressed,
            Value = value,
            Timestamp = DateTime.Now
        });
    }

    protected virtual void OnThumbstickStateChanged(string thumbstickName, short x, short y)
    {
        ButtonStateChanged?.Invoke(this, new ButtonStateChangedEventArgs
        {
            Button = thumbstickName,
            XValue = x,
            YValue = y,
            Timestamp = DateTime.Now
        });
    }

    public Dictionary<GamepadButtonFlags, bool> GetCurrentButtonStates()
    {
        return new Dictionary<GamepadButtonFlags, bool>(_buttonStates);
    }
}

public class ButtonStateChangedEventArgs : EventArgs
{
    public string Button { get; set; }
    public bool IsPressed { get; set; }
    public byte Value { get; set; }
    public short XValue { get; set; }
    public short YValue { get; set; }
    public DateTime Timestamp { get; set; }
}