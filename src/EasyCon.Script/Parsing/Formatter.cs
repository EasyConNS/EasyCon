using System.Text.RegularExpressions;

namespace EasyCon.Script.Parsing;

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

class Formatter(IEnumerable<ExternalVariable> extVars)
{
    private readonly Dictionary<string, int> Constants = [];
    private readonly Dictionary<string, ExternalVariable> ExtVars = extVars.ToDictionary(ev => ev.Name, s => s);

    public bool TryDeclConstant(string key, ExprBase value)
    {
        if (Constants.ContainsKey(key)) return false;
        Constants.Add(key, (InstantExpr)BinaryExpression.Rewrite(value));
        return true;
    }

    public VariableExpr GetVar(string text)
    {
        var m = Regex.Match(text, Formats.RegisterEx_F);
        if (!m.Success)
            throw new FormatException();
        var tag = text[1..];
        var lhs = true;
        if (uint.TryParse(tag, out var reg) && reg == 0 && lhs)
        {
            throw new ParseException(@"寄存器变量编号0暂无法使用");
        }
        return new VariableExpr(tag);
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

    public InstantExpr GetInstant(string text, bool zeroOrPos = false)
    {
        if (Regex.Match(text, Formats.Constant_F).Success)
        {
            if (!Constants.TryGetValue(text, out int v))
                throw new Exception($"未定义的常量“{text}”");
            if (zeroOrPos && v < 0)
                throw new Exception("不能使用负数");
            return new InstantExpr(v, text);
        }
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

    public ExprBase GetValueEx(Script2.Syntax.Token tok)
    {
        if(tok.Type == EasyCon.Script2.Syntax.TokenType.STRING)
        {
            return new LiteralExpr(tok.Value);
        }
        else
        {
            return GetValueEx(tok.Value);
        }
    }

    public ExprBase GetValueEx(string text)
    {
        if (Regex.Match(text, Formats.RegisterEx_F).Success)
            return GetVar(text);
        if (Regex.Match(text, Formats.ExtVar_F).Success)
            return GetExtVar(text); 
        else
            return GetInstant(text);
    }
}
