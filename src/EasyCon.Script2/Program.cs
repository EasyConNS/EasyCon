using EC.Script;
using EC.Script.Ast;
using EC.Script.Syntax;

var syntaxTree = SyntaxTree.Load("../../Script/test.txt");

if(syntaxTree.Diagnostics.Length > 0)
{
    foreach(var diagnostic in syntaxTree.Diagnostics)
    {
        Console.WriteLine(diagnostic);
    }
}
else
{
    var visitor = new SimpleVisitor();
    visitor.VisitProgram(syntaxTree.Root);
}

Console.WriteLine("done");