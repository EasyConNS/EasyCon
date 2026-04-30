using System.Collections.Immutable;

namespace EasyCon.Script.Syntax;

class StructStmt(Token syntax, string name) : StartBlockStmt(syntax)
{
    public override StatementKind Kind => StatementKind.StructStmt;
    public readonly string Name = name;

    protected override string _GetString() => $"STRUCT {Name}";
}

class StructFieldStmt(Token syntax, string name, string typeName, int arrayCount = 1) : Statement(syntax)
{
    public override StatementKind Kind => StatementKind.StructField;
    public readonly string Name = name;
    public readonly string TypeName = typeName;
    public readonly int ArrayCount = arrayCount;

    protected override string _GetString()
        => ArrayCount > 1 ? $"    ${Name}:{TypeName}[{ArrayCount}]" : $"    ${Name}:{TypeName}";
}

class StructDeclBlock(StructStmt header, ImmutableArray<StructFieldStmt> fields, EndBlockStmt end) : Statement(header.Syntax)
{
    public override StatementKind Kind => StatementKind.StructDeclBlock;
    public readonly StructStmt Header = header;
    public readonly ImmutableArray<StructFieldStmt> Fields = fields;
    public readonly EndBlockStmt End = end;

    protected override string _GetString()
    {
        var lines = new List<string> { Header.GetCodeText() };
        foreach (var f in Fields) lines.Add(f.GetCodeText());
        lines.Add(End.GetCodeText());
        return string.Join("\n", lines);
    }
}