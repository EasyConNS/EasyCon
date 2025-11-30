using EasyCon.Script2;
using EasyCon.Script2.Syntax;

var syntaxTree = SyntaxTree.Parse("_中文 = \"123\" \r\n if 3 \r\n endif");

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