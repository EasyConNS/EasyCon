using ECDevice.Connection;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace ECDevice
{
    public partial class NintendoSwitch
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

        public class Key
        {
            const int HatMask = 0b00010000;

            public enum StickKeyCode
            {
                LS = 32,
                RS = 33
            }

            public readonly string Name;
            public readonly int KeyCode;
            public readonly Action<SwitchReport> Down;
            public readonly Action<SwitchReport> Up;
            public readonly int StickX;
            public readonly int StickY;

            Key(string name, int keyCode, Action<SwitchReport> down, Action<SwitchReport> up, int x = -1, int y = -1)
            {
                Name = name;
                KeyCode = keyCode;
                Down = down;
                Up = up;
                StickX = x;
                StickY = y;
            }

            static Dictionary<SwitchButton, int> _buttonKeyCodes = new();

            static Key()
            {
                foreach (SwitchButton b in Enum.GetValues(typeof(SwitchButton)))
                {
                    int n = (int)b;
                    int k = -1;
                    while (n != 0)
                    {
                        n >>= 1;
                        k++;
                    }
                    _buttonKeyCodes[b] = k;
                }
            }

            public static Key Button(SwitchButton button)
            {
                return new Key(button.GetName(),
                    _buttonKeyCodes[button],
                    r => r.Button |= (ushort)button,
                    r => r.Button &= (ushort)~button
                );
            }

            public static implicit operator Key(SwitchButton button)
            {
                return Button(button);
            }

            public static Key HAT(SwitchHAT hat)
            {
                var name = "HAT." + hat.GetName();
                return new Key(name,
                    (int)hat | HatMask,
                    r => r.HAT = (byte)hat,
                    r => r.HAT = (byte)SwitchHAT.CENTER
                );
            }

            public static implicit operator Key(SwitchHAT hat)
            {
                return HAT(hat);
            }

            public static Key HAT(DirectionKey dkey)
            {
                return HAT(GetHATFromDirection(dkey));
            }

            public static Key LStick(byte x, byte y)
            {
                return new Key($"LStick({x},{y})",
                    (int)StickKeyCode.LS,
                    r => { r.LX = x; r.LY = y; },
                    r => { r.LX = NSwitchUtil.STICK_CENTER; r.LY = NSwitchUtil.STICK_CENTER; },
                    x,
                    y
                );
            }

            public static Key RStick(byte x, byte y)
            {
                return new Key($"RStick({x},{y})",
                    (int)StickKeyCode.RS,
                    r => { r.RX = x; r.RY = y; },
                    r => { r.RX = NSwitchUtil.STICK_CENTER; r.RY = NSwitchUtil.STICK_CENTER; },
                    x,
                    y
                );
            }

            public static Key LStick(DirectionKey dkey, bool slow = false)
            {
                GetXYFromDirection(dkey, out byte x, out byte y, slow);
                return LStick(x, y);
            }

            public static Key RStick(DirectionKey dkey, bool slow = false)
            {
                GetXYFromDirection(dkey, out byte x, out byte y, slow);
                return RStick(x, y);
            }

            public static Key LStick(double degree)
            {
                GetXYFromDegree(degree, out byte x, out byte y);
                return LStick(x, y);
            }

            public static Key RStick(double degree)
            {
                GetXYFromDegree(degree, out byte x, out byte y);
                return RStick(x, y);
            }

            public static Key FromKeyCode(int keyCode)
            {
                if (keyCode < HatMask)
                    return Button((SwitchButton)(1 << keyCode));
                else if (keyCode == (int)StickKeyCode.LS)
                    return LStick(NSwitchUtil.STICK_CENTER, NSwitchUtil.STICK_CENTER);
                else if (keyCode == (int)StickKeyCode.RS)
                    return RStick(NSwitchUtil.STICK_CENTER, NSwitchUtil.STICK_CENTER);
                else
                    return HAT((SwitchHAT)(keyCode ^ HatMask));
            }

            public static implicit operator Key(int keyCode)
            {
                return FromKeyCode(keyCode);
            }
        }

        private IConnClient clientCon { get; set; }
        SwitchReport _report = new();
        Dictionary<int, KeyStroke> _keystrokes = new();
        bool _reset = false;

        CancellationTokenSource source = new();
        EventWaitHandle _ewh = new(false, EventResetMode.ManualReset);

        DateTime _nextSendTime = DateTime.MinValue;
        DirectionKey _leftStick = 0;
        DirectionKey _rightStick = 0;
        DirectionKey _hat = 0;
        bool need_cpu_opt = true;

        public delegate void LogHandler(string s);
        public event LogHandler Log;
        public event IConnClient.BytesTransferedHandler BytesSent;
        public event IConnClient.BytesTransferedHandler BytesReceived;

        private OperationRecords operationRecords = new();
        public RecordState recordState = RecordState.RECORD_STOP;

        public ConnectResult TryConnect(string connStr, bool sayhello)
        {
            if (connStr == "")
                return ConnectResult.InvalidArgument;

            var ewh = new EventWaitHandle(false, EventResetMode.AutoReset);
            var result = ConnectResult.None;
            void statuschanged(Status status)
            {
                lock (ewh)
                {
                    if (result != ConnectResult.None)
                        return;
                    if (status == Status.Connected || status == Status.ConnectedUnsafe)
                    {
                        result = ConnectResult.Success;
                        ewh.Set();
                    }
                    if (status == Status.Error)
                    {
                        result = ConnectResult.Error;
                        ewh.Set();
                    }
                }
            }

            Disconnect();
            clientCon = new TTLSerialClient(connStr);
            clientCon.BytesSent += (port, bytes) => BytesSent?.Invoke(port, bytes);
            clientCon.BytesReceived += (port, bytes) => BytesReceived?.Invoke(port, bytes);
            clientCon.CPUOpt = need_cpu_opt;
            
            clientCon.StatusChanged += statuschanged;
            clientCon.Connect(sayhello);
            if (!ewh.WaitOne(300) && sayhello)
            {
                clientCon.Disconnect();
                clientCon = null;
                return ConnectResult.Timeout;
            }
            if (result != ConnectResult.Success)
            {
                clientCon.Disconnect();
                clientCon = null;
                return result;
            }
            clientCon.StatusChanged -= statuschanged;

            source = new CancellationTokenSource();
            Task.Run(() =>
            {
                Loop();
            },
            source.Token);

            return ConnectResult.Success;
        }

        public void Disconnect()
        {
            clientCon?.Disconnect();
            clientCon = null;
            if (source != null)
            {
                source.Cancel();
            }
        }

        public bool IsConnected()
        {
            return clientCon?.CurrentStatus == Status.Connected || clientCon?.CurrentStatus == Status.ConnectedUnsafe;
        }

        public Status ConnectionStatus => clientCon?.CurrentStatus ?? Status.Connecting;

        public SwitchReport GetReport()
        {
            return _report.Clone() as SwitchReport;
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

        public void Down(Key key)
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

        public void Up(Key key)
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

        public void Press(Key key, int duration)
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
            Direction(dkey, down, ref _leftStick, Key.LStick);
        }

        public void RightDirection(DirectionKey dkey, bool down)
        {
            Direction(dkey, down, ref _rightStick, Key.RStick);
        }

        public void HatDirection(DirectionKey dkey, bool down)
        {
            if (down)
            {
                _hat |= dkey;
                GetHATFromDirection(_hat);
                Down(Key.HAT(_hat));
            }
            else
            {
                _hat &= ~dkey;
                if (GetHATFromDirection(_hat) == SwitchHAT.CENTER)
                {
                    Debug.WriteLine("_hat " + _hat.GetName());
                    Debug.WriteLine("dkey " + dkey.GetName());
                    Up(Key.HAT(dkey));
                }
                else
                {
                    Debug.WriteLine("_hat " + _hat.GetName());
                    Debug.WriteLine("dkey " + dkey.GetName());
                    Down(Key.HAT(_hat));
                }
            }
        }

        public bool StartRecord()
        {
            recordState = RecordState.RECORD_START;
            operationRecords.Clear();
            return true;
        }

        public bool StopRecord()
        {
            recordState = RecordState.RECORD_STOP;
            return true;
        }

        public bool PauseRecord()
        {
            recordState = RecordState.RECORD_PAUSE;
            return true;
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
