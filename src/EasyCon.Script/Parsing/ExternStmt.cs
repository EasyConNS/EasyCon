using System.Collections.Immutable;

namespace EasyCon.Script.Syntax;

internal sealed class ExternFuncStmt(Token externToken, Token identifier, ImmutableArray<ParameterSyntax> parameters, TypeClauseSyntax returnType, Token fromToken, Token libraryPath, Token? asToken = null, Token? exportName = null) : Statement(externToken)
{
    public override StatementKind Kind => StatementKind.ExternFuncDeclaration;
    public readonly Token Identifier = identifier;
    public string Name => Identifier.Value;
    public readonly ImmutableArray<ParameterSyntax> Parameters = parameters;
    public readonly TypeClauseSyntax ReturnType = returnType;
    public readonly Token AsToken = asToken!;
    public readonly Token ExportNameToken = exportName!;
    public readonly Token FromToken = fromToken;
    public readonly Token LibraryPath = libraryPath;
    public string Library => LibraryPath.STRTrimQ();
    public string ExportName => ExportNameToken != null ? ExportNameToken.STRTrimQ() : Name;

    protected override string _GetString()
    {
        var parm = string.Join(", ", Parameters.Select(p => p.ToString()));
        parm = Parameters.Length == 0 ? "()" : $"({parm})";
        var asPart = ExportNameToken != null ? $" AS \"{ExportName}\"" : "";
        return $"EXTERN FUNC {Name}{parm}:{ReturnType.TypeName.ToUpper()}{asPart} FROM \"{Library}\"";
    }
}
