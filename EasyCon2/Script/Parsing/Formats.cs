using System.Text.RegularExpressions;

namespace EasyCon2.Script.Parsing
{
    static class Formats
    {
        const string __Constant = @"_[\d\p{L}_]+";
        const string __Register = @"\$\d+";
        const string __Register32 = @"\$\$\d+";
        const string __Number = @"-?\d+";
        const string __ExtVar = @"@[\d\p{L}_]+";

        public const string Constant = "(" + __Constant + ")";
        public const string Constant_F = "^" + __Constant + "$";
        public const string Register = "(" + __Register + ")";
        public const string Register_F = "^" + __Register + "$";
        public const string Register32_F = "^" + __Register32 + "$";
        public const string ExtVar = "(" + __ExtVar + ")";
        public const string ExtVar_F = "(" + __ExtVar + ")";
        public const string RegisterEx = "(" + __Register + "|" + __Register32 + ")";
        public const string RegisterEx_F = "^" + __Register + "|" + __Register32 + "$";
        public const string VariableEx = "(" + __Register + "|" + __Register32 + "|" + __ExtVar + ")";
        public const string VariableEx_F = "^" + __Register + "|" + __Register32 + "|" + __ExtVar + "$";
        public const string Instant = "(" + __Constant + "|" + __Number + ")";
        public const string Value = "(" + __Constant + "|" + __Register + "|" + __Number + ")";
        public const string ValueEx = "(" + __Constant + "|" + __Register + "|" + __Register32 + "|" + __ExtVar + "|" + __Number + ")";

        public class Formatter
        {
            public readonly Dictionary<string, int> Constants;
            public readonly Dictionary<string, ExternalVariable> ExtVars;

            public Formatter(Dictionary<string, int> constants, Dictionary<string, ExternalVariable> extVars)
            {
                Constants = constants;
                ExtVars = extVars;
            }

            public string GetRegText(uint reg)
            {
                return $"${reg}";
            }

            public string GetReg32Text(uint reg)
            {
                return $"$${reg}";
            }

            public string GetExtVarText(string name)
            {
                return $"@{name}";
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

            public ValReg32 GetReg32(string text, bool lhs = false)
            {
                var m = Regex.Match(text, @"\$\$(\d+)");
                if (!m.Success)
                    throw new FormatException();
                var reg = uint.Parse(m.Groups[1].Value);
                if (reg + 1 >= Processor.RegisterCount)
                    throw new ParseException($"32位寄存器取值范围$$0~$${Processor.RegisterCount - 2}");
                if (lhs && reg == 0)
                    throw new ParseException($"寄存器$$0为只读");
                return new ValReg32(reg);
            }

            public ValRegEx GetRegEx(string text, bool lhs = false)
            {
                if (Regex.Match(text, Register_F).Success)
                    return GetReg(text, lhs);
                if (Regex.Match(text, Register32_F).Success)
                    return GetReg32(text, lhs);
                throw new FormatException();
            }

            public ValExtVar GetExtVar(string text, bool lhs = false)
            {
                if (!text.StartsWith("@"))
                    throw new FormatException();
                var name = text.Substring(1);
                if (!ExtVars.ContainsKey(name))
                    throw new ParseException($"未定义的外部变量“{text}”");
                return new ValExtVar(ExtVars[name]);
            }

            public ValVar GetVar(string text, bool lhs = false)
            {
                if (Regex.Match(text, ExtVar_F).Success)
                    return GetExtVar(text, lhs);
                return GetRegEx(text, lhs);
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

            public ValBase GetValueEx(string text, bool lhs = false)
            {
                if (Regex.Match(text, VariableEx_F).Success)
                    return GetVar(text, lhs);
                else
                    return GetInstant(text);
            }
        }
    }
}
