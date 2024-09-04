namespace EC.Script.Syntax;

public static class SyntaxFacts
{
    public static int GetUnaryOperatorPrecedence(this TokenType kind)
    {
        switch (kind)
        {
            case TokenType.MinusToken:
            case TokenType.TildeToken: // 取反
            case TokenType.NotKeyword:
                return 8;

            default:
                return 0;
        }
    }

    public static int GetBinaryOperatorPrecedence(this TokenType kind)
    {
        switch (kind)
        {
            case TokenType.StarToken:
            case TokenType.SlashToken:
            case TokenType.SlashIToken:
            case TokenType.ModToken:
                return 7;

            case TokenType.PlusToken:
            case TokenType.MinusToken:
                return 6;

            case TokenType.LeftShiftToken:
            case TokenType.RightShiftToken:
                return 5;

            case TokenType.LessToken:
            case TokenType.LessEqualsToken:
            case TokenType.GreaterToken:
            case TokenType.GreaterEqualsToken:
                return 4;

            case TokenType.EqualsEqualsToken:
            case TokenType.NotEqualsToken:
                return 3;

            case TokenType.AndToken:
            case TokenType.XorToken:
            case TokenType.OrToken:
                return 2;

            case TokenType.AndKeyword:
            case TokenType.OrKeyword:
                return 1;

            default:
                return 0;
        }
    }
    public static bool IsComment(this TokenType kind)
    {
        return kind == TokenType.CommentTrivia;
    }
}
