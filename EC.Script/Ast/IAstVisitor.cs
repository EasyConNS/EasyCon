namespace ECP.Ast;

public interface IAstVisitor<T>
{
    T VisitProgram(Program ast);
    T VisitBlock(Block ast);
    T VisitNumber(Number ast);
    T VisitBooleanLiteral(BooleanLiteral ast);
    T VisitVariable(Variable ast);
    T VisitConstDefine(ConstDefine ast);
    T VisitNot(Not ast);
    T VisitBinary(Binary ast);
    T VisitAssign(Assign ast);
    T VisitOpAssign(OpAssign ast);
    T VisitCall(Call ast);
    T VisitIfElse(IfElse ast);
    T VisitElseIf(ElseIf ast);
    T VisitForCondition(ForStatement ast);
    T VisitForWhile(ForWhile ast);
    T VisitLoopControl(LoopControl ast);
    T VisitButtonAction(ButtonAction ast);
    T VisitStickAction(StickAction ast);
    T VisitFunction(Function ast);
}