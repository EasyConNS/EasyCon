using VBF.Compilers.Scanners;
using System.Collections.ObjectModel;

namespace ECP.Ast;

public class FuncDecl : Statement
{
    public FuncDecl(LexemeValue func, IList<Statement> statements)
    {
        Name = func.Content;
        if (statements == null)
        {
            Statements = new ReadOnlyCollection<Statement>(new List<Statement>());
            return;
        }
        Statements = new ReadOnlyCollection<Statement>(statements);
    }

    public string Name { get; init; }
    public ReadOnlyCollection<Statement> Statements { get; init; }

    public override T Accept<T>(IAstVisitor<T> visitor)
    {
        return visitor.VisitFunction(this);
    }
}