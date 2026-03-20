namespace EasyCon.Script2.Binding;

public enum BoundNodeKind
{
    Statement,
    BlockStatement,
    NopStatement,
    ExpressionStatement,
    KeyAction,
    StickAction,

    Goto,
    ConditionGoto,
    Label,
    Return,

    Literal,
    IndexDecl,
    Variable,
    IndexVariable,
    ExLabelVariable,
    BinaryExpression,
    UnaryExpression,
    ConversionExpression,
    AssignmentExpression,
    CallExpression,

    While,
}