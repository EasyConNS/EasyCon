#if false
using EasyScript;

var text = File.ReadAllText("../Script/光速过帧v1.4精准版.txt");

var lexer = new Lexer(text);
var parser = new Parser(lexer);

var visitor = new SimpleVisitor();
visitor.VisitProgram(parser.ParseProgram());

Console.WriteLine("done");
#endif