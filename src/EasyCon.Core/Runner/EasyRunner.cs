using EasyCon.Script;
using EasyCon.Script.Ssa;
using EasyScript;
using System.Collections.Immutable;

namespace EasyCon.Core.Runner;

/// <summary>
/// 统一编译链路运行器（docs/Pipeline.md）：
/// Load/Init → Compilation.CompileFile/CompileSource（模块管线，桌面现编不落盘缓存）
/// → EcxImage → Run 经 EcxVm 桥（EcxInterpreter 执行，语义与 C VM 对齐）。
/// </summary>
public sealed class EasyRunner : IRunner
{
    CompileResult? _result;
    string? _lastCode;
    string? _lastFileName;

    // 桌面解释器路径每次现编（与 v1 等价、无回归）；缓存收益留给 compile/MCU 分发路径
    CompileOptions _options = new() { UseDiskCache = false };

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
        _lastCode = code;
        _lastFileName = null;
        _options.ExtVars = extVarNames;
        _result = Compilation.CompileSource(code, _options);
        return _result.Diagnostics;
    }

    public ImmutableArray<Diagnostic> Load(string fileName, ImmutableHashSet<string> extVarNames)
    {
        _lastCode = null;
        _lastFileName = fileName;
        _options.ExtVars = extVarNames;
        _result = Compilation.CompileFile(fileName, _options);
        return _result.Diagnostics;
    }

    public void Run(IIoAdapter ioAdapter, ICGamePad pad, OcrDelegate? ocr, OcrInitDelegate? ocrInit, Func<int> ocrConf, FrameDelegate? frameProvider, RoiDelegate? roiProvider, LabelMatchDelegate? labelMatch, ImmutableHashSet<string>? labelNames, CancellationToken token, string[]? args = null)
    {
        var image = _result?.Image;
        if (image == null) return;

        EcxVm.Run(image, ioAdapter, pad, ocr, ocrInit, ocrConf, frameProvider, roiProvider, labelMatch,
            token, args, _result!.NativeSymbols);
    }

    public string ToCode()
    {
        return _result?.FormatCode() ?? throw new ArgumentNullException("无代码");
    }

    /// <summary>
    /// 以人类可读格式输出 main 模块的 SSA IR。
    /// 模块管线中 stdlib/vision 为独立模块，不在此输出（CLI modules 命令查看模块清单）。
    /// </summary>
    /// <param name="beforeOptimize">true 时输出优化前的原始 SSA（重新编译，跳过优化）</param>
    public string DumpIr(bool beforeOptimize = false)
    {
        if (_result == null)
            return "error: 未加载脚本";
        if (!beforeOptimize)
        {
            if (_result.Program == null)
                return string.Join("\n", _result.Diagnostics.Where(d => d.IsError).Select(d => $"error: {d.Message}"));
            return SsaPrinter.Dump(_result.Program);
        }

        var options = new CompileOptions { ExtVars = _options.ExtVars, Optimize = false, UseDiskCache = false };
        var rerun = _lastFileName != null
            ? Compilation.CompileFile(_lastFileName, options)
            : Compilation.CompileSource(_lastCode ?? "", options);
        return rerun.Program != null
            ? SsaPrinter.Dump(rerun.Program)
            : string.Join("\n", rerun.Diagnostics.Where(d => d.IsError).Select(d => $"error: {d.Message}"));
    }
}