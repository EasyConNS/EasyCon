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

    readonly SwitchReport _report = new();
    readonly Dictionary<int, KeyStroke> _keystrokes = new();

    bool _reset = false;
    CancellationTokenSource source = new();

    DirectionKey _leftStick = 0;
    DirectionKey _rightStick = 0;
    DirectionKey _hat = 0;
    bool need_cpu_opt = true;
    bool need_open_delay = false;

    public event LogHandler Log;
    public event BytesTransferedHandler BytesSent;
    public event BytesTransferedHandler BytesReceived;
    public event StatusChangedHandler StatusChanged;

    private readonly OperationRecords operationRecords = new();
    public RecordState recordState = RecordState.RECORD_STOP;

    public ConnectResult TryConnect(string constr)
    {
        var result = _TryConnect(constr);

        if (result == ConnectResult.Success)
        {
            source = new();
            Task.Run(() =>
            {
                Loop(source.Token);
            },
            source.Token);
        }
        else
        {
            Thread.Sleep(100);
            result = _TryConnect(constr,9600);
            if (result == ConnectResult.Success)
            {
                source = new();
                Task.Run(() =>
                {
                    Loop(source.Token);
                },
                source.Token);
            }
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
            _reset = true;
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
        System.Diagnostics.Debug.WriteLine($"{str} {key?.Name ?? ""}");
        Log($"{str} {key?.Name ?? ""}");
    }

    SwitchReport IReporter.GetReport()
    {
        return _report.Clone() as SwitchReport;
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

    public bool SetCpuOpt(bool enable)
    {
        need_cpu_opt = enable;
        return need_cpu_opt;
    }

    public bool SetOpenDelay(bool enable)
    {
        need_open_delay = enable;
        return need_open_delay;
    }
}
