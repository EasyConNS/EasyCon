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
                if (reg >= Processor.OfflineMaxRegisterCount)
                    throw new ParseException($"寄存器变量取值范围：0~{Processor.OfflineMaxRegisterCount - 1}");
                if (lhs && reg == 0)
                    throw new ParseException(@"寄存器变量编号0只读");
                return new ValReg(reg);
            } else
            {
                return new ValRegEx(text);
            }
        }
    }
}
