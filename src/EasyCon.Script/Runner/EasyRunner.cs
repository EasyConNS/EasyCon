using EasyCon.Script.Binding;
using EasyCon.Script.Parsing;
using EasyCon.Script2.Text;
using EasyScript;
using System.CodeDom.Compiler;

namespace EasyCon.Script.Runner;

public sealed class EasyRunner : IRunner
{
    CompicationUnit unit;
    BoundProgram prog;

    public bool HasKeyAction => prog.KeyAction;
    public bool NeedILLoad => prog.NeedIL;

    public byte[] Assemble(bool auto = true)
    {
        return new Assembly.Assembler().Assemble(prog, auto);
    }
    public void Init(string code, IEnumerable<ExternalVariable> extVars)
    {
        var sourceText = SourceText.From(code);
        var parser = new Parser(sourceText, extVars);
        prog = Binder.BindProgram(parser.Parse(out unit));
    }
    public void Load(string fileName, IEnumerable<ExternalVariable> extVars)
    {
        var text = File.ReadAllText(fileName);
        var sourceText = SourceText.From(text, fileName);
        var parser = new Parser(sourceText, extVars);
        prog = Binder.BindProgram(parser.Parse(out unit));
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
