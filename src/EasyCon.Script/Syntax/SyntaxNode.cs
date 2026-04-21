using System.Collections.Immutable;

namespace EasyCon.Script.Syntax;

// AST节点基类
public abstract class AstNode(Token syntax)
{
    public Token Syntax { get; } = syntax;
    public int Line => Syntax.Location.StartLine + 1;

    public virtual T Accept<T>(IAstVisitor<T> visitor) { throw new NotImplementedException(); }

    // public readonly List<TriviaNode> LeadingTrivia = [];
    // public readonly List<TriviaNode> TrailingTrivia = [];
}


public abstract class Member(Token key) : AstNode(key) { }

// 表达式节点
public abstract class Expression(Token key) : AstNode(key) { }

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
        else if (value is bool)
            Value = (bool)value;
        else if (value is uint intValue)
        {
            Value = intValue;
        }
        else
            throw new Exception($"Unexpected literal '{value}' of type {value.GetType()}");
    }
}