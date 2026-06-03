using EasyCon.Script.Symbols;
using System.Collections.Immutable;

namespace EasyCon.Script.Binding.Ssa;

/// <summary>
/// 基于 SSA 调用图的采集卡需求分析。
///
/// BoundProgram.NeedIL 是"乐观假设"：只要 lib 里出现了 CaptureHole/OcrHole/RoiHole
/// 的调用（标准库 VisionSource 一定如此），就标记为需要采集卡。这对纯算法脚本
/// 会导致 CLI 无谓地打开采集卡设备。
///
/// 本分析器从 MainFunction 出发做调用图可达性传播，判断脚本是否真的可能执行到
/// 任何 vision 洞函数。同时过滤 ILNames 中的 __capture__ 占位符，仅保留用户脚本
/// 通过 @标签 语法实际引用的图像标签。
/// </summary>
static class CaptureAnalyzer
{
    public static (bool NeedCapture, ImmutableArray<string> FilteredILNames) Analyze(
        SsaProgram program,
        ImmutableArray<string> rawILNames)
    {
        // 过滤 ILNames：去掉 __capture__ 占位符（lib 内调用洞函数时由 Binder.Calls.cs 添加）
        var filtered = rawILNames
            .Where(n => n != BuiltinFunctions.CapturePlaceholder)
            .ToImmutableArray();

        // 用户脚本直接通过 @标签 引用了图像 → 必须开采集卡
        bool needByImageLabel = !filtered.IsEmpty;

        // 调用图可达性分析：从 main 出发，看是否能到达 vision 洞函数
        bool needByCallGraph = ReachesCaptureHole(program);

        return (needByImageLabel || needByCallGraph, filtered);
    }

    private static bool ReachesCaptureHole(SsaProgram program)
    {
        if (program.MainFunction == null) return false;

        // BFS 从 main 出发，收集所有可达的 FunctionSymbol
        var visited = new HashSet<FunctionSymbol>();
        var queue = new Queue<FunctionSymbol>();
        queue.Enqueue(program.MainFunction.Symbol);
        visited.Add(program.MainFunction.Symbol);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!program.Functions.TryGetValue(current, out var ssaFunc))
                continue;

            // 遍历该函数的所有指令
            foreach (var block in ssaFunc.Blocks)
            {
                foreach (var inst in block.Instructions)
                {
                    // Case 1: 内联到 SSA 的 vision 洞指令（FRAME/OCR/ROI 经 EmitIntrinsic 处理后）
                    // 这些指令直接代表一次采集卡操作，无需再向下展开
                    if (inst.Op is SsaOp.Capture or SsaOp.Ocr or SsaOp.Roi)
                        return true;

                    // Case 2: 常规函数调用，检查 callee
                    if (inst.Op is not (SsaOp.Call or SsaOp.StaticCall))
                        continue;
                    if (inst.Aux is not FunctionSymbol callee)
                        continue;

                    // 命中未内联的 vision 洞函数 → 一定需要采集卡
                    if (BuiltinFunctions.RequiresCapture(callee))
                        return true;

                    // 用户自定义函数：继续传播
                    // （EXTERNAL 函数没法继续向下展开，且 EXTERN 不会是洞函数）
                    if (callee.Declaration != null && visited.Add(callee))
                        queue.Enqueue(callee);
                }
            }
        }

        return false;
    }
}