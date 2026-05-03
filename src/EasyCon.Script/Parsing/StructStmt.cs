using System.Collections.Immutable;

namespace EasyCon.Script.Syntax;

class StructStmt(Token syntax, string name) : StartBlockStmt(syntax)
{
    public override StatementKind Kind => StatementKind.StructStmt;
    public readonly string Name = name;

    protected override string _GetString() => $"STRUCT {Name}";
}

class StructFieldStmt(Token syntax, string name, string typeName) : Statement(syntax)
{
    public override StatementKind Kind => StatementKind.StructField;
    public readonly string Name = name;
    public readonly string TypeName = typeName;

    protected override string _GetString() => $"    ${Name}:{TypeName}";
}

class StructDeclBlock(StructStmt header, ImmutableArray<StructFieldStmt> fields, EndBlockStmt end) : Statement(header.Syntax)
{
    public override StatementKind Kind => StatementKind.StructDeclBlock;
    public readonly StructStmt Header = header;
    public readonly ImmutableArray<StructFieldStmt> Fields = fields;
    public readonly EndBlockStmt End = end;

    protected override string _GetString()
    {
        throw new NotImplementedException();
    }
}