using EasyCon.Script.Symbols;
using System.Text.RegularExpressions;

namespace EasyCon.Script.Syntax;

static class Formatter
{
    internal static readonly Dictionary<string, ScriptType> SpecialConsts = new()
    {
        ["__TIME__"] = ScriptType.Int,
        ["__FILE__"] = ScriptType.String,
    };

    internal static bool IsSpecialConst(string name) => SpecialConsts.ContainsKey(name);

    public static BaseExpr GetValueEx(Token tok)
    {
        switch (tok.Type)
        {
            case TokenType.STRING:
                return new LiteralExpr(tok, tok.Value);
            case TokenType.INT:
                return new LiteralExpr(tok, int.Parse(tok.Value));
            case TokenType.CONST:
                if (IsSpecialConst(tok.Value))
                    return new RuntimeValueExpr(tok, tok.Value);
                return new ConstVarExpr(tok);
            case TokenType.VAR:
                return new VariableExpr(tok);
            case TokenType.EX_VAR:
                return new RuntimeValueExpr(tok, tok.Value[1..]);
            default:
                return new VariableExpr(tok, true);
        }
    }
}