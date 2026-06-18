using EasyCon.Script;
using EasyCon.Script.Ssa;
using EasyCon.Script.Syntax;
using EasyCon.Script.Text;
using EasyScript;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;

namespace EasyCon.Core.Runner;

public sealed class EasyRunner : IRunner
{
    Compilation? compilation;
    CompileResult? _result;

    public bool HasKeyAction => _result?.KeyAction ?? false;
    public bool NeedILLoad => _result?.NeedIL ?? false;
    public CompilationTiming? Timing => _result?.Timing;

    public byte[] Assemble(bool auto = true)
    {
        //return new Assembly.Assembler().Assemble(prog, auto);
        throw new NotImplementedException();
    }
    public ImmutableArray<Diagnostic> Init(string code, ImmutableHashSet<string> extVarNames)
    {
        var timing = new CompilationTiming();
        var sw = Stopwatch.StartNew();

        var sourceText = SyntaxTree.Parse(code);
        timing.LexingAndParsing = sw.Elapsed;

        compilation = Compilation.Create(sourceText, timing);
        _result = compilation.Compile(extVarNames);
        return _result.Diagnostics;
    }
    public ImmutableArray<Diagnostic> Load(string fileName, ImmutableHashSet<string> extVarNames)
    {
        var timing = new CompilationTiming();
        var sw = Stopwatch.StartNew();

        var text = File.ReadAllText(fileName);
        var sourceText = SourceText.From(text, fileName);
        timing.FileLoad = sw.Elapsed;

        sw.Restart();
        var syntaxTree = SyntaxTree.Parse(sourceText);
        timing.LexingAndParsing = sw.Elapsed;

        compilation = Compilation.Create(syntaxTree, timing);
        _result = compilation.Compile(extVarNames);
        return _result.Diagnostics;
    }

    public void Run(IIoAdapter ioAdapter, ICGamePad pad, OcrDelegate? ocr, OcrInitDelegate? ocrInit, Func<int> ocrConf, FrameDelegate? frameProvider, RoiDelegate? roiProvider, LabelMatchDelegate? labelMatch, ImmutableHashSet<string>? labelNames, CancellationToken token)
    {
        if (_result?.Program == null) return;

        using var evaluator = new SsaEvaluator(_result.Program, token)
        {
            GamePad = pad,
            IoAdapter = ioAdapter,
            Ocr = ocr,
            OcrInit = ocrInit,
            OcrConf = ocrConf,
            Frame = frameProvider,
            Roi = roiProvider,
            LabelMatch = labelMatch,
            UseJit = UseJit,
        };
        evaluator.Evaluate();
    }

    /// <summary>启用 JIT 编译执行（默认 false）。</summary>
    public bool UseJit { get; set; }

    public string ToCode()
    {
        return compilation?.FormatCode() ?? throw new ArgumentNullException("无代码");
    }

    /// <summary>
    /// 以人类可读格式输出编译后的 SSA IR。
    /// </summary>
    /// <param name="beforeOptimize">true 时输出优化前的原始 SSA（需要重新编译）</param>
    public string DumpIr(bool beforeOptimize = false)
    {
        if (!beforeOptimize)
        {
            if (_result?.Program == null)
                return string.Join("\n", (_result?.Diagnostics ?? []).Select(d => $"error: {d.Message}"));
            return SsaPrinter.Dump(_result.Program);
        }
        // 需要重新编译但跳过优化
        return compilation?.DumpIr(null, beforeOptimize: true) ?? "error: 未加载脚本";
    }
}