namespace EasyCon.Script2.Binding;

public enum BoundNodeKind
{
    Statement,
    BlockStatement,
    NopStatement,
    ExpressionStatement,
    CallStatement,
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
    CallExpression,

    While,
}