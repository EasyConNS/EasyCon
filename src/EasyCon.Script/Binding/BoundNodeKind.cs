namespace EasyCon.Script2.Binding;

public enum BoundNodeKind
{
    Statement,
    BlockStatement,
    NopStatement,
    VariableDeclaration,
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
    SliceVariable,
    ExLabelVariable,
    BinaryExpression,
    UnaryExpression,
    ConversionExpression,
    AssignmentExpression,
    CallExpression,

    While,
}