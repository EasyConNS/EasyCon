using System.Text.RegularExpressions;

namespace EasyScript.Parsing;

class Formatter(Dictionary<string, int> constants, Dictionary<string, ExternalVariable> extVars)
{
    private readonly Dictionary<string, int> Constants = constants;
    private readonly Dictionary<string, ExternalVariable> ExtVars = extVars;

    public bool TryDeclConstant(string key, string value)
    {
        if (Constants.ContainsKey(key)) return false;
        Constants.Add(key, GetInstant(value).Val);
        return true;
    }
    
    private ValExtVar GetExtVar(string text)
    {
        if (!text.StartsWith('@'))
            throw new FormatException();
        var name = text[1..];
        if (!ExtVars.TryGetValue(name, out ExternalVariable? value))
            throw new ParseException($"找不到识图标签：“{text}");
        return new ValExtVar(value);
    }

    public ValVar GetVar(string text)
    {
        if (Regex.Match(text, Formats.ExtVar_F).Success)
            return GetExtVar(text);
        return FormatterUtil.GetRegEx(text);
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

    public ValBase GetValueEx(string text)
    {
        if (Regex.Match(text, Formats.VariableEx_F).Success)
            return GetVar(text);
        else
            return GetInstant(text);
    }
}


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
        if (uint.TryParse(text, out var reg) && reg < Processor.OfflineMaxRegisterCount)
        {
            if (lhs && reg == 0)
                throw new ParseException(@"寄存器变量编号0只读");
            return new ValReg(reg);
        }
        else
        {
            return new ValRegEx(text);
        }
    }
}
