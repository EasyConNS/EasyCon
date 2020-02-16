using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using PTDevice;

namespace EasyCon.Script
{
    abstract class KeyAction : Statement
    {
        public readonly NintendoSwitch.Key Key;

        public KeyAction(NintendoSwitch.Key key)
        {
            Key = key;
        }

        public static Statement TryCompile(string text)
        {
            NintendoSwitch.Key key;
            Match m;
            m = Regex.Match(text, @"^([a-z]+)$", RegexOptions.IgnoreCase);
            if (m.Success && (key = NSKeys.Get(m.Groups[1].Value)) != null)
                return new KeyPress(key);
            m = Regex.Match(text, @"^([a-z]+)\s+(\d+)$", RegexOptions.IgnoreCase);
            if (m.Success && (key = NSKeys.Get(m.Groups[1].Value)) != null)
                return new KeyPress(key, int.Parse(m.Groups[2].Value));
            m = Regex.Match(text, @"^([a-z]+)\s+down$", RegexOptions.IgnoreCase);
            if (m.Success && (key = NSKeys.Get(m.Groups[1].Value)) != null)
                return new KeyDown(key);
            m = Regex.Match(text, @"^([a-z]+)\s+up$", RegexOptions.IgnoreCase);
            if (m.Success && (key = NSKeys.Get(m.Groups[1].Value)) != null)
                return new KeyUp(key);
            return null;
        }
    }

    class KeyPress : KeyAction
    {
        public const int DefaultDuration = 50;

        public readonly int Duration;
        bool _omitted = false;

        public KeyPress(NintendoSwitch.Key key)
            : base(key)
        {
            Duration = DefaultDuration;
            _omitted = true;
        }

        public KeyPress(NintendoSwitch.Key key, int duration)
            : base(key)
        {
            Duration = duration;
        }

        public override void Exec(Processor processor)
        {
            NintendoSwitch.GetInstance().Press(Key, Duration);
            Thread.Sleep(Duration);
        }

        protected override string _ToString()
        {
            return _omitted ? $"{NSKeys.GetName(Key)}" : $"{NSKeys.GetName(Key)} {Duration}";
        }
    }

    class KeyDown : KeyAction
    {
        public KeyDown(NintendoSwitch.Key key)
            : base(key)
        { }

        public override void Exec(Processor processor)
        {
            NintendoSwitch.GetInstance().Down(Key);
        }

        protected override string _ToString()
        {
            return$"{NSKeys.GetName(Key)} DOWN";
        }
    }

    class KeyUp : KeyAction
    {
        public KeyUp(NintendoSwitch.Key key)
            : base(key)
        { }

        public override void Exec(Processor processor)
        {
            NintendoSwitch.GetInstance().Up(Key);
        }

        protected override string _ToString()
        {
            return $"{NSKeys.GetName(Key)} UP";
        }
    }
}
