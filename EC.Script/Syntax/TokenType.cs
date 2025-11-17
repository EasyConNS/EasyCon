namespace EC.Script.Syntax;

public enum TokenType
{
    BadToken,

    // Trivia
    SkippedTextTrivia,
    LineBreakTrivia,
    WhitespaceTrivia,
    CommentTrivia, // #{XXX}

    // Tokens
    EOF,
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
    LessThan, // <
    LessThanEqual, // <=
    GreaterThan, // >
    GreaterThanEqual, // >=
    EqualEqual, // ==
    NotEqual, // !=

    // TODO
    Punctuation,

    LeftPHToken, // (
    RightPHToken, // )
    LeftBKToken, // [
    RightBKToken, // ]
    ColonToken, // :
    CommaToken, // ,
    SemiToken, // ;
    DotToken, // .
   
    // Keywords
    Import, // IMPORT
    As, // AS
    If, // IF
    Elif, // ELIF
    Else, // ELSE
    Endif, // ENDIF
    For, // FOR
    To, // TO
    In, //IN
    Step, // STEP
    Break, // BREAK
    Continue, // CONTINUE
    Next, // NEXT
    Func, // FUNC
    Return, // RETURN
    EndFunc, // ENDFUNC
    True, // TRUE
    False, // FALSE
    LogicAnd, // AND
    LogicOr, // OR
    LogicNot, // NOT

    // Gamepad Keys
    ResetKeyword, // RESET
    DPadKeyword, // DUP, DDOWN, DRIGHT, DLEFT
    DirKeyword, // UP, DOWN
    StickKeyword, // RS, LS
    ButtonKeyword, // A,B,X,Y.L,R,ZL,ZR.LCLICK,RCLICK.HOME,CAPTURE,PLUS,MINUS

    // Ident
    Identifier,
}