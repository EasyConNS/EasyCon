using EasyDevice.Connection;

namespace EasyDevice;

public interface IReporter
{
    SwitchReport GetReport();
}

public delegate void LogHandler(string s);
public partial class NintendoSwitch : IReporter
{
    public enum ConnectResult
    {
        None,
        Success,
        InvalidArgument,
        Timeout,
        Error,
    }

    readonly Dictionary<int, KeyStroke> _keystrokes = new();

    bool _reset = false;
    CancellationTokenSource source = new();
    private Task _writeLoop;

    /// <summary>重连前取消上一条设备写循环，避免新旧两个 Loop 并行向设备写报告。</summary>
    private void CancelStaleLoop()
    {
        try { source.Cancel(); }
        catch (ObjectDisposedException) { }
        if (_writeLoop != null && !_writeLoop.Wait(TimeSpan.FromSeconds(5)))
            throw new InvalidOperationException("busy: 上一设备写循环仍在停止中");
        source.Dispose();
    }

    DirectionKey _leftStick = 0;
    DirectionKey _rightStick = 0;
    DirectionKey _hat = 0;

    public event LogHandler Log;
    public event BytesTransferedHandler BytesSent;
    public event BytesTransferedHandler BytesReceived;
    public event StatusChangedHandler StatusChanged;

    private readonly OperationRecords operationRecords = new();
    internal RecordState recordState = RecordState.RECORD_STOP;

    public ConnectResult TryConnect(string constr)
    {
        if (string.IsNullOrWhiteSpace(constr))
            return ConnectResult.InvalidArgument;
        bool explicitPokeldn = constr.StartsWith("pokeldn:", StringComparison.OrdinalIgnoreCase)
            || constr == ECDevice.PokeldnServiceName;
        int[] baudrates = explicitPokeldn ? [PokeldnSerialClient.DefaultBaudRate] : [115200, 9600, PokeldnSerialClient.DefaultBaudRate];
        ConnectResult result = ConnectResult.None;
        foreach (int baudrate in baudrates)
        {
            result = _TryConnect(constr, baudrate, pokeldn: baudrate == PokeldnSerialClient.DefaultBaudRate);
            if (result == ConnectResult.Success)
            {
                source = new();
                CancellationToken token = source.Token;
                lock (this)
                {
                    _keystrokes.Clear();
                    _report.Reset();
                    _leftStick = _rightStick = _hat = DirectionKey.None;
                    _reset = false;
                    _ewh.Reset();
                }
                // 保留上游专用线程：Loop 会长期等待设备事件。
                _writeLoop = Task.Factory.StartNew(() => Loop(token), CancellationToken.None,
                    TaskCreationOptions.LongRunning, TaskScheduler.Default);
                Log?.Invoke($"已连接 {FirmwareName} {FirmwareVersion}");
                if (ConsoleConnected == false)
                    Log?.Invoke("手柄固件已连接，NS 尚未接管手柄；请检查 USB 连接或在 NS 上更改握法/顺序完成配对。");
                break;
            }
            if (result == ConnectResult.InvalidArgument)
                break;
        }
        return result;
    }

    public void Reset()
    {
        lock (this)
        {
            DebugKey("Reset");
            Signal();
            _keystrokes.Clear();
            _leftStick = _rightStick = _hat = 0;
            _reset = true;
            clientCon?.ClearQueue();
        }
    }

    public void Down(ECKey key)
    {
        lock (this)
        {
            DebugKey("Down", key);
            Signal();
            _keystrokes[key.KeyCode] = new KeyStroke(key);
            if (recordState == RecordState.RECORD_START)
            {
                operationRecords.AddRecord(_keystrokes[key.KeyCode]);
            }
        }
    }

    public void Up(ECKey key)
    {
        lock (this)
        {
            DebugKey("Up", key);
            Signal();
            _keystrokes[key.KeyCode] = new KeyStroke(key, true);
            if (recordState == RecordState.RECORD_START)
            {
                operationRecords.AddRecord(_keystrokes[key.KeyCode]);
            }
        }
    }

    public void LeftDirection(DirectionKey dkey, bool down)
    {
        if (down)
            _leftStick |= dkey;
        else
            _leftStick &= ~dkey;
        Down(ECKeyUtil.LStick(_leftStick));
    }

    public void RightDirection(DirectionKey dkey, bool down)
    {
        if (down)
            _rightStick |= dkey;
        else
            _rightStick &= ~dkey;
        Down(ECKeyUtil.RStick(_rightStick));
    }

    public void HatDirection(DirectionKey dkey, bool down)
    {
        if (down)
        {
            _hat |= dkey;
            Down(ECKeyUtil.HAT(_hat));
        }
        else
        {
            _hat &= ~dkey;
            if (_hat.GetHATFromDirection() == SwitchHAT.CENTER)
            {
                Up(ECKeyUtil.HAT(dkey));
            }
            else
            {
                Down(ECKeyUtil.HAT(_hat));
            }
        }
        System.Diagnostics.Debug.WriteLine("_hat " + _hat.GetName());
        System.Diagnostics.Debug.WriteLine("dkey " + dkey.GetName());
    }

    private void DebugKey(string str, ECKey key = null)
    {
        System.Diagnostics.Debug.WriteLine($"{str} {key?.Name ?? "??"}");
        Log?.Invoke($"{str} {key?.Name ?? ""}");
    }

    SwitchReport IReporter.GetReport()
    {
        lock (this) return _report.Clone() as SwitchReport;
    }

    public void StartRecord()
    {
        recordState = RecordState.RECORD_START;
        operationRecords.Clear();
    }

    public void StopRecord()
    {
        recordState = RecordState.RECORD_STOP;
    }

    public void PauseRecord()
    {
        recordState = RecordState.RECORD_PAUSE;
    }

    public string GetRecordScript()
    {
        return operationRecords.ToScript();
    }
}