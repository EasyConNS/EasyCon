namespace EasyScript;

// AST节点基类
public abstract class ASTNode
{
    public int Line { get; set; }
    public int Column { get; set; }
    public abstract T Accept<T>(IAstVisitor<T> visitor);
}

public class Program(List<Statement> statements) : ASTNode
{
    public List<Statement> Statements = statements;

    public override T Accept<T>(IAstVisitor<T> visitor)
    {
        return visitor.VisitProgram(this);
    }
}

// 表达式节点
public abstract class Expression : ASTNode { }

// 字面量表达式
public class LiteralExpression : Expression
{
    public object Value { get; }

    public LiteralExpression(object value)
    {
        // 在构造时验证数值范围
        if (value is string strValue)
        {
            Value = strValue;
        }
        else if (value is int intValue)
        {
            if (intValue < short.MinValue || intValue > short.MaxValue)
            {
                throw new Exception($"整数超出范围 ({short.MinValue} - {short.MaxValue})");
            }
            Value = (int)intValue;
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
public class VariableExpression : Expression
{
    public string Name { get; }
    public bool IsConstant { get; }
    public bool IsSpecial { get; }

    public VariableExpression(string name, bool isConstant, bool isSpecial)
    {
        Name = name;
        IsConstant = isConstant;
        IsSpecial = isSpecial;
    }
    public override T Accept<T>(IAstVisitor<T> visitor)
    {
        return visitor.VisitVariable(this);
    }
}

// 二元运算表达式
public class BinaryExpression : Expression
{
    public Expression Left { get; }
    public TokenType Operator { get; }
    public Expression Right { get; }

    public BinaryExpression(Expression left, TokenType op, Expression right)
    {
        Left = left;
        Operator = op;
        Right = right;
    }

    public override T Accept<T>(IAstVisitor<T> visitor)
    {
        return visitor.VisitBinaryOp(this);
    }
}

// 条件表达式
public class ConditionExpression : Expression
{
    public Expression Left { get; }
    public TokenType Operator { get; }
    public Expression Right { get; }

    public ConditionExpression(Expression left, TokenType op, Expression right)
    {
        Left = left;
        Operator = op;
        Right = right;
    }

    public override T Accept<T>(IAstVisitor<T> visitor)
    {
        return visitor.VisitCondition(this);
    }
}

// 语句节点
public abstract class Statement : ASTNode
{
    public List<string> LeadingTrivia = [];
    public List<string> TrailingTrivia = [];
}

// 赋值语句
public class AssignmentStatement : Statement
{
    public string VariableName { get; }
    public bool IsConstant { get; }
    public TokenType AssignmentType { get; }
    public Expression Value { get; }

    public AssignmentStatement(string variableName, bool isConstant, TokenType assignmentType, Expression value)
    {
        VariableName = variableName;
        IsConstant = isConstant;
        AssignmentType = assignmentType;
        Value = value;
    }
    public override T Accept<T>(IAstVisitor<T> visitor)
    {
        return visitor.VisitAssignmentStat(this);
    }
}

// If语句
public class IfStatement : Statement
{
    public Expression Condition { get; }
    public List<Statement> ThenBranch { get; }

    public List<ElseIfClause> ElseIfBranch { get; }
    public ElseClause? ElseClause { get; }

    public IfStatement(Expression condition, List<Statement> thenBranch, List<ElseIfClause> elseIfBranch, ElseClause? elseClause)
    {
        Condition = condition;
        ThenBranch = thenBranch;
        ElseIfBranch = elseIfBranch;
        ElseClause = elseClause;
    }

    public override T Accept<T>(IAstVisitor<T> visitor)
    {
        return visitor.VisitIfStat(this);
    }
}

public class ElseIfClause : Statement
{
    public Expression Condition { get; }
    public List<Statement> ElseIfBranch { get; }

    public ElseIfClause(Expression condition, List<Statement> elseIfBranch)
    {
        Condition = condition;
        ElseIfBranch = elseIfBranch;
    }

    public override T Accept<T>(IAstVisitor<T> visitor)
    {
        return visitor.VisitElseIfClause(this);
    }
}

public class ElseClause : Statement
{
    public List<Statement> ElseBranch { get; }

    public ElseClause(List<Statement> elseBranch)
    {
        ElseBranch = elseBranch;
    }

    public override T Accept<T>(IAstVisitor<T> visitor)
    {
        return visitor.VisitElseClause(this);
    }
}

// For语句
public class ForStatement : Statement
{
    public string LoopVariable { get; }  // 循环变量名（对于范围循环）
    public VariableExpression StartValue { get; } // 起始值
    public VariableExpression EndValue { get; }   // 结束值
    public int StepValue { get; }  // 步长
    public Expression? LoopCount { get; }  // 循环次数（对于计数循环）
    public bool IsInfinite { get; }       // 是否无限循环
    public List<Statement> Body { get; }  // 循环体

    public ForStatement(string loopVariable, VariableExpression startValue, VariableExpression endValue,
                      Expression? loopCount, bool isInfinite,
                      List<Statement> body, int stepValue = 1)
    {
        LoopVariable = loopVariable;
        StartValue = startValue;
        EndValue = endValue;
        StepValue = stepValue;
        LoopCount = loopCount;
        IsInfinite = isInfinite;
        Body = body;
    }
    public override T Accept<T>(IAstVisitor<T> visitor)
    {
        return visitor.VisitForStat(this);
    }
}

// Break语句
public class BreakStatement : Statement
{
    public uint Circle { get; }
    public BreakStatement(uint circle = 1)
    {
        Circle = circle;
    }
    public override T Accept<T>(IAstVisitor<T> visitor)
    {
        return visitor.VisitBreak(this);
    }
}

// Continue语句
public class ContinueStatement : Statement
{
    public uint Circle { get; }
    public ContinueStatement(uint circle = 1)
    {
        Circle = circle;
    }
    public override T Accept<T>(IAstVisitor<T> visitor)
    {
        return visitor.VisitContinue(this);
    }
}

// 函数定义语句
public class FunctionDefinitionStatement : Statement
{
    public string FunctionName { get; }
    public List<string> Parameters { get; }
    public List<Statement> Body { get; }

    public FunctionDefinitionStatement(string functionName, List<string> parameters, List<Statement> body)
    {
        FunctionName = functionName;
        Parameters = parameters;
        Body = body;
    }
    public override T Accept<T>(IAstVisitor<T> visitor)
    {
        return visitor.VisitFunctionDefinition(this);
    }
}

// Return语句
public class ReturnStatement : Statement
{
    public Expression Value { get; }

    public ReturnStatement(Expression value)
    {
        Value = value;
    }

    public override T Accept<T>(IAstVisitor<T> visitor)
    {
        return visitor.VisitReturn(this);
    }
}


// 函数调用表达式
public class CallExpression : Statement
{
    public string FunctionName { get; }
    public List<Expression> Arguments { get; }

    public CallExpression(string functionName)
    {
        FunctionName = functionName;
        Arguments = [];
    }

    public CallExpression(string functionName, List<Expression> arguments)
    {
        FunctionName = functionName;
        Arguments = arguments;
    }
    public override T Accept<T>(IAstVisitor<T> visitor)
    {
        return visitor.VisitCall(this);
    }
}


public class KeyStatement : Statement
{
    public string KeyName { get; }

    public KeyStatement(string keyName, uint duration = 50)
    {
        KeyName = keyName;
    }

    public override T Accept<T>(IAstVisitor<T> visitor)
    {
        return visitor.VisitKey(this);
    }
}