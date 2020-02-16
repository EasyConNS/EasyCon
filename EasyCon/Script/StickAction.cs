using PTDevice;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace EasyCon.Script
{
    abstract class StickAction : Statement
    {
        public readonly NintendoSwitch.Key Key;
        public readonly string KeyName;
        public readonly string Direction;

        public StickAction(NintendoSwitch.Key key, string keyname, string direcion)
        {
            Key = key;
            KeyName = keyname?.ToUpper();
            Direction = direcion?.ToUpper();
        }

        public static Statement TryCompile(string text)
        {
            Match m;
            m = Regex.Match(text, @"^([lr]s)\s+(reset)$", RegexOptions.IgnoreCase);
            if (m.Success)
            {
                var keyname = m.Groups[1].Value;
                var key = GetKey(keyname);
                if (key == null)
                    return null;
                return new StickUp(key, keyname);
            }
            m = Regex.Match(text, @"^([lr]s)\s+([a-z0-9]+)$", RegexOptions.IgnoreCase);
            if (m.Success)
            {
                var keyname = m.Groups[1].Value;
                var direction = m.Groups[2].Value;
                var key = GetKey(keyname, direction);
                if (key == null)
                    return null;
                return new StickDown(key, keyname, direction);
            }
            m = Regex.Match(text, @"^([lr]s)\s+([a-z0-9]+)\s*,\s*(\d+)$", RegexOptions.IgnoreCase);
            if (m.Success)
            {
                var keyname = m.Groups[1].Value;
                var direction = m.Groups[2].Value;
                var duration = m.Groups[3].Value;
                var key = GetKey(keyname, direction);
                if (key == null)
                    return null;
                return new StickPress(key, keyname, direction, int.Parse(duration));
            }
            return null;
        }

        static NintendoSwitch.Key GetKey(string keyname, string direction = "0")
        {
            int degree;
            if (int.TryParse(direction, out degree))
            {
                if (keyname.Equals("LS", StringComparison.OrdinalIgnoreCase))
                    return NintendoSwitch.Key.LStick(degree);
                else
                    return NintendoSwitch.Key.RStick(degree);
            }
            else
            {
                var dk = NSKeys.GetDirection(direction);
                if (dk == NintendoSwitch.DirectionKey.None)
                    return null;
                if (keyname.Equals("LS", StringComparison.OrdinalIgnoreCase))
                    return NintendoSwitch.Key.LStick(dk);
                else
                    return NintendoSwitch.Key.RStick(dk);
            }
        }
    }

    class StickPress : StickAction
    {
        public readonly int Duration;

        public StickPress(NintendoSwitch.Key key, string keyname, string direction, int duration)
            : base(key, keyname, direction)
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
            return $"{KeyName} {Direction},{Duration}";
        }
    }

    class StickDown : StickAction
    {
        public StickDown(NintendoSwitch.Key key, string keyname, string direction)
            : base(key, keyname, direction)
        { }

        public override void Exec(Processor processor)
        {
            NintendoSwitch.GetInstance().Down(Key);
        }

        protected override string _ToString()
        {
            return $"{KeyName} {Direction}";
        }
    }

    class StickUp : StickAction
    {
        public StickUp(NintendoSwitch.Key key, string keyname)
            : base(key, keyname, null)
        { }

        public override void Exec(Processor processor)
        {
            NintendoSwitch.GetInstance().Up(Key);
        }

        protected override string _ToString()
        {
            return $"{KeyName} RESET";
        }
    }
}
