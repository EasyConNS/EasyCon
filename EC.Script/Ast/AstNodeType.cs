namespace EC.Script.Ast;

internal enum AstNodeType
{
    // Statements
    BlockStatement,
    NopStatement,
    VariableDeclaration,
    IfStatement,
    ForStatement,
    LabelStatement,
    GotoStatement,
    ConditionalGotoStatement,
    ReturnStatement,
    ExpressionStatement,
    KeyActionStatement,

    // Expressions
    ErrorExpression,
    LiteralExpression,
    VariableExpression,
    AssignmentExpression,
    CompoundAssignmentExpression,
    UnaryExpression,
    BinaryExpression,
    CallExpression,
    ConversionExpression,
}
