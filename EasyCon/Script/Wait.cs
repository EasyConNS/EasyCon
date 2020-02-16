using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using System.Threading;

namespace EasyCon.Script
{
    class Wait : Statement
    {
        public readonly int Duration;
        bool _omitted;

        public Wait(int duration, bool omitted)
        {
            Duration = duration;
            _omitted = omitted;
        }

        public static Statement TryCompile(string text)
        {
            int duration;
            if (int.TryParse(text, out duration))
                return new Wait(duration, true);
            var m = Regex.Match(text, @"^wait\s+(\d+)$", RegexOptions.IgnoreCase);
            if (m.Success)
                return new Wait(int.Parse(m.Groups[1].Value), false);
            return null;
        }

        protected override string _ToString()
        {
            return _omitted ? $"{Duration}" : $"WAIT {Duration}";
        }

        public override void Exec(Processor processor)
        {
            Thread.Sleep(Duration);
        }
    }
}
