namespace EasyScript.Binding;

enum BoundNodeKind
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