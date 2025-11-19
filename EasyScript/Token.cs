namespace EasyScript;

public enum TokenType
{
    BadToken,

    // Trivia
    NEWLINE,
    //LineBreakTrivia,
    WhitespaceTrivia,
    CommentTrivia, // #{XXX}

    // Tokens
    Integer,
    //Decimal,
    Number,
    String,
    Const, // _XXX
    Variable, // $XXX
    ExtVariable, // @XXX

    Assign, // =
    Plus, // +
    PlusAssign, // +=
    Minus, // -
    MinusAssign, // -=
    Multiply, // *
    MultiplyAssign, // *=
    Divide, // /
    DivideAssign, // /=
    SlashI, // \
    SlashIAssign, // \=
    Modulus, // %
    ModulusAssign, // %=
    BitAnd, // &
    BitAndAssign, // &=
    BitOr, // |
    BitOrAssign, // |=
    BitXor, // ^
    BitXorAssign, // ^=
    BitNot, // ~
    LeftShift, // <<
    LeftShiftAssign, // <<=
    RightShift, // >>
    RightShiftAssign, // >>=

    EqualEqual, // ==
    NotEqual, // !=
    LessThan, // <
    LessThanEqual, // <=
    GreaterThan, // >
    GreaterThanEqual, // >=
    LogicAnd, // and
    LogicOr, // or
    LogicNot, // not

    //  Punctuation
    LeftParen, // (
    RightParen, // )
    LeftBracket, // [
    RightBracket, // ]
    ColonToken, // :
    Comma, // ,
    SemiToken, // ;
    Dot, // .

    // Keywords
    Import, // IMPORT
    As, // AS
    If, // IF
    Elif, // ELIF
    Else, // ELSE
    Endif, // ENDIF
    For, // FOR
    To, // TO
    Step, // STEP
    Break, // BREAK
    Continue, // CONTINUE
    Next, // NEXT
    Func, // FUNC
    Return, // RETURN
    EndFunc, // ENDFUNC
    True, // TRUE
    False, // FALSE

    // Gamepad Keys
    ResetKeyword, // RESET
    DPadKeyword, // DUP, DDOWN, DRIGHT, DLEFT
    DirKeyword, // UP, DOWN
    StickKeyword, // RS, LS
    ButtonKeyword, // A,B,X,Y.L,R,ZL,ZR.LCLICK,RCLICK.HOME,CAPTURE,PLUS,MINUS

    // Ident
    Identifier,

    EOF
}

internal class Token(TokenType type, string value, int line, int column)
{
    public TokenType Type { get; } = type;
    public string Value { get; } = value;
    public int Line { get; } = line;
    public int Column { get; } = column;

    public override string ToString()
    {
        return $"T('{Value}':{Type}:L{Line}:C{Column})";
    }
}
