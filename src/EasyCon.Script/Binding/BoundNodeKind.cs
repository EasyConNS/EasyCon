namespace EasyCon.Script.Binding;

public enum BoundNodeKind
{
    Statement,
    BlockStatement,
    NopStatement,
    ConstantDeclaration,
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
    CallExpression,

    StructInit,
    FieldAccess,
    FieldAssignment,
    FieldIndexAccess,
    FieldIndexAssignment,
    IndexAssignment,

    While,
    IfStatement,
}