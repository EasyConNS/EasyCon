using EasyCon.Script;
using EasyCon.Script.Binding.Ssa;
using EasyCon.Script.Syntax;
using EasyScript;
using System.Collections.Immutable;

namespace EasyCon.Core.Runner;

public sealed class EasyRunner : IRunner
{
    Compilation? compilation;
    CompileResult? _result;

    public bool HasKeyAction => _result?.KeyAction ?? false;
    public bool NeedILLoad => _result?.NeedIL ?? false;

    public byte[] Assemble(bool auto = true)
    {
        //return new Assembly.Assembler().Assemble(prog, auto);
        throw new NotImplementedException();
    }
    public ImmutableArray<Diagnostic> Init(string code, ImmutableHashSet<string> extVarNames)
    {
        var sourceText = SyntaxTree.Parse(code);
        compilation = Compilation.Create(sourceText);
        _result = compilation.Compile(extVarNames);
        return _result.Diagnostics;
    }
    public ImmutableArray<Diagnostic> Load(string fileName, ImmutableHashSet<string> extVarNames)
    {
        var sourceText = SyntaxTree.Load(fileName);
        compilation = Compilation.Create(sourceText);
        _result = compilation.Compile(extVarNames);
        return _result.Diagnostics;
    }

    public void Run(IOutputAdapter output, ICGamePad pad, OcrDelegate? ocr, FrameDelegate? frameProvider, RoiDelegate? roiProvider, LabelMatchDelegate? labelMatch, ImmutableHashSet<string>? labelNames, CancellationToken token)
    {
        if (_result?.Program == null) return;

        using var evaluator = new SsaEvaluator(_result.Program, token)
        {
            GamePad = pad,
            Output = output,
            Ocr = ocr,
            Frame = frameProvider,
            Roi = roiProvider,
            LabelMatch = labelMatch,
        };
        evaluator.Evaluate();
    }

    public string ToCode()
    {
        return compilation?.FormatCode() ?? throw new ArgumentNullException("无代码");
    }
}