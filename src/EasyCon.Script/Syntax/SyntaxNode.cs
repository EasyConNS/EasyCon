using System.Collections.Immutable;

namespace EasyCon.Script.Syntax;

// AST节点基类
public abstract class AstNode(Token syntax)
{
    public Token Syntax { get; } = syntax;
    public int Line => Syntax.Location.StartLine + 1;

    internal virtual T Accept<T>(IAstVisitor<T> visitor) { throw new NotImplementedException(); }
}


public abstract class Member(Token key) : AstNode(key) { }