namespace EasyCon.Script2.Syntax;

public static class TokenFacts
{
    public static int GetUnaryOperatorPrecedence(this TokenType kind)
    {
        return kind switch
        {
            TokenType.SUB or TokenType.BitNot => 6,//case TokenType.LogicNot:
            _ => 0,
        };
    }

    public static int GetBinaryOperatorPrecedence(this TokenType kind)
    {
        return kind switch
        {
            TokenType.MUL or TokenType.DIV or TokenType.SlashI or TokenType.MOD or TokenType.BitAnd or TokenType.SHL or TokenType.SHR => 5,
            TokenType.ADD or TokenType.SUB or TokenType.BitOr or TokenType.XOR => 4,
            TokenType.EQL or TokenType.NEQ or TokenType.LESS or TokenType.LEQ or TokenType.GTR or TokenType.GEQ => 3,
            TokenType.LogicAnd => 2,
            TokenType.LogicOr => 1,
            _ => 0,
        };
    }

    public static string? GetText(TokenType kind)
    {
        return kind switch
        {
            // Operators
            TokenType.ASSIGN => "=",
            TokenType.ADD => "+",
            TokenType.SUB => "-",
            TokenType.MUL => "*",
            TokenType.DIV => "/",
            TokenType.SlashI => "\\",
            TokenType.MOD => "%",
            TokenType.BitAnd => "&",
            TokenType.BitOr => "|",
            TokenType.XOR => "^",
            TokenType.SHL => "<<",
            TokenType.SHR => ">>",
            TokenType.ADD_ASSIGN => "+=",
            TokenType.SUB_ASSIGN => "-=",
            TokenType.MUL_ASSIGN => "*=",
            TokenType.DIV_ASSIGN => "/=",
            TokenType.SlashI_ASSIGN => "\\=",
            TokenType.MOD_ASSIGN => "%=",
            TokenType.BitAnd_ASSIGN => "&=",
            TokenType.BitOr_ASSIGN => "|=",
            TokenType.XOR_ASSIGN => "^=",
            TokenType.SHL_ASSIGN => "<<=",
            TokenType.SHR_ASSIGN => ">>=",
            TokenType.BitNot => "~",
            TokenType.EQL => "==",
            TokenType.NEQ => "!=",
            TokenType.LESS => "<",
            TokenType.GTR => ">",
            TokenType.LEQ => "<=",
            TokenType.GEQ => ">=",
            TokenType.LogicAnd => "and",
            TokenType.LogicOr => "or",
            TokenType.LogicNot => "not",
            // Punctuation
            TokenType.LeftParen => "(",
            TokenType.LeftBracket => "[",
            TokenType.COMMA => ",",
            TokenType.DOT => ".",
            TokenType.RightParen => ")",
            TokenType.RightBracket => "]",
            TokenType.COLON => ":",
            TokenType.SEMICOLON => ";",
            _ => null,
        };
    }

    public static bool OperatorIsAug(this TokenType kind)
    {
        return kind switch
        {
            TokenType.ADD_ASSIGN => true,
            TokenType.SUB_ASSIGN => true,
            TokenType.MUL_ASSIGN => true,
            TokenType.DIV_ASSIGN => true,
            TokenType.SlashI_ASSIGN => true,
            TokenType.MOD_ASSIGN => true,
            TokenType.BitAnd_ASSIGN => true,
            TokenType.BitOr_ASSIGN => true,
            TokenType.XOR_ASSIGN => true,
            TokenType.SHL_ASSIGN => true,
            TokenType.SHR_ASSIGN => true,
            _ => false,
        };
    }
}