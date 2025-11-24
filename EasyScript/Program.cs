using EasyScript;

var visitor = new SimpleVisitor();

var text = File.ReadAllText("../Script/光速过帧v1.4精准版.txt");

var lexer = new Lexer(text);
var parser = new Parser(lexer);

foreach(var s in parser.Parse())
{
    visitor.Visit(s);
}
Console.WriteLine();
Console.WriteLine("done");
