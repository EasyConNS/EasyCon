using EasyCon.Script.Binding;
using EasyCon.Script.Syntax;
using EasyScript;
using System.CodeDom.Compiler;

namespace EasyCon.Script;

public sealed class Compilation
{
    public bool KeyAction { get; private set; } = false;
    public bool NeedIL { get; private set; } = false;

    private Compilation(bool isScript, Compilation? previous, SyntaxTree syntaxTrees)
    {
        IsScript = isScript;
        Previous = previous;
        SyntaxTrees = syntaxTrees;
    }
    public static Compilation Create(SyntaxTree syntaxTrees)
    {
        return new Compilation(isScript: false, previous: null, syntaxTrees);
    }
    public bool IsScript { get; }
    public Compilation? Previous { get; }
    public SyntaxTree SyntaxTrees { get; }

    private BoundProgram GetProgram()
    {
        var previous = Previous == null ? null : Previous.GetProgram();
        return Binder.BindProgram(SyntaxTrees);
    }

    public EvaluationResult Evaluate(IOutputAdapter output, ICGamePad pad, CancellationToken token)
    {
        //if (SyntaxTrees.Diagnostics.Any())
        //    return new EvaluationResult(GlobalScope.Diagnostics, null);

        var program = GetProgram();
        KeyAction = program.KeyAction;
        NeedIL = program.NeedIL;

        //if (program.Diagnostics.HasErrors())
        //    return new EvaluationResult(program.Diagnostics, null);

        var evaluator = new Evaluator(program, token)
        {
            GamePad = pad,
            Output = output,
        };
        var value = evaluator.Evaluate();

        return new EvaluationResult([], value);
    }

    public string FormatCode()
    {
        using var writer = new StringWriter();
        using var printer = new IndentedTextWriter(writer, "    ");
        foreach (var statement in SyntaxTrees.Root.Members)
        {
            statement.WriteTo(printer);
        }
        return writer.ToString().Trim();
    }
}
