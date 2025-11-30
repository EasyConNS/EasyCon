using EasyCon.Script2.Syntax;
using System.Collections.Immutable;

namespace EasyCon.Script2.Ast;

// AST节点基类
public abstract class ASTNode
{
    public int Line { get; set; }
    public int Column { get; set; }
    public abstract T Accept<T>(IAstVisitor<T> visitor);
}

public sealed class MainProgram(ImmutableArray<Statement> statements) : ASTNode
{
    public ImmutableArray<Statement> Statements = statements;

    public override T Accept<T>(IAstVisitor<T> visitor)
    {
        return visitor.VisitProgram(this);
    }
}

// 表达式节点
public abstract class Expression : ASTNode { }

public sealed class TriviaNode(Token trivia) : Expression
{
    public string Text => trivia.Value;
    public int Line => trivia.Line;

    public override T Accept<T>(IAstVisitor<T> visitor)
    {
        return visitor.VisitTrivia(this);
    }
}

// 字面量表达式
public sealed class LiteralExpression : Expression
{
    public object Value { get; }

    public LiteralExpression(object value)
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
public sealed class VariableExpression(string name, bool isConstant, bool isSpecial) : Expression
{
    public string Name { get; } = name;
    public bool IsConstant { get; } = isConstant;
    public bool IsSpecial { get; } = isSpecial;

    public override T Accept<T>(IAstVisitor<T> visitor)
    {
        return visitor.VisitVariable(this);
    }
}

// 二元运算表达式
public sealed class BinaryExpression(Expression left, TokenType op, Expression right) : Expression
{
    public Expression Left { get; } = left;
    public TokenType Operator { get; } = op;
    public Expression Right { get; } = right;

    public override T Accept<T>(IAstVisitor<T> visitor)
    {
        return visitor.VisitBinaryOp(this);
    }
}

// 条件表达式
public sealed class ConditionExpression(Expression left, TokenType op, Expression right) : Expression
{
    public Expression Left { get; } = left;
    public TokenType Operator { get; } = op;
    public Expression Right { get; } = right;

    public override T Accept<T>(IAstVisitor<T> visitor)
    {
        return visitor.VisitCondition(this);
    }
}

// 语句节点
public abstract class Statement : ASTNode
{
    public readonly List<TriviaNode> LeadingTrivia = [];
    public readonly List<TriviaNode> TrailingTrivia = [];
}

// 赋值语句
public sealed class AssignmentStatement(VariableExpression? Variablee, Token assignment, Expression expression) : Statement
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
public sealed class IfStatement(Expression condition, ImmutableArray<Statement> thenBranch, ImmutableArray<ElseIfClause> elseIfBranch, ElseClause? elseClause) : Statement
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

public sealed class ElseIfClause(Expression condition, ImmutableArray<Statement> elseIfBranch) : Statement
{
    public Expression Condition { get; } = condition;
    public ImmutableArray<Statement> ElseIfBranch { get; } = elseIfBranch;

    public override T Accept<T>(IAstVisitor<T> visitor)
    {
        return visitor.VisitElseIfClause(this);
    }
}

public sealed class ElseClause(ImmutableArray<Statement> elseBranch) : Statement
{
    public ImmutableArray<Statement> ElseBranch { get; } = elseBranch;

    public override T Accept<T>(IAstVisitor<T> visitor)
    {
        return visitor.VisitElseClause(this);
    }
}

// For语句
public sealed class ForStatement(string loopVariable, Expression startValue, Expression endValue,
                  Expression? loopCount, bool isInfinite,
                  ImmutableArray<Statement> body, int stepValue = 1) : Statement
{
    public string LoopVariable { get; } = loopVariable;
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
public sealed class BreakStatement(uint circle = 1) : Statement
{
    public uint Circle { get; } = circle;

    public override T Accept<T>(IAstVisitor<T> visitor)
    {
        return visitor.VisitBreak(this);
    }
}

// Continue语句
public sealed class ContinueStatement : Statement
{
    public override T Accept<T>(IAstVisitor<T> visitor)
    {
        return visitor.VisitContinue(this);
    }
}

// 函数定义语句
public sealed class FunctionDefinitionStatement(string functionName, List<string> parameters, ImmutableArray<Statement> body) : Statement
{
    public string FunctionName { get; } = functionName;
    public List<string> Parameters { get; } = parameters;
    public ImmutableArray<Statement> Body { get; } = body;

    public override T Accept<T>(IAstVisitor<T> visitor)
    {
        return visitor.VisitFunctionDefinition(this);
    }
}

// Return语句
public sealed class ReturnStatement(Expression value) : Statement
{
    public Expression Value { get; } = value;

    public override T Accept<T>(IAstVisitor<T> visitor)
    {
        return visitor.VisitReturn(this);
    }
}


// 函数调用表达式
public sealed class CallExpression(string functionName, ImmutableArray<Expression> arguments) : Statement
{
    public string FunctionName { get; } = functionName;
    public ImmutableArray<Expression> Arguments { get; } = arguments;

    public override T Accept<T>(IAstVisitor<T> visitor)
    {
        return visitor.VisitCall(this);
    }
}


public abstract class KeyStatement(string keyName, uint duration) : Statement
{
    public string KeyName { get; } = keyName;
    public uint Duration { get; } = duration;

    public override T Accept<T>(IAstVisitor<T> visitor)
    {
        return visitor.VisitKey(this);
    }
}

public sealed class ButtonStatement(string keyName, uint duration = 50) : KeyStatement(keyName, duration) { }

public sealed class ButtonStStatement(string keyName, bool isDown) :KeyStatement(keyName, 0)
{
    public bool IsDown { get; } = isDown;
}

public sealed class StickStatement(string keyName, string state, bool reset, uint duration = 50) :KeyStatement(keyName, duration)
{
    public string Direction { get; } = state;
    public bool IsReset { get; } = reset;
}
