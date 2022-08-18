namespace ECP.Ast;

public interface IAstVisitor<T>
{
    T VisitProgram(Program ast);
}