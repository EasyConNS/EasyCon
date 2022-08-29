namespace ECP.Ast;

public interface IAstVisitor<T>
{
    T VisitProgram(Program ast);
    T VisitBlock(Block ast);
    T VisitNumber(Number ast);
    T VisitConstDefine(ConstDefine ast);
    T VisitMovStatement(MovStatement ast);
    T VisitWaitExp(WaitExp ast);
    T VisitIfElse(IfElse ast);
    T VisitBinary(Binary ast);
    T VisitForState(ForStatement ast);
    T VisitForWhile(ForWhile ast);
    T VisitButtonAction(ButtonAction ast);
    T VisitFuncState(FuncState ast);
    T VisitBuildinState(BuildinState ast);
}