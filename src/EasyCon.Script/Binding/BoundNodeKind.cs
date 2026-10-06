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
    CallExpression,
    RuntimeValue,

    StructInit,
    FieldAccess,
    FieldAssignment,
    IndexAssignment,

    While,
    ForStatement,
    UntilStatement,
    IfStatement,


    /// <summary>绑定失败占位表达式（BoundErrorExpression；诊断已报告，下游按错误收敛）。</summary>
    ErrorExpression,
}
