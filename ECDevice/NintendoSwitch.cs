using ECDevice.Connection;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace ECDevice
{
    public interface IReporter
    {
        SwitchReport GetReport();
    }
    public delegate void LogHandler(string s);
    public partial class NintendoSwitch : IReporter
    {
        const int MINIMAL_INTERVAL = 30;

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

        DateTime _nextSendTime = DateTime.MinValue;
        DirectionKey _leftStick = 0;
        DirectionKey _rightStick = 0;
        DirectionKey _hat = 0;
        bool need_cpu_opt = true;

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
            NSKeyUtil.GetXYFromDirection(_leftStick, out byte x, out byte y);
            Down(ECKeyUtil.LStick(x, y));
        }

        public void RightDirection(DirectionKey dkey, bool down)
        {
            if (down)
                _rightStick |= dkey;
            else
                _rightStick &= ~dkey;
            NSKeyUtil.GetXYFromDirection(_rightStick, out byte x, out byte y);
            Down(ECKeyUtil.RStick(x, y));
        }

        public void HatDirection(DirectionKey dkey, bool down)
        {
            if (down)
            {
                _hat |= dkey;
                NSKeyUtil.GetHATFromDirection(_hat);
                Down(ECKeyUtil.HAT(NSKeyUtil.GetHATFromDirection(_hat)));
            }
            else
            {
                _hat &= ~dkey;
                if (NSKeyUtil.GetHATFromDirection(_hat) == SwitchHAT.CENTER)
                {
                    Debug.WriteLine("_hat " + _hat.GetName());
                    Debug.WriteLine("dkey " + dkey.GetName());
                    Up(ECKeyUtil.HAT(NSKeyUtil.GetHATFromDirection(dkey)));
                }
                else
                {
                    Debug.WriteLine("_hat " + _hat.GetName());
                    Debug.WriteLine("dkey " + dkey.GetName());
                    Down(ECKeyUtil.HAT(NSKeyUtil.GetHATFromDirection(_hat)));
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
    }
}
