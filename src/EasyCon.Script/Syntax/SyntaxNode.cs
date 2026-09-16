using System.Collections.Immutable;

namespace EasyCon.Script.Syntax;

// AST节点基类
public abstract class AstNode(Token syntax)
{
    public Token Syntax { get; } = syntax;

    /// <summary>起始源码行（1 基）；合成节点（无 SourceText，如 EmptyStmt）返回 0 表示未知。</summary>
    public int Line => Syntax?.Location?.Text == null ? 0 : Syntax.Location.StartLine + 1;

    internal virtual T Accept<T>(IAstVisitor<T> visitor) { throw new NotImplementedException(); }
}


public abstract class Member(Token key) : AstNode(key) { }