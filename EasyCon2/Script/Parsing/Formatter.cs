using System.Text.RegularExpressions;

namespace EasyCon2.Script.Parsing
{
    class Formatter
    {
        private readonly Dictionary<string, int> Constants;
        private readonly Dictionary<string, ExternalVariable> ExtVars;

        public Formatter(Dictionary<string, int> constants, Dictionary<string, ExternalVariable> extVars)
        {
            Constants = constants;
            ExtVars = extVars;
        }

        public void SetConstantTable(string key, int valu)
        {
            Constants[key] = valu;
        }
        
        private ValExtVar GetExtVar(string text, bool lhs = false)
        {
            if (!text.StartsWith("@"))
                throw new FormatException();
            var name = text[1..];
            if (!ExtVars.ContainsKey(name))
                throw new ParseException($"未定义的搜图变量“{text}”");
            return new ValExtVar(ExtVars[name]);
        }

        public ValVar GetVar(string text, bool lhs = false)
        {
            if (Regex.Match(text, Formats.ExtVar_F).Success)
                return GetExtVar(text, lhs);
            return FormatterUtil.GetRegEx(text, lhs);
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
            if (Regex.Match(text, Formats.Constant_F).Success)
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
            if (Regex.Match(text, Formats.VariableEx_F).Success)
                return GetVar(text, lhs);
            else
                return GetInstant(text);
        }
    }
}
