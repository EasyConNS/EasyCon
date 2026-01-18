namespace EasyCon.Script2.Binding;

public enum BoundNodeKind
{
    Statement,
    BlockStatement,
    NopStatement,
    ExpressionStatement,
    KeyAction,

    Goto,
    ConditionGoto,
    Label,
    Return,

    Literal,
    Variable,
    ExLabelVariable,
    BinaryExpression,
    UnaryExpression,
    ConversionExpression,
    AssignmentExpression,
    CallExpression,

    While,
}