namespace EC.Script.Ast;

internal interface IAstVisitor<T>
{
    T VisitProgram(MainProgram ast);

    T VisitConstDecl(ConstDecl ast);

    T VisitAssign(AssignExpr ast);

    T VisitAugAssign(AugAssignExpr ast);

    T VisitCall(CallExpr ast);

    T VisitBinary(BinaryExpr ast);
    T VisitUnary(UnaryExpr ast);
}