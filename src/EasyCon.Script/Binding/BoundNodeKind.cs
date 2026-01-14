namespace EasyCon.Script2.Binding;

public enum BoundNodeKind
{
    Statement,
    BlockStatement,
    NopStatement,
    Expression,
    CallExpression,
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

    While,
}