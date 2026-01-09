namespace EasyCon.Script2.Binding;

public enum BoundNodeKind
{
    Statement,
    BlockStatement,
    NopStatement,
    Expression,
    KeyAction,

    Goto,
    ConditionGoto,
    Label,
    Return,

    Literal,
    Variable,
    BinaryExpression,
    CallExpression,

    If,
    For,
    While,
    Func
}