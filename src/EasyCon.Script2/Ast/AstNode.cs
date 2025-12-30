using EasyCon.Script2.Syntax;
using System.Collections.Immutable;

namespace EasyCon.Script2.Ast;

// AST节点基类
public abstract class ASTNode(Token key)
{
    public Token Key = key;
    public int Line => key.Line;

    public virtual T Accept<T>(IAstVisitor<T> visitor) {throw new NotImplementedException();}

    public readonly List<TriviaNode> LeadingTrivia = [];
    public readonly List<TriviaNode> TrailingTrivia = [];
}

public sealed class MainProgram(ImmutableArray<ImportStatement> importDecl, ImmutableArray<Member> statements, Token endOfFileToken) : ASTNode(endOfFileToken)
{
    public ImmutableArray<ImportStatement> Imports = importDecl;
    public ImmutableArray<Member> Statements = statements;
    public Token EndOfFileToken = endOfFileToken;

    public override T Accept<T>(IAstVisitor<T> visitor)
    {
        return visitor.VisitProgram(this);
    }
}

public abstract class Member(Token key) : ASTNode(key){}

public sealed class GlobalStatement(Statement statement) : Member(statement.Key)
{
    public Statement Statement { get; } = statement;

    public override T Accept<T>(IAstVisitor<T> visitor)
    {
        return Statement.Accept(visitor);
    }
}

// 表达式节点
public abstract class Expression(Token key) : ASTNode(key) { }

public sealed class TriviaNode(Token trivia) : Expression(trivia)
{
    public string Text => trivia.Value;
}

// 字面量表达式
public sealed class LiteralExpression : Expression
{
    public object Value { get; }

    public LiteralExpression(Token keyword, object value) : base(keyword)
    {
        // 在构造时验证数值范围
        if (value is string strValue)
        {
            Value = strValue;
        }
        else if (value is uint intValue)
        {
            if (intValue < ushort.MinValue || intValue > ushort.MaxValue)
            {
                throw new Exception($"整数超出范围 ({ushort.MinValue} - {ushort.MaxValue})");
            }
            Value = (uint)intValue;
        }
        else
        {
            Value = value;
        }
    }
}

// 变量表达式
public sealed class VariableExpression(Token keyword, bool isConstant, bool isSpecial) : Expression(keyword)
{
    public string Name { get; } = keyword.Value;
    public bool IsConstant { get; } = isConstant;
    public bool IsSpecial { get; } = isSpecial;
}

public sealed class IndexExpression(Token keyword, ImmutableArray<Expression> items, Token rb) : Expression(keyword)
{
    public ImmutableArray<Expression> Items { get; } = items;
}

public sealed class ParenthesizedExpression(Token left, Expression expr, Token right) : Expression(left)
{
    public Expression Expr { get; } = expr;
}

public sealed class UnaryExpression(Token op, Expression right) : Expression(op)
{
    public TokenType Operator { get; } = op.Type;
    public Expression Right { get; } = right;
}

// 二元运算表达式
public sealed class BinaryExpression(Expression left, Token op, Expression right) : Expression(op)
{
    public Expression Left { get; } = left;
    public TokenType Operator { get; } = op.Type;
    public Expression Right { get; } = right;
}

// 条件表达式
public sealed class ConditionExpression(Expression left, Token op, Expression right) : Expression(op)
{
    public Expression Left { get; } = left;
    public TokenType Operator { get; } = op.Type;
    public Expression Right { get; } = right;
}

public sealed class NotConditionExpression(Token op, ConditionExpression condition) : Expression(op)
{
    public ConditionExpression Condition { get; } = condition;
}

// 语句节点
public abstract class Statement(Token key) : ASTNode(key) { }

public sealed class KeywordStatement(Token keyword) : Statement(keyword) { }

public sealed class ImportStatement(Token imp, string name, LiteralExpression path) : Member(imp)
{
    public string Name { get; } = name;
    public LiteralExpression Path { get; } = path;

    public override T Accept<T>(IAstVisitor<T> visitor)
    {
        return visitor.VisitImport(this);
    }
}

// 赋值语句
public sealed class AssignmentStatement(Token varToken, VariableExpression? Variablee, Token assignment, Expression expression) : Statement(varToken)
{
    public VariableExpression Variable { get; } = Variablee!;
    public TokenType AssignmentType => Assignment.Type;
    public Expression Expression { get; } = expression;

    private Token Assignment { get; } = assignment;

    public override T Accept<T>(IAstVisitor<T> visitor)
    {
        return visitor.VisitAssignmentStat(this);
    }
}

// If语句
public sealed class IfStatement(IfCondition condition, ImmutableArray<Statement> thenBranch, ImmutableArray<ElseIfClause> elseIfBranch, ElseClause? elseClause, KeywordStatement endif) : Statement(condition.Key)
{
    public IfCondition Condition { get; } = condition;
    public ImmutableArray<Statement> ThenBranch { get; } = thenBranch;

    public ImmutableArray<ElseIfClause> ElseIfBranch { get; } = elseIfBranch;
    public ElseClause? ElseClause { get; } = elseClause;

    public KeywordStatement Endif { get; } = endif;

    public override T Accept<T>(IAstVisitor<T> visitor)
    {
        return visitor.VisitIfStat(this);
    }
}

public sealed class IfCondition(Token token, Expression condition) : Statement(token)
{
    public Expression Condition { get; } = condition;

    public override T Accept<T>(IAstVisitor<T> visitor)
    {
        return Condition.Accept(visitor);
    }
}

public sealed class ElseIfClause(IfCondition condition, ImmutableArray<Statement> elseIfBranch) : Statement(condition.Key)
{
    public IfCondition Condition { get; } = condition;
    public ImmutableArray<Statement> ElseIfBranch { get; } = elseIfBranch;

    public override T Accept<T>(IAstVisitor<T> visitor)
    {
        return visitor.VisitElseIfClause(this);
    }
}

public sealed class ElseClause(KeywordStatement elseStmt, ImmutableArray<Statement> elseBranch) : Statement(elseStmt.Key)
{
    public KeywordStatement Else { get; } = elseStmt;
    public ImmutableArray<Statement> ElseBranch { get; } = elseBranch;

    public override T Accept<T>(IAstVisitor<T> visitor)
    {
        return visitor.VisitElseClause(this);
    }
}

// For语句
public sealed class ForStatement(ForExpr forExpr, ImmutableArray<Statement> body, KeywordStatement nextStmt) : Statement(forExpr.Key)
{
    public ForExpr ForExpr { get; } = forExpr;
    public ImmutableArray<Statement> Body { get; } = body;

    public KeywordStatement NextStmt { get; } = nextStmt;

    public override T Accept<T>(IAstVisitor<T> visitor)
    {
        return visitor.VisitForStat(this);
    }
}

public sealed class ForExpr(Token forToken, VariableExpression loopVariable, Expression startValue, Expression endValue,
                  bool isInfinite, int stepValue = 1) : Statement(forToken)
{
    public VariableExpression LoopVariable { get; } = loopVariable;
    public Expression StartValue { get; } = startValue;
    public Expression EndValue { get; } = endValue;  // LoopCount if StartValue is null
    public int StepValue { get; } = stepValue;
    public bool IsInfinite { get; } = isInfinite;
    public override T Accept<T>(IAstVisitor<T> visitor)
    {
        throw new NotImplementedException();
    }
}

// Break语句
public sealed class BreakStatement(Token keyword, uint circle = 1) : Statement(keyword)
{
    public uint Circle { get; } = circle;

    public override T Accept<T>(IAstVisitor<T> visitor)
    {
        return visitor.VisitBreak(this);
    }
}

// Continue语句
public sealed class ContinueStatement(Token keyword) : Statement(keyword)
{
    public override T Accept<T>(IAstVisitor<T> visitor)
    {
        return visitor.VisitContinue(this);
    }
}

// 函数定义语句
public sealed class FunctionDefinitionStatement(FuncDeclare funcdecl, ImmutableArray<Statement> body, KeywordStatement endfunc) : Member(funcdecl.Key)
{
    public FuncDeclare FuncDecl { get; } = funcdecl;
    public ImmutableArray<Statement> Body { get; } = body;
    public KeywordStatement EndFunc { get; } = endfunc;

    public override T Accept<T>(IAstVisitor<T> visitor)
    {
        return visitor.VisitFunctionDefinition(this);
    }
}

public sealed class FuncDeclare(Token keyword, Token ident, ImmutableArray<VariableExpression> parameters) : Statement(keyword)
{
    public Token NameIdent { get; } = ident;
    public ImmutableArray<VariableExpression> Parameters { get; } = parameters;

    public override T Accept<T>(IAstVisitor<T> visitor)
    {
        throw new NotImplementedException();
    }
}

// Return语句
public sealed class ReturnStatement(Token keyword, Expression value = null) : Statement(keyword)
{
    public Expression Value { get; } = value;

    public override T Accept<T>(IAstVisitor<T> visitor)
    {
        return visitor.VisitReturn(this);
    }
}


// 函数调用表达式
public sealed class CallExpression(Token keyword, string functionName, ImmutableArray<Expression> arguments) : Statement(keyword)
{
    public string FunctionName { get; } = functionName;
    public ImmutableArray<Expression> Arguments { get; } = arguments;

    public override T Accept<T>(IAstVisitor<T> visitor)
    {
        return visitor.VisitCall(this);
    }
}


public abstract class GamePadStatement(ImmutableArray<Token> keywords, uint duration) : Statement(keywords.First())
{
    public uint Duration { get; } = duration;

    public ImmutableArray<Token> Keywords { get; } = keywords;

    public string KeyName { get; } = string.Join("+", keywords);

    public override T Accept<T>(IAstVisitor<T> visitor)
    {
        return visitor.VisitKey(this);
    }
}

public sealed class ButtonStatement(ImmutableArray<Token> keywords, uint duration = 50) : GamePadStatement(keywords, duration) { }

public sealed class ButtonStStatement(ImmutableArray<Token> keywords, bool isDown) : GamePadStatement(keywords, 0)
{
    public bool IsDown { get; } = isDown;
}

public sealed class StickStatement(ImmutableArray<Token> keywords, string state, bool reset, uint duration = 50) : GamePadStatement(keywords, duration)
{
    public string Direction { get; } = state;
    public bool IsReset { get; } = reset;
}
