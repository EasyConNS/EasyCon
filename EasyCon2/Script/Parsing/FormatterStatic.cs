using System.Text.RegularExpressions;

namespace EasyCon2.Script.Parsing
{
    partial class Formatter
    {
        public static string GetRegText(uint reg)
        {
            return $"${reg}";
        }

        public static string GetReg32Text(uint reg)
        {
            return $"$${reg}";
        }

        public static string GetExtVarText(string name)
        {
            return $"@{name}";
        }

        public static ValReg GetReg(string text, bool lhs = false)
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

        private static ValReg32 GetReg32(string text, bool lhs = false)
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

        public static ValRegEx GetRegEx(string text, bool lhs = false)
        {
            if (Regex.Match(text, Formats.Register_F).Success)
                return GetReg(text, lhs);
            if (Regex.Match(text, Formats.Register32_F).Success)
                return GetReg32(text, lhs);
            throw new FormatException();
        }
    }
}
