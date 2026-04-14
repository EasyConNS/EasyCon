using System.Text.RegularExpressions;

namespace EasyCon.Script.Syntax;

static class Formatter
{
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
                return new VariableExpr("??", true);
                // throw new FormatException($"表达式类型不正确：{tok.Type}");
        }
    }
}
