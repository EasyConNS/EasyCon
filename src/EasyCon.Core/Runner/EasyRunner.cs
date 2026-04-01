using EasyCon.Script;
using EasyCon.Script.Syntax;
using EasyScript;

namespace EasyCon.Core.Runner;

public sealed class EasyRunner : IRunner
{
    Compilation? compilation;

    public bool HasKeyAction => compilation?.KeyAction?? false;
    public bool NeedILLoad => compilation?.NeedIL ?? false;

    public byte[] Assemble(bool auto = true)
    {
        //return new Assembly.Assembler().Assemble(prog, auto);
        throw new NotImplementedException();
    }
    public void Init(string code, IEnumerable<ExternalVariable> extVars)
    {
        var sourceText = SyntaxTree.Parse(code, extVars);
        compilation = Compilation.Create(sourceText);
    }
    public void Load(string fileName, IEnumerable<ExternalVariable> extVars)
    {
        var sourceText = SyntaxTree.Load(fileName, extVars);
        compilation = Compilation.Create(sourceText);
    }

    public void Run(IOutputAdapter output, ICGamePad pad, CancellationToken token)
    {
        compilation?.Evaluate(output, pad, token);
    }

    public string ToCode()
    {
        return compilation?.FormatCode()??throw new ArgumentNullException("无代码");
    }
}
