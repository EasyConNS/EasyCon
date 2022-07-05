using System.Text.RegularExpressions;

namespace EasyScript.Parsing
{
    class FormatterUtil
    {
        public static ValRegEx GetRegEx(string text, bool lhs = false)
        {
            if (Regex.Match(text, Formats.RegisterEx_F).Success)
                return GetVars(text, lhs);
            throw new FormatException();
        }

        private static ValRegEx GetVars(string text, bool lhs = false)
        {
            var m = Regex.Match(text, Formats.RegisterEx_F);
            if (!m.Success)
                throw new FormatException();
            text = text[1..];
            if (uint.TryParse(text, out var reg))
            {
                if (reg >= Processor.RegisterCount)
                    throw new ParseException($"寄存器取值范围0~{Processor.RegisterCount - 1}");
                if (lhs && reg == 0)
                    throw new ParseException(@"寄存器0为只读");
                return new ValReg(reg);
            } else
            {
                return new ValRegEx(text);
            }
        }
    }
}
