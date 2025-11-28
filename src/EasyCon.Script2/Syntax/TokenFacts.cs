namespace EC.Script.Syntax;

public static class TokenFacts
{
    public static int GetUnaryOperatorPrecedence(this TokenType kind)
    {
        switch (kind)
        {
            case TokenType.SUB:
            case TokenType.BitNot:
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
}