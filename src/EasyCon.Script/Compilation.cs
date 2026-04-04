using EasyCon.Script.Binding;
using EasyCon.Script.Syntax;
using EasyScript;
using System.CodeDom.Compiler;
using System.Collections.Immutable;

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

    private BoundProgram GetProgram(IEnumerable<string>? extVars)
    {
        var previous = Previous == null ? null : Previous.GetProgram(extVars);
        return Binder.BindProgram(SyntaxTrees, extVars);
    }

    public void Compile(IEnumerable<string>? extVars)
    {
        var program = GetProgram(extVars);
        KeyAction = program.KeyAction;
        NeedIL = program.NeedIL;
    }

    public EvaluationResult Evaluate(IOutputAdapter output, ICGamePad pad,
        Dictionary<string, Func<int>> externalGetters,
        CancellationToken token)
    {

        var program = GetProgram(externalGetters.Select(v=>v.Key));
        var evaluator = new Evaluator(program, externalGetters ?? [], token)
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
