using EasyCon.Script2.Text;

namespace EasyCon.Script2.Syntax;

public enum TokenType
{
    BadToken,

    // Trivia
    NEWLINE,
    //LineBreakTrivia,
    WhitespaceTrivia,
    COMMENT, // #{XXX}

    IDENT,
    INT,
    Number,
    STRING,
    CONST, // _XXX
    VAR, // $XXX
    EX_VAR, // @XXX

    ASSIGN, // =
    ADD, // +
    SUB, // -
    MUL, // *
    DIV, // /
    SlashI, // \
    MOD, // %
    BitAnd, // &
    BitOr, // |
    XOR, // ^
    SHL, // <<
    SHR, // >>

    ADD_ASSIGN, // +=
    SUB_ASSIGN, // -=
    MUL_ASSIGN, // *=
    DIV_ASSIGN, // /=
    SlashI_ASSIGN, // \=
    MOD_ASSIGN, // %=
    BitAnd_ASSIGN, // &=
    BitOr_ASSIGN, // |=
    XOR_ASSIGN, // ^=
    SHL_ASSIGN, // <<=
    SHR_ASSIGN, // >>=

    BitNot, // ~

    EQL, // ==
    NEQ, // !=
    LESS, // <
    GTR, // >
    LEQ, // <=
    GEQ, // >=

    LogicAnd, // and
    LogicOr, // or
    LogicNot, // not

    //  Punctuation
    LeftParen, // ()
    RightParen,
    LeftBracket, // []
    RightBracket,
    OpenBrace, // {}
    CloseBrace, 
    COMMA, // ,
    DOT, // .
    COLON, // :
    SEMICOLON, // ;

    // Keywords
    STRUCT, // struct
    IMPORT, // import
    AS, // as
    IF, 
    ELIF,
    ELSE, 
    ENDIF,
    FOR, 
    TO,
    STEP,
    BREAK,
    CONTINUE,
    NEXT, 
    FUNC,
    RETURN,
    ENDFUNC, 
    TRUE, // true
    FALSE, // false

    // Gamepad Keys
    ResetKeyword, // RESET
    DPadKeyword, // DUP, DDOWN, DRIGHT, DLEFT
    DirKeyword, // UP, DOWN，RIGHT, LEFT
    StickKeyword, // RS, LS
    ButtonKeyword, // A,B,X,Y.L,R,ZL,ZR.LCLICK,RCLICK.HOME,CAPTURE,PLUS,MINUS

    EOF
}

public sealed record Token(SourceText text, TokenType type, string value, int line, int start)
{
    public TokenType Type { get; } = type;
    public string Value { get; } = value;
    public int Line { get; } = line;

    public SourceText Text { get; } = text;
    public SourceSpan Span => new(start, value.Length, line);

    public override string ToString()
    {
        return $"T{Line}('{Value}':{Type})";
    }
}
