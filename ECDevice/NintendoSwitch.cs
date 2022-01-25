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
            Direction(dkey, down, ref _leftStick, ECKeyUtil.LStick);
        }

        public void RightDirection(DirectionKey dkey, bool down)
        {
            Direction(dkey, down, ref _rightStick, ECKeyUtil.RStick);
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

        public SwitchReport GetReport()
        {
            return _report.Clone() as SwitchReport;
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
