namespace EasyCon.Script.Syntax;

enum StatementKind
{
    Empty,
    // key action
    CommonAction,
    // Block statements
    ForBlock,
    WhileBlock,
    UntilBlock,
    IfBlock,
    FuncDeclBlock,
    StructDeclBlock,
    // Statement keywords
    Import,
    FuncDecl,
    EndFuncStmt,
    ReturnStmt,
    IfStmt,
    ElseIf,
    Else,
    EndIf,
    ForStmt,
    Next,
    Break,
    Continue,
    WhileStmt,
    UntilStmt,
    EndBlock,
    ExternFuncDecl,
    StructDecl,
    StructField,
}