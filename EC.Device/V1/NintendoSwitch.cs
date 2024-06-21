using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace EC.Device
{
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

        private readonly OperationRecords operationRecords = new();
        public RecordState recordState = RecordState.RECORD_STOP;

        public ConnectResult TryConnect(string constr, bool keepalive)
        {
            var result = _TryConnect(constr, keepalive);

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
                result = _TryConnect(constr, keepalive,9600);
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
                PrintKey("Reset");
                Signal();
                _keystrokes.Clear();
                _reset = true;
            }
        }

        public void Down(ECKey key)
        {
            lock (this)
            {
                PrintKey("Down", key);
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
                PrintKey("Up", key);
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
                PrintKey("Press", key);
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
                    Debug.WriteLine("_hat " + _hat.GetName());
                    Debug.WriteLine("dkey " + dkey.GetName());
                    Up(ECKeyUtil.HAT(dkey));
                }
                else
                {
                    Debug.WriteLine("_hat " + _hat.GetName());
                    Debug.WriteLine("dkey " + dkey.GetName());
                    Down(ECKeyUtil.HAT(_hat));
                }
            }
        }
        
        private static void PrintKey(string str, ECKey key = null)
        {
            str = str + " " + key?.Name ?? "";
            Debug.WriteLine(str);
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
            return operationRecords.ToScript(true);
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
}
