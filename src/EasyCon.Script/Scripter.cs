using EasyScript.Parsing;
using EasyScript.Statements;
using System.Collections.Immutable;
using System.CodeDom.Compiler;

namespace EasyScript;

public class Scripter
{
    readonly Dictionary<string, int> Constants = [];
    readonly Dictionary<string, ExternalVariable> ExtVars = [];

    ImmutableArray<Statement> _statements = [];

    Dictionary<string, FunctionStmt> _funcTables = [];

    public bool HasKeyAction {
        get
        {
            return _statements.OfType<KeyAction>().Any();
        }
    }

    public void Parse(string code, IEnumerable<ExternalVariable> extVars)
    {
        Constants.Clear();
        ExtVars.Clear();
        foreach (var ev in extVars)
            ExtVars[ev.Name] = ev;
        _statements = new Parser(Constants, ExtVars).Parse(code).ToImmutableArray();
        _statements.OfType<FunctionStmt>().ToList().ForEach(f => { _funcTables[f.Label] = f; });
    }

    public void Run(IOutputAdapter output, ICGamePad pad)
    {
        var _processor = new Processor(_funcTables)
        {
            Output = output,
            GamePad = pad,
        };
        while (_processor.PC < _statements.Count())
        {
            var cmd = _statements[_processor.PC];
            _processor.PC++;
            cmd.Exec(_processor);
        }
    }

    public string ToCode()
    {
        using (var writer = new StringWriter())
        using(var printer = new IndentedTextWriter(writer, "  "))
        {
            _statements.ToList().ForEach(u => u.WriteTo(printer));
            return writer.ToString();
        }
    }

    public byte[] Assemble(bool auto = true)
    {
        return new Assembly.Assembler().Assemble(_statements, auto);
    }

    public void Reset()
    {
        _statements = [];
        _funcTables = [];
        ExtVars.Clear();
    }
}
