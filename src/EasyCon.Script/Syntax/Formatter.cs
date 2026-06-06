using EasyCon.Script.Symbols;
using System.Text.RegularExpressions;

namespace EasyCon.Script.Syntax;

static class Formatter
{
    // 运行时常量：求值阶段通过 getter 获取
    internal static readonly Dictionary<string, ScriptType> SpecialConsts = new()
    {
        ["__TIME__"] = ScriptType.Int,
        ["__APP__"] = ScriptType.String,
    };

    // 编译期常量：解析阶段直接折叠为字面量
    private static bool IsCompileTimeConst(string name) => name is "__FILE__";

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
                if (IsCompileTimeConst(tok.Value))
                    return FoldCompileTimeConst(tok);
                if (IsSpecialConst(tok.Value))
                    return new RuntimeValueExpr(tok, tok.Value);
                return new ConstVarExpr(tok);
            case TokenType.VAR:
                return new VariableExpr(tok);
            case TokenType.EX_VAR:
                return new ImageLabelExpr(tok, tok.Value[1..]);
            default:
                return new VariableExpr(tok, true);
        }
    }

    private static LiteralExpr FoldCompileTimeConst(Token tok)
    {
        var value = tok.Value switch
        {
            "__FILE__" => Path.GetDirectoryName(Path.GetFullPath(tok.Location.FileName) ?? "") ?? "",
            _ => ""
        };
        return new LiteralExpr(tok, value);
    }
}