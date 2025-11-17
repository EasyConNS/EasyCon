namespace EC.Script.Syntax;

// 定义Token类型
public enum TokenType1
{
    Comment,

    Number,
    String,
    Const, // _xxx
    Variable, // $xxx
    ExtVariable, // @xxx

    Assign, // =
    Plus, // +
    Minus, // -
    Multiply, // *
    Divide, // /
    SlashI, // \
    Modulus, // %
    BitwiseAnd, // &
    BitwiseOr, // |
    BitwiseXor, // ^
    BitwiseNot, // ~
    PlusAssign, // +=
    MinusAssign, // -=
    MultiplyAssign, // *=
    DivideAssign, // /=
    SlashIAssign, // \=
    ModulusAssign, // %=
    BitwiseAndAssign, // &=
    BitwiseOrAssign, // |=
    BitwiseXorAssign, // ^=
    LeftShift, // <<
    RightShift, // >> 
    LeftShiftAssign, // <<=
    RightShiftAssign, // >>=

    EqualEqual, // ==
    NotEqual, // !=
    LessThan, // <
    GreaterThan, // >
    LessThanEqual, // <=
    GreaterThanEqual, // >=
    LogicAnd, // and
    LogicOr, // or
    LogicNot, // not
    Punctuation,
    
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
    GamepadKeyword,
    Identifier,
    
    EOF
}

// 定义Token类
public class Token
{
    public TokenType Type { get; set; }
    public string Value { get; set; }
    public int LineNumber { get; set; }

    public Token(TokenType type, string value, int lineNumber)
    {
        Type = type;
        Value = value;
        LineNumber = lineNumber;
    }
}