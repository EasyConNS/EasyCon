using PTDevice.Arduino;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PTDevice
{
    public class NintendoSwitch
    {
        const int MINIMAL_INTERVAL = 30;

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

        public const byte STICK_MIN = 0;
        public const byte STICK_CENTER = 128;
        public const byte STICK_MAX = 255;

        [Flags]
        public enum DirectionKey
        {
            None = 0x0,
            Up = 0x1,
            Down = 0x2,
            Left = 0x4,
            Right = 0x8,
            UpLeft = 0x16,
            DownLeft = 0x32,
            UpRight = 0x64,
            DownRight = 0x128,
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
                List<byte> serialized = new List<byte>();
                serialized.AddRange(BitConverter.GetBytes(Button).Reverse());
                serialized.Add(HAT);
                serialized.Add(LX);
                serialized.Add(LY);
                serialized.Add(RX);
                serialized.Add(RY);
                if (raw)
                    return serialized.ToArray();

                // generate packet
                List<byte> packet = new List<byte>();
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
                List<string> list = new List<string>();
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
                RS = 33,
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

            static Dictionary<Button, int> _buttonKeyCodes = new Dictionary<Button, int>();

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

            public static Key LStick(DirectionKey dkey)
            {
                byte x, y;
                GetXYFromDirection(dkey, out x, out y);
                return LStick(x, y);
            }

            public static Key RStick(DirectionKey dkey)
            {
                byte x, y;
                GetXYFromDirection(dkey, out x, out y);
                return RStick(x, y);
            }

            public static Key LStick(double degree)
            {
                byte x, y;
                GetXYFromDirection(degree, out x, out y);
                return LStick(x, y);
            }

            public static Key RStick(double degree)
            {
                byte x, y;
                GetXYFromDirection(degree, out x, out y);
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

        class KeyStroke
        {
            public readonly Key Key;
            public readonly bool Up;
            public readonly int Duration;
            public readonly DateTime Time;
            public int KeyCode => Key.KeyCode;

            public KeyStroke(Key key, bool up = false, int duration = 0, DateTime time = default)
            {
                Key = key;
                Up = up;
                Duration = duration;
                Time = DateTime.Now;
            }
        }
        public enum RecordState
        {
            RECORD_START = 0x00,
            RECORD_PAUSE = 0x01,
            RECORD_STOP = 0x02,
        }

        class OperationRecords
        {
            private List<KeyStroke> records = new List<KeyStroke>();
            string script = "";

            public OperationRecords()
            {
            }

            private string GetScriptKeyName(string key)
            {
                switch(key)
                {
                    case "RStick(128,0)":
                        return "RS UP";
                    case "RStick(128,128)":
                        return "RS RESET";
                    case "RStick(128,255)":
                        return "RS DOWN";
                    case "RStick(0,128)":
                        return "RS LEFT";
                    case "RStick(255,128)":
                        return "RS RIGHT";

                    case "LStick(128,0)":
                        return "LS UP";
                    case "LStick(128,128)":
                        return "LS RESET";
                    case "LStick(128,255)":
                        return "LS DOWN";
                    case "LStick(0,128)":
                        return "LS LEFT";
                    case "LStick(255,128)":
                        return "LS RIGHT";

                    case "HAT.TOP":
                        return "UP";
                    case "HAT.BOTTOM":
                        return "DOWN";
                    case "HAT.LEFT":
                        return "LEFT";
                    case "HAT.RIGHT":
                        return "RIGHT";

                    case "HAT.TOP_LEFT":
                        return "UPLEFT";
                    case "HAT.TOP_RIGHT":
                        return "UPRIGHT";
                    case "HAT.BOTTOM_LEFT":
                        return "DOWNLEFT";
                    case "HAT.BOTTOM_RIGHT":
                        return "DOWNRIGHT";

                    default:
                        return key;
                }
            }

            public void AddRecord(KeyStroke key)
            {
                string new_item = "";

                // insert the wait cmd
                if (records.Count() > 0)
                {
                    long wait_time = (key.Time.Ticks - records.Last().Time.Ticks) / 10000;
                    script += "WAIT " + wait_time+ "\r\n";
                }

                records.Add(key);
                new_item += GetScriptKeyName(key.Key.Name);
                Debug.WriteLine("keycode:"+key.KeyCode);
                if(key.KeyCode != 32 && key.KeyCode !=33)
                {
                    if (key.Up)
                    {
                        new_item += " UP";
                    }
                    else
                    {
                        new_item += " DOWN" ;
                    }
                }
                script += new_item + "\r\n";
            }

            public void Clear()
            {
                records.Clear();
                script = "";
            }
            public string ToScript(bool WithComment)
            {
                if (WithComment)
                {

                }

                return script;
            }
        }

        public ArduinoSerial Arduino { get; private set; }
        Report _report = new Report();
        Dictionary<int, KeyStroke> _keystrokes = new Dictionary<int, KeyStroke>();
        bool _reset = false;
        Thread _thread;
        EventWaitHandle _ewh = new EventWaitHandle(false, EventResetMode.ManualReset);
        DateTime _nextSendTime = DateTime.MinValue;
        DirectionKey _leftStick = 0;
        DirectionKey _rightStick = 0;
        DateTime _lastActionTime = DateTime.MinValue;

        public delegate void LogHandler(string s);
        public event LogHandler Log;
        public event ArduinoSerial.BytesTransferedHandler BytesSent;
        public event ArduinoSerial.BytesTransferedHandler BytesReceived;

        static NintendoSwitch _instance;
        OperationRecords operationRecords = new OperationRecords();
        public RecordState recordState = RecordState.RECORD_STOP;

        NintendoSwitch()
        { }

        public static NintendoSwitch GetInstance()
        {
            if (_instance == null)
                _instance = new NintendoSwitch();
            return _instance;
        }

        public void Test(int key)
        {
            if (key == 1)
            {
                //NS.TryConnect("COM5", true);
                Arduino.Write(ArduinoSerial.Command.Ready, ArduinoSerial.Command.Debug);
            }
            else if (key == 2)
            {
                List<byte> list = new List<byte>();
                for (int i = 0; i < 200; i++)
                {
                    list.Add((byte)i);
                }
                Flash(list.ToArray());
                var str = File.ReadAllText(@"F:\Users\Nukieberry\Documents\GitHub\SerialCon\Joystick.hex");
                //str = PTAssembler.Assemble(str, new byte[] { 1, 1, 2, 1, 3, 1 });
                File.WriteAllText(@"F:\Users\Nukieberry\Documents\GitHub\SerialCon\1.hex", str);
            }
            else if (key == 3)
            {
                Arduino.Write(ArduinoSerial.Command.Ready, ArduinoSerial.Command.ScriptStart);
            }
            else if (key == 4)
            {
                Arduino.Write(ArduinoSerial.Command.Ready, ArduinoSerial.Command.ScriptStop);
            }
            else if (key == 5)
            {
            }
        }

        public void Connect(string portName)
        {
            Disconnect();
            Arduino = new ArduinoSerial(portName);
            Arduino.BytesSent += (port, bytes) => BytesSent?.Invoke(port, bytes);
            Arduino.BytesReceived += (port, bytes) => BytesReceived?.Invoke(port, bytes);
            Arduino.Connect(false);
            _thread = new Thread(Loop);
            _thread.IsBackground = true;
            _thread.Start();
        }

        public enum ConnectResult
        {
            None,
            Success,
            InvalidArgument,
            Timeout,
            Error,
        }

        public ConnectResult TryConnect(string portName, bool sayhello)
        {
            if (portName == "")
                return ConnectResult.InvalidArgument;
            Disconnect();
            EventWaitHandle ewh = new EventWaitHandle(false, EventResetMode.AutoReset);
            ConnectResult result = ConnectResult.None;
            Arduino = new ArduinoSerial(portName);
            Arduino.BytesSent += (port, bytes) => BytesSent?.Invoke(port, bytes);
            Arduino.BytesReceived += (port, bytes) => BytesReceived?.Invoke(port, bytes);
            ArduinoSerial.StatusChangedHandler statuschanged = status =>
            {
                lock (ewh)
                {
                    if (result != ConnectResult.None)
                        return;
                    if (status == ArduinoSerial.Status.Connected || status == ArduinoSerial.Status.ConnectedUnsafe)
                    {
                        result = ConnectResult.Success;
                        ewh.Set();
                    }
                    if (status == ArduinoSerial.Status.Error)
                    {
                        result = ConnectResult.Error;
                        ewh.Set();
                    }
                }
            };
            Arduino.StatusChanged += statuschanged;
            Arduino.Connect(sayhello);
            if (!ewh.WaitOne(300) && sayhello)
            {
                Arduino.Disconnect();
                Arduino = null;
                return ConnectResult.Timeout;
            }
            if (result != ConnectResult.Success)
            {
                Arduino.Disconnect();
                Arduino = null;
                return result;
            }
            Arduino.StatusChanged -= statuschanged;
            _thread = new Thread(Loop);
            _thread.IsBackground = true;
            _thread.Start();
            return ConnectResult.Success;
        }

        public void Disconnect()
        {
            Arduino?.Disconnect();
            Arduino = null;
            _thread?.Abort();
            _thread?.Join(100);
            _thread = null;
        }

        public bool IsConnected()
        {
            return Arduino?.CurrentStatus == ArduinoSerial.Status.Connected || Arduino?.CurrentStatus == ArduinoSerial.Status.ConnectedUnsafe;
        }

        public ArduinoSerial.Status GetConnectionStatus()
        {
            return Arduino?.CurrentStatus ?? ArduinoSerial.Status.Connecting;
        }

        public Report GetReport()
        {
            return _report.Clone() as Report;
        }

        void Signal()
        {
            if (Arduino == null)
                return;
            //PrintTime();
            else
                _ewh.Set();
        }

        bool SendSync(Func<byte, bool> predicate, int timeout, params byte[] bytes)
        {
            EventWaitHandle ewh = new EventWaitHandle(false, EventResetMode.AutoReset);
            ArduinoSerial.BytesTransferedHandler h = (port, bytes_) =>
            {
                foreach (var b in bytes_)
                    if (predicate(b))
                    {
                        ewh.Set();
                        break;
                    }
            };
            try
            {
                Arduino.BytesReceived += h;
                Arduino.Write(bytes);
                if (!ewh.WaitOne(timeout))
                    return false;
                return true;
            }
            finally
            {
                Arduino.BytesReceived -= h;
            }
        }

        bool ResetControl()
        {
            EventWaitHandle ewh = new EventWaitHandle(false, EventResetMode.AutoReset);
            ArduinoSerial.BytesTransferedHandler h = (port, bytes_) =>
            {
                if (bytes_.Contains(ArduinoSerial.Reply.Hello))
                    ewh.Set();
            };
            try
            {
                Arduino.BytesReceived += h;
                for (int i = 0; i < 3; i++)
                    Arduino.Write(ArduinoSerial.Command.Ready, ArduinoSerial.Command.Hello);
                return ewh.WaitOne(50);
            }
            finally
            {
                Arduino.BytesReceived -= h;
            }
        }

        void Receive(string portName, byte[] bytes)
        {
            //PrintTime();
            //if (bytes[0] != ArduinoSerial.Reply.Ack)
            //{
            //    var str = $"[Resend] {bytes[0]}";
            //    Debug.WriteLine(str);
            //    Log?.Invoke(str);
            //    Signal();
            //}
        }

        void PrintTime()
        {
            if (_lastActionTime != DateTime.MinValue)
                Debug.WriteLine("Interval: " + (DateTime.Now - _lastActionTime).TotalMilliseconds);
            _lastActionTime = DateTime.Now;
        }

        void PrintKey(string str, Key key = null)
        {
            str = str + " " + key?.Name ?? "";
            Debug.WriteLine(str);
            //Log?.Invoke(str);;
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
                Debug.Write("code:" + key.KeyCode);
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

        static void GetXYFromDirection(double degree, out byte x, out byte y)
        {
            double radian = degree * Math.PI / 180;
            double dy = Math.Round((Math.Tan(radian) * Math.Sign(Math.Cos(radian))).Clamp(-1, 1), 4);
            double dx = radian == 0 ? 1 : Math.Round((1 / Math.Tan(radian) * Math.Sign(Math.Sin(radian))).Clamp(-1, 1), 4);
            x = (byte)((dx + 1) / 2 * (STICK_MAX - STICK_MIN) + STICK_MIN);
            y = (byte)((-dy + 1) / 2 * (STICK_MAX - STICK_MIN) + STICK_MIN);
        }

        static void GetXYFromDirection(DirectionKey dkey, out byte x, out byte y)
        {
            if (dkey.HasFlag(DirectionKey.Left) && !dkey.HasFlag(DirectionKey.Right))
                x = STICK_MIN;
            else if (!dkey.HasFlag(DirectionKey.Left) && dkey.HasFlag(DirectionKey.Right))
                x = STICK_MAX;
            else
                x = STICK_CENTER;
            if (dkey.HasFlag(DirectionKey.Up) && !dkey.HasFlag(DirectionKey.Down))
                y = STICK_MIN;
            else if (!dkey.HasFlag(DirectionKey.Up) && dkey.HasFlag(DirectionKey.Down))
                y = STICK_MAX;
            else
                y = STICK_CENTER;
        }

        static HAT GetHATFromDirection(DirectionKey dkey)
        {
            if (dkey.HasFlag(DirectionKey.Up) && dkey.HasFlag(DirectionKey.Down))
                dkey &= ~DirectionKey.Up & ~DirectionKey.Down;
            if (dkey.HasFlag(DirectionKey.Left) && dkey.HasFlag(DirectionKey.Right))
                dkey &= ~DirectionKey.Left & ~DirectionKey.Right;
            if (dkey == DirectionKey.Up)
                return HAT.TOP;
            if (dkey == DirectionKey.Down)
                return HAT.BOTTOM;
            if (dkey == DirectionKey.Left)
                return HAT.LEFT;
            if (dkey == DirectionKey.Right)
                return HAT.RIGHT;
            if (dkey.HasFlag(DirectionKey.Up) && dkey.HasFlag(DirectionKey.Left))
                return HAT.TOP_LEFT;
            if (dkey.HasFlag(DirectionKey.Up) && dkey.HasFlag(DirectionKey.Right))
                return HAT.TOP_RIGHT;
            if (dkey.HasFlag(DirectionKey.Down) && dkey.HasFlag(DirectionKey.Left))
                return HAT.BOTTOM_LEFT;
            if (dkey.HasFlag(DirectionKey.Down) && dkey.HasFlag(DirectionKey.Right))
                return HAT.BOTTOM_RIGHT;
            return HAT.CENTER;
        }

        public static DirectionKey GetDirectionFromHAT(HAT hat)
        {
            switch (hat)
            {
                case HAT.TOP:
                    return DirectionKey.Up;
                case HAT.TOP_RIGHT:
                    return DirectionKey.Up | DirectionKey.Right;
                case HAT.RIGHT:
                    return DirectionKey.Right;
                case HAT.BOTTOM_RIGHT:
                    return DirectionKey.Down | DirectionKey.Right;
                case HAT.BOTTOM:
                    return DirectionKey.Down;
                case HAT.BOTTOM_LEFT:
                    return DirectionKey.Down | DirectionKey.Left;
                case HAT.LEFT:
                    return DirectionKey.Left;
                case HAT.TOP_LEFT:
                    return DirectionKey.Up | DirectionKey.Left;
                default:
                    return DirectionKey.None;
            }
        }

        void Direction(DirectionKey dkey, bool down, ref DirectionKey flags, Func<byte, byte, Key> getkey)
        {
            var oldflags = flags;
            if (down)
                flags |= dkey;
            else
                flags &= ~dkey;
            byte x, y;
            GetXYFromDirection(flags, out x, out y);
            Down(getkey(x, y));
        }

        public void LeftDirection(DirectionKey dkey, bool down)
        {
            Direction(dkey, down, ref _leftStick, Key.LStick);
        }

        public void RightDirection(DirectionKey dkey, bool down)
        {
            Direction(dkey, down, ref _rightStick, Key.RStick);
        }

        void Loop()
        {
            int sleep = 0;
            while (true)
            {
                if (_keystrokes.Count == 0)
                    _ewh.WaitOne();
                else
                    _ewh.WaitOne(sleep);
                if (DateTime.Now < _nextSendTime)
                    Thread.Sleep((int)(_nextSendTime - DateTime.Now).TotalMilliseconds);
                sleep = int.MaxValue;
                //Thread.Yield();
                lock (this)
                {
                    if (_reset)
                    {
                        _report.Reset();
                        _reset = false;
                    }
                    var values = _keystrokes.Values.ToArray();
                    foreach (var ks in values)
                    {
                        if (ks.Time > DateTime.Now)
                        {
                            var n = (int)(ks.Time - DateTime.Now).TotalMilliseconds;
                            if (n > 0 && n < sleep)
                                sleep = n;
                        }
                        else if (ks.Up)
                        {
                            ks.Key.Up(_report);
                            _keystrokes.Remove(ks.KeyCode);
                        }
                        else
                        {
                            ks.Key.Down(_report);
                            if (ks.Duration > 0)
                            {
                                _keystrokes[ks.KeyCode] = new KeyStroke(ks.Key, true, 0, DateTime.Now + TimeSpan.FromMilliseconds(ks.Duration));
                                if (ks.Duration < sleep)
                                    sleep = ks.Duration;
                            }
                            else
                                _keystrokes.Remove(ks.KeyCode);
                        }
                    }
                    string log;
                    log = $"[Send {DateTime.Now.ToString("ss.fff")}] { _report.GetKeyStr()}";
                    //Debug.WriteLine(log);
                    Log?.Invoke(log);
                    Arduino.Write(_report.GetBytes());
                    _nextSendTime = DateTime.Now.AddMilliseconds(MINIMAL_INTERVAL);
                    _ewh.Reset();
                }
            }
        }

        public bool Flash(byte[] asmBytes)
        {
            const int PacketSize = 20;
            List<byte> list = new List<byte>(asmBytes);
            for (int i = 0; i < list.Count; i += PacketSize)
            {
                int len = Math.Min(PacketSize, list.Count - i);
                var packet = list.GetRange(i, len).ToArray();
                while (true)
                {
                    if (!SendSync(
                            b => b == ArduinoSerial.Reply.FlashStart,
                            100,
                            ArduinoSerial.Command.Ready,
                            (byte)(i & 0x7F),
                            (byte)(i >> 7),
                            (byte)(len & 0x7F),
                            (byte)(len >> 7),
                            ArduinoSerial.Command.Flash)
                        || !SendSync(
                            b => b == ArduinoSerial.Reply.FlashEnd,
                            100,
                            packet)
                        )
                    {
                        // error, retry
                        if (!ResetControl())
                            return false;
                        continue;
                    }
                    break;
                }
            }
            return true;
        }

        public bool RemoteStart()
        {
            return SendSync(b => b == ArduinoSerial.Reply.ScriptAck, 100, ArduinoSerial.Command.Ready, ArduinoSerial.Command.ScriptStart);
        }

        public bool RemoteStop()
        {
            return SendSync(b => b == ArduinoSerial.Reply.ScriptAck, 100, ArduinoSerial.Command.Ready, ArduinoSerial.Command.ScriptStop);
        }

        public int GetVersion()
        {
            int ver = -1;
            SendSync(b =>
            {
                if (b >= 0x40 && b <= 0x80)
                {
                    ver = b;
                    return true;
                }
                return false;
            }, 100, ArduinoSerial.Command.Ready, ArduinoSerial.Command.Version);
            return ver;
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
            if (IsConnected())
            {
                Arduino.CpuOpt = enable;
                return true;
            }
            return false;
        }
    }
}
