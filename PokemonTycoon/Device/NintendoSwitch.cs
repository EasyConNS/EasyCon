using PokemonTycoon.Device.Arduino;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PokemonTycoon.Device
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
            public readonly string Name;
            public readonly string IndexKey;
            public readonly Action<Report> Down;
            public readonly Action<Report> Up;

            Key(string name, string indexKey, Action<Report> down, Action<Report> up)
            {
                Name = name;
                IndexKey = indexKey;
                Down = down;
                Up = up;
            }

            public static Key Button(Button button)
            {
                return new Key(button.GetName(),
                    button.GetName(),
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
                    name,
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
                    "LStick",
                    r => { r.LX = x; r.LY = y; },
                    r => { r.LX = STICK_CENTER; r.LY = STICK_CENTER; }
                );
            }

            public static Key RStick(byte x, byte y)
            {
                return new Key($"RStick({x},{y})",
                    "RStick",
                    r => { r.RX = x; r.RY = y; },
                    r => { r.RX = STICK_CENTER; r.RY = STICK_CENTER; }
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
        }

        class KeyStroke
        {
            public readonly Key Key;
            public readonly bool Up;
            public readonly int Duration;
            public readonly DateTime Time;

            public KeyStroke(Key key, bool up = false, int duration = 0, DateTime time = default(DateTime))
            {
                Key = key;
                Up = up;
                Duration = duration;
                Time = time;
            }

            public override string ToString()
            {
                return Key.IndexKey;
            }

            public static implicit operator KeyStroke(Key key)
            {
                return new KeyStroke(key);
            }

            public static implicit operator string(KeyStroke ks)
            {
                return ks.ToString();
            }
        }

        ArduinoSerial _arduino;
        Report _report = new Report();
        Dictionary<string, KeyStroke> _keystrokes = new Dictionary<string, KeyStroke>();
        bool _reset = false;
        Thread _thread;
        EventWaitHandle _ewh = new EventWaitHandle(false, EventResetMode.ManualReset);
        DateTime _nextSendTime = DateTime.MinValue;
        DirectionKey _leftStick = 0;
        DirectionKey _rightStick = 0;
        DateTime _lastActionTime = DateTime.MinValue;

        static NintendoSwitch _instance;

        NintendoSwitch()
        { }

        public static NintendoSwitch GetInstance()
        {
            if (_instance == null)
                _instance = new NintendoSwitch();
            return _instance;
        }

        public void Start(string portName)
        {
            _arduino = new ArduinoSerial(portName);
            _arduino.BytesReceived += Receive;
            _arduino.Start();
            _thread = new Thread(Loop);
            _thread.IsBackground = true;
            _thread.Start();
        }

        public Report GetReport()
        {
            return _report.Clone() as Report;
        }

        void Signal()
        {
            if (_arduino == null)
                return;
            //PrintTime();
            else
                _ewh.Set();
        }

        void Receive(byte[] bytes)
        {
            //PrintTime();
            if (bytes[0] != 8)
            {
                var str = $"[Resend] {bytes[0]}";
                Debug.WriteLine(str);
                Logger.WriteLine(str);
                Signal();
            }
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
            //Debug.WriteLine(str);
            //Logger.WriteLine(str);
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
                _keystrokes[key.IndexKey] = new KeyStroke(key);
            }
        }

        public void Up(Key key)
        {
            lock (this)
            {
                PrintKey("Up", key);
                Signal();
                _keystrokes[key.IndexKey] = new KeyStroke(key, true);
            }
        }

        public void Press(Key key, int duration)
        {
            lock (this)
            {
                PrintKey("Press", key);
                Signal();
                _keystrokes[key.IndexKey] = new KeyStroke(key, false, duration);
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
            while (true)
            {
                try
                {
                    if (DateTime.Now < _nextSendTime)
                        Thread.Sleep((int)(_nextSendTime - DateTime.Now).TotalMilliseconds);
                    int sleep = int.MaxValue;
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
                                _keystrokes.Remove(ks);
                            }
                            else
                            {
                                ks.Key.Down(_report);
                                if (ks.Duration > 0)
                                {
                                    _keystrokes[ks] = new KeyStroke(ks.Key, true, 0, DateTime.Now + TimeSpan.FromMilliseconds(ks.Duration));
                                    if (ks.Duration < sleep)
                                        sleep = ks.Duration;
                                }
                                else
                                    _keystrokes.Remove(ks);
                            }
                        }
                        string log;
                        log = $"[Send {DateTime.Now.ToString("ss.fff")}] { _report.GetKeyStr()}";
                        //Debug.WriteLine(log);
                        Logger.WriteLine(log);
                        _arduino.Write(_report.GetBytes());
                        _nextSendTime = DateTime.Now + TimeSpan.FromMilliseconds(MINIMAL_INTERVAL);
                        _ewh.Reset();
                    }
                    if (_keystrokes.Count == 0)
                        _ewh.WaitOne();
                    else
                        _ewh.WaitOne(sleep);
                }
                catch (ThreadInterruptedException)
                { }
            }
        }
    }
}
