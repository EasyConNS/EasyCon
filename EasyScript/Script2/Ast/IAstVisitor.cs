namespace ECP.Ast;

public interface IAstVisitor<T>
{
    T VisitProgram(Program ast);
    T VisitConstDefine(ConstDefine ast);
    T VisitIfElse(IfElse ast);
    T VisitForWhile(ForWhile ast);
}