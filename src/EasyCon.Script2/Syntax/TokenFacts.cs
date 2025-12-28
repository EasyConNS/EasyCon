namespace EasyCon.Script2.Syntax;

public static class TokenFacts
{
    public static int GetUnaryOperatorPrecedence(this TokenType kind)
    {
        switch (kind)
        {
            case TokenType.SUB:
            case TokenType.BitNot:
            //case TokenType.LogicNot:
                return 6;

            default:
                return 0;
        }
    }

    public static int GetBinaryOperatorPrecedence(this TokenType kind)
    {
        switch (kind)
        {
            case TokenType.MUL:
            case TokenType.DIV:
            case TokenType.SlashI:
            case TokenType.MOD:
            case TokenType.BitAnd:
            case TokenType.SHL:
            case TokenType.SHR:
                return 5;

            case TokenType.ADD:
            case TokenType.SUB:
            case TokenType.BitOr:
            case TokenType.XOR:
                return 4;

            case TokenType.EQL:
            case TokenType.NEQ:
            case TokenType.LESS:
            case TokenType.LEQ:
            case TokenType.GTR:
            case TokenType.GEQ:
                return 3;

            case TokenType.LogicAnd:
                return 2;

            case TokenType.LogicOr:
                return 1;

            default:
                return 0;
        }
    }

    public static string? GetText(TokenType kind)
    {
        switch (kind)
        {
            // Operators
            case TokenType.ASSIGN:
                return "=";
            case TokenType.ADD:
                return "+";
            case TokenType.SUB:
                return "-";
            case TokenType.MUL:
                return "*";
            case TokenType.DIV:
                return "/";
            case TokenType.SlashI:
                return "\\";
            case TokenType.MOD:
                return "%";
            case TokenType.BitAnd:
                return "&";
            case TokenType.BitOr:
                return "|";
            case TokenType.XOR:
                return "^";
            case TokenType.SHL:
                return "<<";
            case TokenType.SHR:
                return ">>";
                
            case TokenType.ADD_ASSIGN:
                return "+=";
            case TokenType.SUB_ASSIGN:
                return "-=";
            case TokenType.MUL_ASSIGN:
                return "*=";
            case TokenType.DIV_ASSIGN:
                return "/=";
            case TokenType.SlashI_ASSIGN:
                return "\\=";
            case TokenType.MOD_ASSIGN:
                return "%=";
            case TokenType.BitAnd_ASSIGN:
                return "&=";
            case TokenType.BitOr_ASSIGN:
                return "|=";
            case TokenType.XOR_ASSIGN:
                return "^=";
            case TokenType.SHL_ASSIGN:
                return "<<=";
            case TokenType.SHR_ASSIGN:
                return ">>=";

            case TokenType.BitNot:
                return "~";

            case TokenType.EQL:
                return "==";
            case TokenType.NEQ:
                return "!=";
            case TokenType.LESS:
                return "<";
            case TokenType.GTR:
                return ">";
            case TokenType.LEQ:
                return "<=";
            case TokenType.GEQ:
                return ">=";

            case TokenType.LogicAnd:
                return "and";
            case TokenType.LogicOr:
                return "or";
            case TokenType.LogicNot:
                return "not";

            // Punctuation
            case TokenType.LeftParen:
                return "(";
            case TokenType.LeftBracket:
                return "[";
            case TokenType.COMMA:
                return ",";
            case TokenType.DOT:
                return ".";
            case TokenType.RightParen:
                return ")";
            case TokenType.RightBracket:
                return "]";
            case TokenType.COLON:
                return ":";
            case TokenType.SEMICOLON:
                return ";";
            default:
                return null;
        }
    }
}