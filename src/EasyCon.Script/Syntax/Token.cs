using EasyCon.Script.Text;

namespace EasyCon.Script.Syntax;

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
    WHILE,
    FOR,
    TO,
    IN,
    STEP,
    BREAK,
    CONTINUE,
    NEXT,
    FUNC,
    RETURN,
    ENDFUNC,
    EXTERN,
    FROM,
    END,
    TRUE, // true
    FALSE, // false

    // Gamepad Keys
    ResetKeyword, // RESET
    StickKeyword, // RS, LS
    ButtonKeyword, // A,B,X,Y.L,R,ZL,ZR.LCLICK,RCLICK.HOME,CAPTURE,PLUS,MINUS
    StateKeyword, // 特殊处理的UP/DOWN关键字, 需要特殊处理表示抬起按下
    DirectionKeyword, // 特殊处理的方向关键字
    // UP, DOWN，RIGHT, LEFT, DOWNLEFT, DOWNRIGHT, UPLEFT, UPRIGHT 需要特殊处理，既表示DPAD，也表示方向

    EOF
}

public sealed record Token(SourceText text, TokenType type, string value, int start)
{
    public TokenType Type { get; } = type;
    public string Value { get; } = value;

    public SourceText Text { get; } = text;
    public SourceSpan Span => new(start, Value.Length);

    public TextLocation Location => new(Text, Span);

    public override string ToString()
    {
        return $"T{Location.StartLine + 1}('{Value}':{Type})";
    }
}