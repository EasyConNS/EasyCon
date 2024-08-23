namespace ECScript.Syntax;

public enum TokenType
{
    BadToken,

    // Trivia
    SkippedTextTrivia,
    LineBreakTrivia,
    WhitespaceTrivia,
    CommentTrivia, // #{XXX}

    // Tokens
    EndOfFileToken,
    NumberToken,
    StringToken,
    ConstToken, // _XXX
    VarToken, // $XXX
    ExVarToken, // @XXX
    EqualsToken, // =
    PlusToken, // +
    PlusEqualsToken, // +=
    MinusToken, // -
    MinusEqualsToken, // -=
    StarToken, // *
    StarEqualsToken, // *=
    SlashToken, // /
    SlashEqualsToken, // /=
    SlashIToken, // \
    SlashIEqualsToken, // \=
    ModToken, // %
    ModEqualsToken, // %=
    AndToken, // &
    AndEqualsToken, // &=
    OrToken, // |
    OrEqualsToken, // |=
    XorToken, // ^
    XorEqualsToken, // ^=
    TildeToken, // ~
    LeftShiftToken, // <<
    LeftShiftEqualsToken, // <<=
    RightShiftToken, // >>
    RightShiftEqualsToken, // >>=
    LessToken, // <
    LessEqualsToken, // <=
    GreaterToken, // >
    GreaterEqualsToken, // >=
    EqualsEqualsToken, // ==
    NotEqualsToken, // !=
    LeftPHToken, // (
    RightPHToken, // )
    LeftBKToken, // [
    RightBKToken, // ]
    ColonToken, // :
    CommaToken, // ,
    SemiToken, // ;
    DotToken, // .
   
    // Keywords
    ImportKeyword, // IMPORT
    AsKeyword, // AS
    IfKeyword, // IF
    ElifKeyword, // ELIF
    ElseKeyword, // ELSE
    EndifKeyword, // ENDIF
    ForKeyword, // FOR
    ToKeyword, // TO
    InKeyword, //IN
    StepKeyword, // STEP
    BreakKeyword, // BREAK
    ContinueKeyword, // CONTINUE
    NextKeyword, // NEXT
    FunctionKeyword, // FUNC
    ReturnKeyword, // RETURN
    EndFunctionKeyword, // ENDFUNC
    TrueKeyword, // TRUE
    FalseKeyword, // FALSE
    AndKeyword, // AND
    OrKeyword, // OR
    NotKeyword, // NOT

    // Gamepad Keys
    ResetKeyword, // RESET
    DPadKeyword, // DUP, DDOWN, DRIGHT, DLEFT
    DirKeyword, // UP, DOWN
    StickKeyword, // RS, LS
    ButtonKeyword, // A,B,X,Y.L,R,ZL,ZR.LCLICK,RCLICK.HOME,CAPTURE,PLUS,MINUS

    // Ident
    IdentifierToken,
}