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
    T VisitElseIf(ElseIf ast);
    T VisitBinary(Binary ast);
    T VisitOpAssign(OpAssign ast);
    T VisitForState(ForStatement ast);
    T VisitForWhile(ForWhile ast);
    T VisitLoopControl(LoopControl ast);
    T VisitButtonAction(ButtonAction ast);
    T VisitStickAction(StickAction ast);
    T VisitFuncState(FuncState ast);
    T VisitBuildinState(BuildinState ast);
}