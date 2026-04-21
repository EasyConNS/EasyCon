using EasyCon.Script;
using EasyCon.Script.Syntax;
using EasyScript;
using System.Collections.Immutable;

namespace EasyCon.Core.Runner;

public sealed class EasyRunner : IRunner
{
    Compilation compilation;

    public bool HasKeyAction => compilation?.KeyAction ?? false;
    public bool NeedILLoad => compilation?.NeedIL ?? false;

    public byte[] Assemble(bool auto = true)
    {
        //return new Assembly.Assembler().Assemble(prog, auto);
        throw new NotImplementedException();
    }
    public ImmutableArray<Diagnostic> Init(string code, ImmutableHashSet<string> extVarNames)
    {
        var sourceText = SyntaxTree.Parse(code);
        compilation = Compilation.Create(sourceText);
        return compilation.Compile(extVarNames);
    }
    public ImmutableArray<Diagnostic> Load(string fileName, ImmutableHashSet<string> extVarNames)
    {
        var sourceText = SyntaxTree.Load(fileName);
        compilation = Compilation.Create(sourceText);
        return compilation.Compile(extVarNames);
    }

    public void Run(IOutputAdapter output, ICGamePad pad, Dictionary<string, Func<int>> externalGetters, CancellationToken token)
    {
        compilation?.Evaluate(output, pad, externalGetters.ToImmutableDictionary(), token);
    }

    public string ToCode()
    {
        return compilation?.FormatCode() ?? throw new ArgumentNullException("无代码");
    }
}