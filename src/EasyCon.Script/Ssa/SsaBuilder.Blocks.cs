using EasyCon.Script.Binding;
using EasyCon.Script.Runtime;
using EasyCon.Script.Symbols;
using EasyCon.Script.Syntax;
using EasyScript;
using System.Collections.Immutable;

namespace EasyCon.Script.Ssa;

/// <summary>
/// 将 Bound IR 转换为 SSA IR。
/// 遍历 Bound 的结构化语句，生成扁平的 SsaBlock + SsaValue。
/// </summary>
sealed partial class SsaBuilder
{
    private void SwitchToBlock(SsaBlock block, bool fromConditionalBranch = false)
    {
        _currentBlock = block;
        // 真 SSA（Braun）下不再需要 Clear：变量定义按块独立存储，
        // 合并点的值由 phi 决定，循环头/分支目标读变量时由 ReadVariable 自动处理。
        // block 的封闭（SealBlock）在所有前驱连好后由调用方显式触发。
    }

    private static bool NeedsTerminator(SsaBlock block)
        => !block.IsTerminated;

    // ============ 入口 ============

    public SsaFunction Generate(BoundBlockStatement body)
    {
        var func = new SsaFunction(_function);

        // 入口块无前驱，可立即封闭
        _vars.SealBlock(_currentBlock);

        // 绑定参数：参数值由调用者通过帧写入，入口发出 LoadLocal 获取，
        // 并登记为该参数在入口块的 SSA 定义（体内读参数直接命中，无需再次 LoadLocal）。
        foreach (var param in _function.Parameters)
        {
            var load = NewValue(SsaOp.LoadLocal, param.Type, aux: param);
            AddInst(load);
            _vars.WriteVariable(param, load, _currentBlock);
        }

        // 生成语句
        EmitStatements(body.Statements);

        // 为所有未终止的块添加 Return（包括 IF 内 Return 导致的空 endLabel 块）
        foreach (var block in _blocks)
        {
            if (!block.IsTerminated)
            {
                var ret = NewValue(SsaOp.Return, ScriptType.Void);
                ret.Block = block;
                block.Instructions.Add(ret);
                block.IsReturn = true;
            }
        }

        // 兜底：封闭所有尚未封闭的块（健壮性，正常路径应在控制流结构末尾显式封闭）
        foreach (var block in _blocks)
            _vars.SealBlock(block);

        func.Blocks.AddRange(_blocks);
        return func;
    }

    // ============ 语句发射 ============
}