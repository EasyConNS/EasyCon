namespace ECP.Ast;

public interface IAstVisitor<T>
{
    T VisitProgram(Program ast);
    T VisitNumber(Number ast);
    T VisitConstDefine(ConstDefine ast);
    T VisitMovStatement(MovStatement ast);
    T VisitWaitExp(WaitExp ast);
    T VisitPrintState(PrintState ast);
    T VisitIfElse(IfElse ast);
    T VisitBinary(Binary ast);
    T VisitForState(ForStatement ast);
    T VisitForWhile(ForWhile ast);
    T VisitButtonAction(ButtonAction ast);
}