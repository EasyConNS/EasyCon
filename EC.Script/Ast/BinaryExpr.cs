using EC.Script.Syntax;

namespace EC.Script.Ast;

internal sealed class BinaryExpr : Expression
{
    public BinaryExpr(SyntaxNode syntax, Expression left, BinaryOperator op, Expression right)
        : base(syntax)
    {
        Left = left;
        Operator = op;
        Right = right;
    }

    public Expression Left { get; private set; }
    public BinaryOperator Operator { get; private set; }
    public Expression Right { get; private set; }
    public SyntaxNode OpLexeme { get; private set; }

    public override AstNodeType Kind => AstNodeType.BinaryExpression;

    public override T Accept<T>(IAstVisitor<T> visitor)
    {
        return visitor.VisitBinary(this);
    }

    private static Dictionary<string, BinaryOperator> s_OperatorMap = new Dictionary<string, BinaryOperator>
    {
        ["+"] = BinaryOperator.Add,
        ["-"] = BinaryOperator.Substract,
        ["*"] = BinaryOperator.Multiply,
        ["/"] = BinaryOperator.Divide,
        ["\\"] = BinaryOperator.RangeDivide,

        ["%"] = BinaryOperator.Mod,
        ["^"] = BinaryOperator.Xor,
        ["&"] = BinaryOperator.And,
        ["|"] = BinaryOperator.Or,
        ["<<"] = BinaryOperator.LShift,
        [">>"] = BinaryOperator.RShift,

        ["<"] = BinaryOperator.Less,
        ["<="] = BinaryOperator.LessEq,
        [">"] = BinaryOperator.Greater,
        [">="] = BinaryOperator.GreaterEq,
        ["=="] = BinaryOperator.Equal,
        ["!="] = BinaryOperator.NotEqual,

        ["and"] = BinaryOperator.LogicalAnd,
        ["or"] = BinaryOperator.LogicalOr
    };
}
