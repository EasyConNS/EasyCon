using System.Collections.Immutable;

namespace EasyCon.Script.Syntax;

internal sealed class ExternFuncStmt(Token externToken, Token identifier, ImmutableArray<ParameterSyntax> parameters, TypeClauseSyntax returnType, Token fromToken, Token libraryPath) : Statement(externToken)
{
    public override StatementKind Kind => StatementKind.ExternFuncDeclaration;
    public readonly Token Identifier = identifier;
    public string Name => Identifier.Value;
    public readonly ImmutableArray<ParameterSyntax> Parameters = parameters;
    public readonly TypeClauseSyntax ReturnType = returnType;
    public readonly Token FromToken = fromToken;
    public readonly Token LibraryPath = libraryPath;
    public string Library => LibraryPath.STRTrimQ();

    protected override string _GetString()
    {
        var parm = string.Join(", ", Parameters.Select(p => p.ToString()));
        parm = Parameters.Length == 0 ? "()" : $"({parm})";
        return $"EXTERN FUNC {Name}{parm}:{ReturnType.Identifier.Value.ToUpper()} FROM \"{Library}\"";
    }
}