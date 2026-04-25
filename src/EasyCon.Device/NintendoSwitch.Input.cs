using EasyDevice.Connection;

namespace EasyDevice;

public interface IReporter
{
    SwitchReport GetReport();
}

public delegate void LogHandler(string s);
public partial class NintendoSwitch : IReporter, IDisposable
{
    public enum ConnectResult
    {
        None,
        Success,
        InvalidArgument,
        Timeout,
        Error,
    }

    private readonly object _lock = new();
    readonly Dictionary<int, KeyStroke> _keystrokes = new();

    bool _reset = false;
    CancellationTokenSource source = new();
    bool _disposed;

    DirectionKey _leftStick = 0;
    DirectionKey _rightStick = 0;
    DirectionKey _hat = 0;

    public event LogHandler Log;
    public event BytesTransferedHandler BytesSent;
    public event BytesTransferedHandler BytesReceived;
    public event StatusChangedHandler StatusChanged;

    private readonly OperationRecords operationRecords = new();
    public RecordState RecordState { get; set; } = RecordState.RECORD_STOP;

    public ConnectResult TryConnect(string constr)
    {
        var result = _TryConnect(constr);

        if (result == ConnectResult.Success)
        {
            StartLoop();
        }
        else
        {
            Thread.Sleep(100);
            result = _TryConnect(constr, 9600);
            if (result == ConnectResult.Success)
            {
                StartLoop();
            }
        }

        return result;
    }

    private void StartLoop()
    {
        source = new CancellationTokenSource();
        Task.Run(() => Loop(source.Token), source.Token);
    }

    public void Reset()
    {
        lock (_lock)
        {
            DebugKey("Reset");
            Signal();
            _keystrokes.Clear();
            _reset = true;
        }
    }

    public void Down(ECKey key)
    {
        lock (_lock)
        {
            DebugKey("Down", key);
            Signal();
            _keystrokes[key.KeyCode] = new KeyStroke(key);
            if (RecordState == RecordState.RECORD_START)
            {
                operationRecords.AddRecord(_keystrokes[key.KeyCode]);
            }
        }
    }

    public void Up(ECKey key)
    {
        lock (_lock)
        {
            DebugKey("Up", key);
            Signal();
            _keystrokes[key.KeyCode] = new KeyStroke(key, true);
            if (RecordState == RecordState.RECORD_START)
            {
                operationRecords.AddRecord(_keystrokes[key.KeyCode]);
            }
        }
    }
    public void Press(ECKey key, int duration)
    {
        lock (this)
        {
            DebugKey("Press", key);
            Signal();
            _keystrokes[key.KeyCode] = new KeyStroke(key, false, duration);
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
        return _report.Clone() as SwitchReport;
    }

    public void StartRecord()
    {
        RecordState = RecordState.RECORD_START;
        operationRecords.Clear();
    }

    public void StopRecord()
    {
        RecordState = RecordState.RECORD_STOP;
    }

    public void PauseRecord()
    {
        RecordState = RecordState.RECORD_PAUSE;
    }

    public string GetRecordScript()
    {
        return operationRecords.ToScript();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Disconnect();
        _ewh?.Dispose();
    }
}