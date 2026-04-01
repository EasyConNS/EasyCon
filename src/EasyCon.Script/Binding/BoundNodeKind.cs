namespace EasyCon.Script.Binding;

public enum BoundNodeKind
{
    Statement,
    BlockStatement,
    NopStatement,
    VariableDeclaration,
    ExpressionStatement,
    KeyAction,
    StickAction,

    GotoStatement,
    ConditionGotoStatement,
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