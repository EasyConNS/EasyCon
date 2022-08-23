namespace ECP.Ast;

public interface IAstVisitor<T>
{
    T VisitProgram(Program ast);
    T VisitConstDefine(ConstDefine ast);
    T VisitForWhile(ForWhile ast);
}