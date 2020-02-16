using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace EasyCon.Script
{
    class Print : Statement
    {
        public readonly string Text;

        public Print(string text)
        {
            Text = text;
        }

        public static Statement TryCompile(string text)
        {
            var m = Regex.Match(text, @"^print\s+(.*)$", RegexOptions.IgnoreCase);
            if (m.Success)
                return new Print(m.Groups[1].Value);
            return null;
        }

        protected override string _ToString()
        {
            return $"PRINT {Text}";
        }

        public override void Exec(Processor processor)
        {
            processor.Output.Print(Text);
        }
    }
}
