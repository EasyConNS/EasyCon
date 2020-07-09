using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using PTDevice;
using PokemonTycoon.Graphic;
using System.IO;
using System.ComponentModel;
using Microsoft.WindowsAPICodePack.Taskbar;
using System.Media;

namespace PokemonTycoon.Scripts
{
    public abstract class ScriptCore
    {
        protected const string ScreenShotPath = @"Screenshot\";
        protected const double DefaultColorCap = 0.98;
        protected const double DefaultImageCap = 0.9;
        protected const int DefaultPressDuration = 50;
        protected const int DefailtLoopInterval = 100;

        private ScriptCore _host { get; set; }
        protected IScriptOutput _output { private get; set; }
        protected IScriptOutput Output { get => _host?.Output ?? _output; }
        public bool LogEnabled { get => Output.LogEnabled; set => Output.LogEnabled = value; }
        protected NintendoSwitch NS => _host?.NS ?? _output.NS;
        public DateTime StartTime { get; set; }

        public static class Keys
        {
            [Description("A")]
            public static readonly NintendoSwitch.Key A = NintendoSwitch.Key.Button(NintendoSwitch.Button.A);
            [Description("B")]
            public static readonly NintendoSwitch.Key B = NintendoSwitch.Key.Button(NintendoSwitch.Button.B);
            [Description("X")]
            public static readonly NintendoSwitch.Key X = NintendoSwitch.Key.Button(NintendoSwitch.Button.X);
            [Description("Y")]
            public static readonly NintendoSwitch.Key Y = NintendoSwitch.Key.Button(NintendoSwitch.Button.Y);
            [Description("L")]
            public static readonly NintendoSwitch.Key L = NintendoSwitch.Key.Button(NintendoSwitch.Button.L);
            [Description("R")]
            public static readonly NintendoSwitch.Key R = NintendoSwitch.Key.Button(NintendoSwitch.Button.R);
            [Description("ZL")]
            public static readonly NintendoSwitch.Key ZL = NintendoSwitch.Key.Button(NintendoSwitch.Button.ZL);
            [Description("ZR")]
            public static readonly NintendoSwitch.Key ZR = NintendoSwitch.Key.Button(NintendoSwitch.Button.ZR);
            [Description("MINUS")]
            public static readonly NintendoSwitch.Key MINUS = NintendoSwitch.Key.Button(NintendoSwitch.Button.MINUS);
            [Description("PLUS")]
            public static readonly NintendoSwitch.Key PLUS = NintendoSwitch.Key.Button(NintendoSwitch.Button.PLUS);
            [Description("LCLICK")]
            public static readonly NintendoSwitch.Key LCLICK = NintendoSwitch.Key.Button(NintendoSwitch.Button.LCLICK);
            [Description("RCLICK")]
            public static readonly NintendoSwitch.Key RCLICK = NintendoSwitch.Key.Button(NintendoSwitch.Button.RCLICK);
            [Description("HOME")]
            public static readonly NintendoSwitch.Key HOME = NintendoSwitch.Key.Button(NintendoSwitch.Button.HOME);
            [Description("CAPTURE")]
            public static readonly NintendoSwitch.Key CAPTURE = NintendoSwitch.Key.Button(NintendoSwitch.Button.CAPTURE);

            public static class LStick
            {
                public static readonly NintendoSwitch.Key Up = NintendoSwitch.Key.LStick(NintendoSwitch.DirectionKey.Up);
                public static readonly NintendoSwitch.Key Down = NintendoSwitch.Key.LStick(NintendoSwitch.DirectionKey.Down);
                public static readonly NintendoSwitch.Key Left = NintendoSwitch.Key.LStick(NintendoSwitch.DirectionKey.Left);
                public static readonly NintendoSwitch.Key Right = NintendoSwitch.Key.LStick(NintendoSwitch.DirectionKey.Right);
            }

            public static class RStick
            {
                public static readonly NintendoSwitch.Key Up = NintendoSwitch.Key.RStick(NintendoSwitch.DirectionKey.Up);
                public static readonly NintendoSwitch.Key Down = NintendoSwitch.Key.RStick(NintendoSwitch.DirectionKey.Down);
                public static readonly NintendoSwitch.Key Left = NintendoSwitch.Key.RStick(NintendoSwitch.DirectionKey.Left);
                public static readonly NintendoSwitch.Key Right = NintendoSwitch.Key.RStick(NintendoSwitch.DirectionKey.Right);
            }

            public static class HAT
            {
                public static readonly NintendoSwitch.Key Up = NintendoSwitch.Key.HAT(NintendoSwitch.DirectionKey.Up);
                public static readonly NintendoSwitch.Key Down = NintendoSwitch.Key.HAT(NintendoSwitch.DirectionKey.Down);
                public static readonly NintendoSwitch.Key Left = NintendoSwitch.Key.HAT(NintendoSwitch.DirectionKey.Left);
                public static readonly NintendoSwitch.Key Right = NintendoSwitch.Key.HAT(NintendoSwitch.DirectionKey.Right);
            }
        }

        public static class Colors
        {
            public static readonly Color Default = Color.White;
            public static readonly Color Start = Color.FromArgb(255, 240, 130);
            public static readonly Color End = Color.Cyan;
            public static readonly Color Success = Color.PaleGreen;
            public static readonly Color Fail = Color.LightSalmon;
            public static readonly Color Highlight = Color.FromArgb(255, 128, 255);
            public static readonly Color Trivial = Color.DarkGray;
            public static readonly Color Error = Color.OrangeRed;
        }

        public static class Taskbar
        {
            public static readonly TaskbarProgressBarState None = TaskbarProgressBarState.NoProgress;
            public static readonly TaskbarProgressBarState Running = TaskbarProgressBarState.Indeterminate;
            public static readonly TaskbarProgressBarState Progress = TaskbarProgressBarState.Normal;
            public static readonly TaskbarProgressBarState Highlight = TaskbarProgressBarState.Paused;
            public static readonly TaskbarProgressBarState Error = TaskbarProgressBarState.Error;
        }

        public static class Sounds
        {
            public static readonly SystemSound Beep = SystemSounds.Beep;
            public static readonly SystemSound Error = SystemSounds.Hand;
        }

        Dictionary<Type, ScriptCore> _modules;

        public T Module<T>()
            where T : ScriptCore
        {
            if (this is T)
                return this as T;
            if (_host != null)
                return _host.Module<T>();
            if (_modules == null)
                _modules = new Dictionary<Type, ScriptCore>();
            if (!_modules.ContainsKey(typeof(T)))
            {
                T mod = Activator.CreateInstance(typeof(T), true) as T;
                mod._host = this;
                _modules[typeof(T)] = mod;
            }
            return _modules[typeof(T)] as T;
        }

        protected void SetOutput(IScriptOutput output)
        {
            _output = output;
        }

        internal void ScreenShot(string reason)
        {
            Directory.CreateDirectory(ScreenShotPath);
            var name = $"{ScreenShotPath}{DateTime.Now.ToString("MM_dd HH_mm_ss")} {reason}.png";
            VideoCapture.ScreenShot().Save(name);
        }

        public void Log(object message)
        {
            Output.Log(message);
        }

        internal void Log(string message)
        {
            Output.Log(message);
        }

        internal void Log<T>(IEnumerable<T> list)
        {
            Log(string.Join(",", list));
        }

        internal void Log(params object[] args)
        {
            Log(string.Join("", args));
        }

        public void PushMessage(object message = null, Color? color = null)
        {
            Output.PushMessage(message, color);
        }

        internal void Msg(object message)
        {
            PushMessage(message);
            PushMessage();
            Log(message);
        }

        internal void Msg(params object[] args)
        {
            Color? color = null;
            foreach (var obj in args)
            {
                if (obj is Color)
                    color = obj as Color?;
                else
                    PushMessage(obj, color);
            }
            PushMessage();
            Log(string.Join("", args.OfType<string>()));
        }

        public void Light(Color color)
        {
            Output.Light(color);
        }

        internal void Light(bool b)
        {
            if (b)
                Light(Color.Lime);
            else
                Light(Color.Red);
        }

        public void Light(TaskbarProgressBarState color)
        {
            Output.Light(color);
        }

        public void Beep(SystemSound sound)
        {
            sound.Play();
        }

        protected Func<bool> Timeout(int sec, Func<bool> cond = null, Action action = null)
        {
            var time = DateTime.MinValue;
            return () =>
            {
                if (cond?.Invoke() == false)
                    return false;
                if (time == DateTime.MinValue)
                    time = DateTime.Now.AddSeconds(sec);
                if (DateTime.Now >= time)
                {
                    action?.Invoke();
                    return true;
                }
                return false;
            };
        }

        protected Func<bool> TimeoutThrow(int sec, Func<bool> cond = null)
        {
            var time = DateTime.MinValue;
            return () =>
            {
                if (time == DateTime.MinValue)
                    time = DateTime.Now.AddSeconds(sec);
                if (cond?.Invoke() == false)
                    return false;
                if (DateTime.Now >= time)
                    throw new ScriptException("等待超时");
                return false;
            };
        }

        Func<bool> _LoopCheck(IEnumerable<Func<bool>> funcs, IEnumerable<Action<double>> actions, int interval = DefailtLoopInterval)
        {
            try
            {
                LogEnabled = false;
                var starttime = DateTime.Now;
                while (true)
                {
                    if (funcs != null)
                    {
                        foreach (var func in funcs)
                            if (func.Invoke())
                                return func;
                    }
                    var time = (DateTime.Now - starttime).TotalMilliseconds;
                    if (actions != null)
                    {
                        foreach (var action in actions)
                            action.Invoke(time);
                    }
                    Thread.Sleep(interval);
                }
            }
            finally
            {
                LogEnabled = true;
            }
        }

        protected void Wait(int milli)
        {
            Log($"Wait({milli})");
            Thread.Sleep(milli);
        }

        protected Func<bool> WaitUntil(params Func<bool>[] args)
        {
            Log($"WaitUntil(,{string.Join(",", args.Select(f => f.GetName()))})");
            return _LoopCheck(args, null);
        }

        protected void Reset()
        {
            Log($"Reset()");
            NS.Reset();
        }

        protected void Press(NintendoSwitch.Key key, int duration, int wait)
        {
            Log($"Press({key.Name},{duration})");
            if (duration < 0)
                NS.Down(key);
            else
                NS.Press(key, duration);
            if (wait > 0)
                Thread.Sleep(wait);
        }

        protected void Press(NintendoSwitch.Key key, int duration = DefaultPressDuration)
        {
            Press(key, duration, duration);
        }

        class KeyPressAction
        {
            public NintendoSwitch.Key Key { get; private set; }
            public int KeyInterval { get; private set; }
            public int KeyNext { get; private set; }
            public Action<double>[] Actions;

            public KeyPressAction(ScriptCore script, NintendoSwitch.Key key, int interval, bool skipfirst = false)
            {
                Key = key;
                KeyInterval = interval + DefaultPressDuration;
                KeyNext = skipfirst ? KeyInterval : 0;
                Action<double> action = time =>
                {
                    if (time >= KeyNext)
                    {
                        script.NS.Press(key, DefaultPressDuration);
                        KeyNext += KeyInterval;
                    }
                };
                Actions = new Action<double>[] { action };
            }
        }

        protected Func<bool> PressUntil(NintendoSwitch.Key key, int interval, params Func<bool>[] args)
        {
            Log($"PressUntil({key.Name},{interval},{string.Join(",", args.Select(f => f.GetName()))})");
            return _LoopCheck(args, new KeyPressAction(this, key, interval).Actions, Math.Min(interval + DefaultPressDuration, DefailtLoopInterval));
        }

        protected Func<bool> WaitUntil(NintendoSwitch.Key key, int timeoutSec, params Func<bool>[] args)
        {
            Log($"WaitUntil({key.Name},{timeoutSec},{string.Join(",", args.Select(f => f.GetName()))})");
            return _LoopCheck(args, new KeyPressAction(this, key, timeoutSec * 1000, true).Actions);
        }

        protected Func<bool> WaitUntil(int timeoutSec, params Func<bool>[] args)
        {
            Log($"WaitUntil({timeoutSec},{string.Join(",", args.Select(f => f.GetName()))})");
            var timeout = DateTime.Now + TimeSpan.FromSeconds(timeoutSec);
            Func<bool> func = () => DateTime.Now > timeout;
            args = args.Append(func).ToArray();
            var r = _LoopCheck(args, null);
            return r == func ? null : r;
        }

        protected void Capture()
        {
            Log($"Capture()");
            Press(Keys.CAPTURE, 1000);
        }
    }

    public interface IScriptOutput
    {
        NintendoSwitch NS { get; }
        bool LogEnabled { get; set; }
        void Log(object message);
        void PushMessage(object message = null, Color? color = null);
        void Light(Color color);
        void Light(TaskbarProgressBarState color);
    }
}
