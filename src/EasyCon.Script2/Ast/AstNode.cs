using EasyCon.Script2.Syntax;
using System.Collections.Immutable;

namespace EasyCon.Script2.Ast;

// AST节点基类
public abstract class ASTNode(Token key)
{
    private Token Key = key;
    public int Line => key.Line;
    public abstract T Accept<T>(IAstVisitor<T> visitor);
}

public sealed class MainProgram(ImmutableArray<Statement> statements, Token endOfFileToken) : ASTNode(endOfFileToken)
{
    public ImmutableArray<Statement> Statements = statements;
    public Token EndOfFileToken = endOfFileToken;

    public override T Accept<T>(IAstVisitor<T> visitor)
    {
        return visitor.VisitProgram(this);
    }
}

// 表达式节点
public abstract class Expression(Token key) : ASTNode(key) { }

public sealed class TriviaNode(Token trivia) : Expression(trivia)
{
    public string Text => trivia.Value;

    public override T Accept<T>(IAstVisitor<T> visitor)
    {
        return visitor.VisitTrivia(this);
    }
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

    public override T Accept<T>(IAstVisitor<T> visitor)
    {
        return visitor.VisitLiteral(this);
    }
}

// 变量表达式
public sealed class VariableExpression(Token keyword, bool isConstant, bool isSpecial) : Expression(keyword)
{
    public string Name { get; } = keyword.Value;
    public bool IsConstant { get; } = isConstant;
    public bool IsSpecial { get; } = isSpecial;

    public override T Accept<T>(IAstVisitor<T> visitor)
    {
        return visitor.VisitVariable(this);
    }
}

public sealed class IndexExpression(Token keyword, ImmutableArray<Expression> items, Token rb) : Expression(keyword)
{
    public ImmutableArray<Expression> Items { get; } = items;

    public override T Accept<T>(IAstVisitor<T> visitor)
    {
        return visitor.VisitIndexExpr(this);
    }
}

public sealed class UnaryExpression(Token op, Expression right) : Expression(op)
{
    public TokenType Operator { get; } = op.Type;
    public Expression Right { get; } = right;

    public override T Accept<T>(IAstVisitor<T> visitor)
    {
        //return visitor.VisitBinaryOp(this);
        throw new NotImplementedException();
    }
}

// 二元运算表达式
public sealed class BinaryExpression(Expression left, Token op, Expression right) : Expression(op)
{
    public Expression Left { get; } = left;
    public TokenType Operator { get; } = op.Type;
    public Expression Right { get; } = right;

    public override T Accept<T>(IAstVisitor<T> visitor)
    {
        return visitor.VisitBinaryOp(this);
    }
}

// 条件表达式
public sealed class ConditionExpression(Expression left, Token op, Expression right) : Expression(op)
{
    public Expression Left { get; } = left;
    public TokenType Operator { get; } = op.Type;
    public Expression Right { get; } = right;

    public override T Accept<T>(IAstVisitor<T> visitor)
    {
        return visitor.VisitCondition(this);
    }
}

// 语句节点
public abstract class Statement(Token key) : ASTNode(key)
{
    public readonly List<TriviaNode> LeadingTrivia = [];
    public readonly List<TriviaNode> TrailingTrivia = [];
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
public sealed class IfStatement(Token ifToken, Expression condition, ImmutableArray<Statement> thenBranch, ImmutableArray<ElseIfClause> elseIfBranch, ElseClause? elseClause) : Statement(ifToken)
{
    public Expression Condition { get; } = condition;
    public ImmutableArray<Statement> ThenBranch { get; } = thenBranch;

    public ImmutableArray<ElseIfClause> ElseIfBranch { get; } = elseIfBranch;
    public ElseClause? ElseClause { get; } = elseClause;

    public override T Accept<T>(IAstVisitor<T> visitor)
    {
        return visitor.VisitIfStat(this);
    }
}

public sealed class ElseIfClause(Token elifToken, Expression condition, ImmutableArray<Statement> elseIfBranch) : Statement(elifToken)
{
    public Expression Condition { get; } = condition;
    public ImmutableArray<Statement> ElseIfBranch { get; } = elseIfBranch;

    public override T Accept<T>(IAstVisitor<T> visitor)
    {
        return visitor.VisitElseIfClause(this);
    }
}

public sealed class ElseClause(Token keyword, ImmutableArray<Statement> elseBranch) : Statement(keyword)
{
    public ImmutableArray<Statement> ElseBranch { get; } = elseBranch;

    public override T Accept<T>(IAstVisitor<T> visitor)
    {
        return visitor.VisitElseClause(this);
    }
}

// For语句
public sealed class ForStatement(Token forToken, VariableExpression loopVariable, Expression startValue, Expression endValue,
                  Expression? loopCount, bool isInfinite,
                  ImmutableArray<Statement> body, int stepValue = 1) : Statement(forToken)
{
    public VariableExpression LoopVariable { get; } = loopVariable;
    public Expression StartValue { get; } = startValue;
    public Expression EndValue { get; } = endValue;
    public int StepValue { get; } = stepValue;
    public Expression? LoopCount { get; } = loopCount;
    public bool IsInfinite { get; } = isInfinite;
    public ImmutableArray<Statement> Body { get; } = body;

    public override T Accept<T>(IAstVisitor<T> visitor)
    {
        return visitor.VisitForStat(this);
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
public sealed class FunctionDefinitionStatement(Token keyword, Token ident, ImmutableArray<string> parameters, ImmutableArray<Statement> body) : Statement(keyword)
{
    public Token FunctionIdent { get; } = ident;
    public ImmutableArray<string> Parameters { get; } = parameters;
    public ImmutableArray<Statement> Body { get; } = body;

    public override T Accept<T>(IAstVisitor<T> visitor)
    {
        return visitor.VisitFunctionDefinition(this);
    }
}

// Return语句
public sealed class ReturnStatement(Token keyword, Expression value) : Statement(keyword)
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


public abstract class KeyStatement(Token keyword, uint duration) : Statement(keyword)
{
    public uint Duration { get; } = duration;

    public string KeyName { get; } = keyword.Value;

    public override T Accept<T>(IAstVisitor<T> visitor)
    {
        return visitor.VisitKey(this);
    }
}

public sealed class ButtonStatement(Token keyword, uint duration = 50) : KeyStatement(keyword, duration) { }

public sealed class ButtonStStatement(Token keyword, bool isDown) : KeyStatement(keyword, 0)
{
    public bool IsDown { get; } = isDown;
}

public sealed class StickStatement(Token keyword, string state, bool reset, uint duration = 50) : KeyStatement(keyword, duration)
{
    public string Direction { get; } = state;
    public bool IsReset { get; } = reset;
}
