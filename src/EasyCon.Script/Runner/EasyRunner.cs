using EasyCon.Script.Parsing;
using EasyScript;
using System.CodeDom.Compiler;
using System.Collections.Immutable;

namespace EasyCon.Script.Runner;

public sealed class EasyRunner : IRunner
{
    ImmutableArray<Statement> statements = [];
    Dictionary<string, FuncStmt> _funcTables = [];

    public bool HasKeyAction() => statements.SelectMany(st =>
    {
        return st switch
        {
            IfBlock bst => bst.Statements,
            ForBlock bst => bst.Statements,
            FuncDeclBlock bst => bst.Statements,
            _ => [st],
        };
    }).OfType<KeyAction>().ToList().Count != 0;

    public void Init(string code, IEnumerable<ExternalVariable> extVars)
    {
        statements = new Parser(extVars).Parse(code).Members;
        statements.OfType<FuncDeclBlock>().ToList().ForEach(f => { _funcTables[f.Declare.Name] = f.Declare; });
    }

    public void Run(IOutputAdapter output, ICGamePad pad, CancellationToken token)
    {
        //var _processor = new Processor(_funcTables)
        //{
        //    Output = output,
        //    GamePad = pad,
        //};
        //while (!token.IsCancellationRequested && _processor.PC < statements.Length)
        //{
        //    var cmd = statements[_processor.PC];
        //    _processor.PC++;
        //    cmd.Exec(_processor);
        //    Thread.Sleep(1); // opt cpu time
        //}
    }

    public string ToCode()
    {
        using var writer = new StringWriter();
        using var printer = new IndentedTextWriter(writer, "    ");
        foreach(var statement in statements)
        {
            statement.WriteTo(printer);
        }
        return writer.ToString().Trim();
    }
}
