using System.Text.RegularExpressions;

namespace EasyCon.Script.Syntax;

static class Formatter
{
    public const string ValueEx = Formats.ValueEx;

    public static ExprBase GetValueEx(Token tok)
    {
        switch (tok.Type)
        {
            case TokenType.STRING:
                return tok.Value;
            case TokenType.INT:
                return int.Parse(tok.Value);
            case TokenType.CONST:
                return new ConstVarExpr(tok.Value);
            case TokenType.VAR:
                return new VariableExpr(tok.Value);
            case TokenType.EX_VAR:
                {
                    var name = tok.Value[1..];
                    return new ExtVarExpr(name);
                }
            default:
                throw new FormatException($"表达式类型不正确：{tok.Type}");
        }
    }

    public static ExprBase? GetValueEx(string text)
    {
        if (Regex.Match(text, Formats.RegisterEx_F).Success)
            return new VariableExpr(text);
        if (Regex.Match(text, Formats.ExtVar_F).Success)
        {
            var name = text[1..];
            return new ExtVarExpr(name);
        }
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
}
