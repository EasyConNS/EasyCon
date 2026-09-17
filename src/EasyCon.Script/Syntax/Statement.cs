using EasyCon.Script.Text;
using System.CodeDom.Compiler;
using System.Collections.Immutable;

namespace EasyCon.Script.Syntax;

abstract class Statement(Token syntax) : AstNode(syntax)
{
    public static readonly EmptyStmt Empty = new();
    public string Comment { get; set; } = string.Empty;

    public virtual StatementKind Kind => StatementKind.CommonAction;

    public TextLocation Location => Syntax.Location;

    protected abstract string _GetString();

    public string GetCodeText()
    {
        return $"{_GetString()}{Comment}";
    }
}

class EmptyStmt() : Statement(null!)
{
    public override StatementKind Kind => StatementKind.Empty;
    protected override string _GetString() => "";
}

abstract class StartBlockStmt(Token syntax) : Statement(syntax)
{ }

class EndBlockStmt(Token syntax) : Statement(syntax)
{
    public override StatementKind Kind => StatementKind.EndBlock;
    protected override string _GetString() => "END";
}

sealed class ImportStmt(Token syntax, Token model, string path = "") : Statement(syntax)
{
    public override StatementKind Kind => StatementKind.Import;
    internal readonly Token Model = model;
    internal readonly string InitPath = path;
    internal string Lib => Model.STRTrimQ();

    // 新增：命名空间别名（可选）
    public Token? Alias { get; init; }

    // 新增：是否导入到全局
    public bool IsGlobal => Alias == null;

    // 新增：命名空间名称
    public string NamespaceName => Alias?.Value ?? "global";

    public string FullFileName => Path.Combine(InitPath, Lib);
    protected override string _GetString() => Alias != null
        ? $"IMPORT \"{Lib}\" AS {Alias.Value}"
        : $"IMPORT \"{Lib}\"";
}

sealed class CompicationUnit(ImmutableArray<Statement> members)
{
    public readonly ImmutableArray<Statement> Members = members;
}