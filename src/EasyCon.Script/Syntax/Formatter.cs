using System.Text.RegularExpressions;

namespace EasyCon.Script.Syntax;

static class Formats
{
    const string _ident = @"[\d\p{L}_]+";
    const string _Constant = @"_" + _ident;
    const string _ExtVar = @"@" + _ident;
    const string _Variable = @"\$" + _ident;
    const string _Number = @"-?\d+";

    public const string Constant_F = "^" + _Constant + "$";
    public const string ExtVar_F = "(" + _ExtVar + ")";
    public const string RegisterEx_F = "^" + _Variable + "$";
    public const string ValueEx = "(" + _Constant + "|" + _Variable + "|" + _ExtVar + "|" + _Number + ")";
}

class Formatter(IEnumerable<string> extVarNames)
{
    private readonly HashSet<string> ExtVarNames = new(extVarNames);

    private ExtVarExpr GetExtVar(string text)
    {
        if (!text.StartsWith('@'))
            throw new FormatException();
        var name = text[1..];
        if (!ExtVarNames.Contains(name))
            throw new ParseException($"找不到识图标签“{text}”");
        return new ExtVarExpr(name);
    }

    public ExprBase GetValueEx(Token tok)
    {
        switch (tok.Type)
        {
            case TokenType.STRING:
                return tok.Value;
            case TokenType.INT:
                {
                    var ok = int.TryParse(tok.Value, out var v);
                    if (!ok) throw new FormatException($"数字格式解析错误：{tok.Value}");
                    return v;
                }
            case TokenType.CONST:
                return new VariableExpr(tok.Value, true);
            case TokenType.VAR:
                return new VariableExpr(tok.Value);
            case TokenType.EX_VAR:
                return GetExtVar(tok.Value);
            default:
                throw new FormatException($"token类型不正确：{tok.Type}");
        }
    }

    public ExprBase GetValueEx(string text)
    {
        if (Regex.Match(text, Formats.RegisterEx_F).Success)
            return new VariableExpr(text);
        if (Regex.Match(text, Formats.ExtVar_F).Success)
            return GetExtVar(text);
        else if (Regex.Match(text, Formats.Constant_F).Success)
            return new VariableExpr(text, true);
        else if (Regex.Match(text, "^^\"(?:[^\"\\\\]|\\\\.)*\"$").Success)
            return new LiteralExpr(text);
        else
        {
            var ok = int.TryParse(text, out var v);
            if (!ok) throw new FormatException("无效的数字格式");
            return v;
        }
    }
}
