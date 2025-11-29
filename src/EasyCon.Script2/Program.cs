using EasyCon.Script2;
using EasyCon.Script2.Syntax;

var syntaxTree = SyntaxTree.Parse("$a *= 1+2");

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