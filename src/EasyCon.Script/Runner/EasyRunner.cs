using EasyCon.Script.Binding;
using EasyCon.Script.Parsing;
using EasyScript;
using System.CodeDom.Compiler;

namespace EasyCon.Script.Runner;

public sealed class EasyRunner : IRunner
{
    CompicationUnit unit;
    BoundProgram prog;

    public bool HasKeyAction => prog.KeyAction;

    public void Init(string code, IEnumerable<ExternalVariable> extVars)
    {
        var parser = new Parser(extVars);
        unit = parser.ParseUnit(code);
        prog = Binder.BindProgram(parser.Parse(unit));
    }

    public void Run(IOutputAdapter output, ICGamePad pad, CancellationToken token)
    {
        var evaluator = new Evaluator(prog, token)
        {
            Output = output,
            GamePad = pad,
        };
        evaluator.Evaluate();
    }

    public string ToCode()
    {
        using var writer = new StringWriter();
        using var printer = new IndentedTextWriter(writer, "    ");
        foreach(var statement in unit.Members)
        {
            statement.WriteTo(printer);
        }
        return writer.ToString().Trim();
    }
}
