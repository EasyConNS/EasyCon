using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace EasyCon.Script.Parsing
{
    static class Formats
    {
        const string __Constant = @"_[\d\p{L}_]+";
        const string __Register = @"\$\d+";
        const string __Number = @"-?\d+";
        public const string Constant = "(" + __Constant + ")";
        public const string Constant_F = "^" + __Constant + "$";
        public const string Register = "(" + __Register + ")";
        public const string Register_F = "^" + __Register + "$";
        public const string Instant = "(" + __Constant + "|" + __Number + ")";
        public const string Value = "(" + __Constant + "|" + __Register + "|" + __Number + ")";

        public class Formatter
        {
            public readonly Dictionary<string, int> Constants;

            public Formatter(Dictionary<string, int> constants)
            {
                Constants = constants;
            }

            public ValReg GetReg(string text, bool lhs = false)
            {
                var m = Regex.Match(text, @"\$(\d+)");
                if (!m.Success)
                    throw new FormatException();
                var reg = uint.Parse(m.Groups[1].Value);
                if (reg >= Processor.RegisterCount)
                    throw new ParseException($"寄存器取值范围$0~${Processor.RegisterCount - 1}");
                if (lhs && reg == 0)
                    throw new ParseException($"寄存器$0为只读");
                return new ValReg(reg);
            }

            public string GetRegText(uint reg)
            {
                return $"${reg}";
            }

            public ValInstant GetConstant(string text, bool zeroOrPos = false)
            {
                if (!Constants.ContainsKey(text))
                    throw new ParseException($"未定义的常量“{text}”");
                int v = Constants[text];
                if (zeroOrPos && v < 0)
                    throw new ParseException($"不能使用负数");
                return new ValInstant(v, text);
            }

            public ValInstant GetInstant(string text, bool zeroOrPos = false)
            {
                if (Regex.Match(text, Constant_F).Success)
                    return GetConstant(text);
                else
                {
                    int v = int.Parse(text);
                    if (zeroOrPos && v < 0)
                        throw new ParseException($"不能使用负数");
                    return v;
                }
            }

            public ValBase GetValue(string text, bool lhs = false)
            {
                if (Regex.Match(text, Register_F).Success)
                    return GetReg(text, lhs);
                else
                    return GetInstant(text);
            }
        }
    }
}
