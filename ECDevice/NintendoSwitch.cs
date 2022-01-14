﻿using ECDevice.Connection;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ECDevice
{
    public partial class NintendoSwitch
    {
        const int MINIMAL_INTERVAL = 30;

        public const byte STICK_MIN = 0;
        public const byte STICK_CENMIN = 64;
        public const byte STICK_CENTER = 128;
        public const byte STICK_CENMAX = 192;
        public const byte STICK_MAX = 255;

        [Flags]
        public enum Button
        {
            Y = 0x01,
            B = 0x02,
            A = 0x04,
            X = 0x08,
            L = 0x10,
            R = 0x20,
            ZL = 0x40,
            ZR = 0x80,
            MINUS = 0x100,
            PLUS = 0x200,
            LCLICK = 0x400,
            RCLICK = 0x800,
            HOME = 0x1000,
            CAPTURE = 0x2000,
        }

        public enum HAT
        {
            TOP = 0x00,
            TOP_RIGHT = 0x01,
            RIGHT = 0x02,
            BOTTOM_RIGHT = 0x03,
            BOTTOM = 0x04,
            BOTTOM_LEFT = 0x05,
            LEFT = 0x06,
            TOP_LEFT = 0x07,
            CENTER = 0x08,
        }

        [Flags]
        public enum DirectionKey
        {
            None = 0x0,
            Up = 0x1,
            Down = 0x2,
            Left = 0x4,
            Right = 0x8,
        }

        public enum ConnectResult
        {
            None,
            Success,
            InvalidArgument,
            Timeout,
            Error,
        }

        public class Report : ICloneable
        {
            public ushort Button;
            public byte HAT;
            public byte LX;
            public byte LY;
            public byte RX;
            public byte RY;

            public Report()
            {
                Reset();
            }

            public void Reset()
            {
                Button = 0;
                HAT = (byte)NintendoSwitch.HAT.CENTER;
                LX = STICK_CENTER;
                LY = STICK_CENTER;
                RX = STICK_CENTER;
                RY = STICK_CENTER;
            }

            public byte[] GetBytes(bool raw = false)
            {
                // Protocal packet structure:
                // bit 7 (highest):    0 = data byte, 1 = end flag
                // bit 6~0:            data (Big-Endian)
                // serialize data
                var serialized = new List<byte>();
                serialized.AddRange(BitConverter.GetBytes(Button).Reverse());
                serialized.Add(HAT);
                serialized.Add(LX);
                serialized.Add(LY);
                serialized.Add(RX);
                serialized.Add(RY);
                if (raw)
                    return serialized.ToArray();

                // generate packet
                var packet = new List<byte>();
                long n = 0;
                int bits = 0;
                foreach (var b in serialized)
                {
                    n = (n << 8) | b;
                    bits += 8;
                    while (bits >= 7)
                    {
                        bits -= 7;
                        packet.Add((byte)(n >> bits));
                        n &= (1 << bits) - 1;
                    }
                }
                packet[packet.Count - 1] |= 0x80;
                return packet.ToArray();
            }

            public object Clone()
            {
                return new Report
                {
                    Button = Button,
                    HAT = HAT,
                    LX = LX,
                    LY = LY,
                    RX = RX,
                    RY = RY,
                };
            }

            public string GetKeyStr()
            {
                var list = new List<string>();
                foreach (Button button in Enum.GetValues(typeof(Button)))
                    if ((Button & (ushort)button) != 0)
                        list.Add(button.GetName());
                if (HAT != (byte)NintendoSwitch.HAT.CENTER)
                    list.Add($"HAT.{((NintendoSwitch.HAT)HAT).GetName()}");
                if (LX != STICK_CENTER || LY != STICK_CENTER)
                    list.Add($"LS({LX},{LY})");
                if (RX != STICK_CENTER || RY != STICK_CENTER)
                    list.Add($"RS({RX},{RY})");
                return string.Join(" ", list);
            }
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
            public readonly Action<Report> Down;
            public readonly Action<Report> Up;
            public readonly int StickX;
            public readonly int StickY;

            Key(string name, int keyCode, Action<Report> down, Action<Report> up, int x = -1, int y = -1)
            {
                Name = name;
                KeyCode = keyCode;
                Down = down;
                Up = up;
                StickX = x;
                StickY = y;
            }

            static Dictionary<Button, int> _buttonKeyCodes = new();

            static Key()
            {
                foreach (Button b in Enum.GetValues(typeof(Button)))
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

            public static Key Button(Button button)
            {
                return new Key(button.GetName(),
                    _buttonKeyCodes[button],
                    r => r.Button |= (ushort)button,
                    r => r.Button &= (ushort)~button
                );
            }

            public static implicit operator Key(Button button)
            {
                return Button(button);
            }

            public static Key HAT(HAT hat)
            {
                var name = "HAT." + hat.GetName();
                return new Key(name,
                    (int)hat | HatMask,
                    r => r.HAT = (byte)hat,
                    r => r.HAT = (byte)NintendoSwitch.HAT.CENTER
                );
            }

            public static implicit operator Key(HAT hat)
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
                    r => { r.LX = STICK_CENTER; r.LY = STICK_CENTER; },
                    x,
                    y
                );
            }

            public static Key RStick(byte x, byte y)
            {
                return new Key($"RStick({x},{y})",
                    (int)StickKeyCode.RS,
                    r => { r.RX = x; r.RY = y; },
                    r => { r.RX = STICK_CENTER; r.RY = STICK_CENTER; },
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
                    return Button((Button)(1 << keyCode));
                else if (keyCode == (int)StickKeyCode.LS)
                    return LStick(STICK_CENTER, STICK_CENTER);
                else if (keyCode == (int)StickKeyCode.RS)
                    return RStick(STICK_CENTER, STICK_CENTER);
                else
                    return HAT((HAT)(keyCode ^ HatMask));
            }

            public static implicit operator Key(int keyCode)
            {
                return FromKeyCode(keyCode);
            }
        }

        private IConnClient clientCon { get; set; }
        Report _report = new();
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

        public Report GetReport()
        {
            return _report.Clone() as Report;
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
                if (GetHATFromDirection(_hat) == HAT.CENTER)
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