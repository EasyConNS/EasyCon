namespace EasyCon.Script2.Binding;

public enum BoundNodeKind
{
    Statement,
    BlockStatement,
    NopStatement,
    Expression,
    KeyAction,
    CallExpression,
    Goto,
    ConditionGoto,
    Return,
    Literal,

    If,
    For,
    Func
}