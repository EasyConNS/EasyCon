using System.Text.RegularExpressions;

namespace EasyCon.Script.Syntax;

static class Formatter
{
    public static ExprBase GetValueEx(Token tok)
    {
        switch (tok.Type)
        {
            case TokenType.STRING:
                return new LiteralExpr(tok, tok.Value);
            case TokenType.INT:
                return new LiteralExpr(tok, int.Parse(tok.Value));
            case TokenType.CONST:
                return new ConstVarExpr(tok);
            case TokenType.VAR:
                return new VariableExpr(tok);
            case TokenType.EX_VAR:
                {
                    var name = tok.Value[1..];
                    return new ExtVarExpr(tok, name);
                }
            default:
                return new VariableExpr(tok, true);
                // throw new FormatException($"表达式类型不正确：{tok.Type}");
        }
    }
}