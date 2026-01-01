using System.Text.RegularExpressions;

namespace EasyScript.Parsing;

static class Formats
{
    const string _ident = @"[\d\p{L}_]+";
    const string _Constant = @"_" + _ident;
    const string _ExtVar = @"@" + _ident;
    const string _Variable = @"\$" + _ident;
    const string _Number = @"-?\d+";

    public const string Constant_F = "^" + _Constant + "$";
    public const string ExtVar_F = "(" + _ExtVar + ")";
    public const string RegisterEx = "(" + _Variable + ")";
    public const string RegisterEx_F = "^" + _Variable + "$";
    public const string VariableEx_F = "^" + _Variable + "|" + _ExtVar + "$";
    public const string ValueEx = "(" + _Constant + "|" + _Variable + "|" + _ExtVar + "|" + _Number + ")";
}

class Formatter(Dictionary<string, ExternalVariable> extVars)
{
    private readonly Dictionary<string, VariableExpr> Variables = [];
    private readonly Dictionary<string, int> Constants = [];
    private readonly Dictionary<string, ExternalVariable> ExtVars = extVars;

    public bool TryDeclConstant(string key, ExprBase value)
    {
        if (Constants.ContainsKey(key)) return false;
        Constants.Add(key, value.Get(null));
        return true;
    }

    public VariableExpr GetReg(string text, bool decl = false)
    {
        var m = Regex.Match(text, Formats.RegisterEx_F);
        if (!m.Success)
            throw new FormatException();
        if (Variables.TryGetValue(text, out var regVar))
        {
            return regVar;
        }
        if(!decl)
        {
            throw new ParseException($"未定义的变量“{text}”");
        }
        var tag = text[1..];
        var lhs = true;
        if (uint.TryParse(tag, out var reg) && reg == 0 && lhs)
        {
            throw new ParseException(@"寄存器变量编号0暂无法使用");
        }
        var nv = new VariableExpr(tag);
        Variables.Add(text, nv);
        return nv;
    }

    private ExtVarExpr GetExtVar(string text)
    {
        if (!text.StartsWith('@'))
            throw new FormatException();
        var name = text[1..];
        if (!ExtVars.TryGetValue(name, out ExternalVariable? value))
            throw new ParseException($"找不到识图标签“{text}”");
        return new ExtVarExpr(value);
    }

    public ExprBase GetVar(string text)
    {
        if (Regex.Match(text, Formats.ExtVar_F).Success)
            return GetExtVar(text);
        return GetReg(text);
    }

    public InstantExpr GetConstant(string text, bool zeroOrPos = false)
    {
        if (!Constants.TryGetValue(text, out int v))
            throw new Exception($"未定义的常量“{text}”");
        if (zeroOrPos && v < 0)
            throw new Exception("不能使用负数");
        return new InstantExpr(v, text);
    }

    public InstantExpr GetInstant(string text, bool zeroOrPos = false)
    {
        if (Regex.Match(text, Formats.Constant_F).Success)
            return GetConstant(text);
        else
        {
            var ok = int.TryParse(text, out var v);
            if (!ok)
                throw new FormatException("无效的数字格式");
            if (zeroOrPos && v < 0)
                throw new Exception("不能使用负数");
            return v;
        }
    }

    public ExprBase GetValueEx(string text)
    {
        if (Regex.Match(text, Formats.VariableEx_F).Success)
            return GetVar(text);
        else
            return GetInstant(text);
    }
}
