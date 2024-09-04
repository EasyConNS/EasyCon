namespace EC.Script.Syntax;

public enum NodeType
{
    ImportDeclaration,
    VariableDeclaration,
    FunctionDeclaration,

    Assignment,
    AugAssignment,

    IfStatement,
    ForStatement,
    BreakStatement,
    ContinueStatement,
    ReturnStatement,

    LiteralExpression,
    UnaryExpression,
    BinaryExpression,
    CallExpression,
}