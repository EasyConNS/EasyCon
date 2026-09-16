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
    private readonly FunctionSymbol _function;
    private readonly ImmutableHashSet<FunctionSymbol> _externFunctions;
    private readonly SsaVariableState _vars;
    private readonly Dictionary<BoundLabel, SsaBlock> _labelBlocks = new();
    private readonly List<SsaBlock> _blocks = new();
    private SsaBlock _currentBlock;
    /// <summary>当前块是否为「死续块」：EmitGoto（BREAK/CONTINUE/GOTO）跳转后创建的
    /// 无前驱承接块，其后的语句为不可达死代码。结构化语句（if/for/while/until）与
    /// 标签/跳转不得从死续块引出 fallthrough 边——否则合并点的 SealBlock 会沿这条
    /// 幽灵边穿透读取变量，在未封闭的死块里生成 0 臂占位 φ（fuzz 孤立块根因：
    /// `b34 preds=[] jmp->b33 phis=[v189<-[]]`）。</summary>
    private bool _inDeadSink;
    private int _nextValueId;
    private int _nextBlockId;
    /// <summary>当前语句源码行（1 基，0=未知）；NewValue 盖戳到 SsaValue.Line。</summary>
    private int _currentLine;
    private readonly AstNode _emptySyntax;

    public int NextValueId => _nextValueId;
    public int NextBlockId => _nextBlockId;

    public SsaBuilder(FunctionSymbol function, AstNode emptySyntax,
        int startValueId = 0, int startBlockId = 0,
        ImmutableHashSet<FunctionSymbol>? externFunctions = null)
    {
        _function = function;
        _externFunctions = externFunctions ?? ImmutableHashSet<FunctionSymbol>.Empty;
        _emptySyntax = emptySyntax;
        _nextValueId = startValueId;
        _nextBlockId = startBlockId;
        _vars = new SsaVariableState(this);
        SwitchToBlock(CreateBlock());
    }

    // ============ 工厂方法 ============

    private SsaBlock CreateBlock()
    {
        var block = new SsaBlock(_nextBlockId++);
        _blocks.Add(block);
        return block;
    }

    private SsaValue NewValue(SsaOp op, ScriptType type,
        SsaValue? arg0 = null, SsaValue? arg1 = null,
        object? aux = null)
    {
        var v = new SsaValue(_nextValueId++, op, type)
        {
            Arg0 = arg0,
            Arg1 = arg1,
            Aux = aux,
            Block = _currentBlock,
            Line = _currentLine
        };
        if (arg0 != null) arg0.Uses++;
        if (arg1 != null) arg1.Uses++;
        return v;
    }

    private static void AddExtraUses(List<SsaValue> extras)
    {
        foreach (var v in extras) v.Uses++;
    }

    private void AddInst(SsaValue val)
    {
        _currentBlock.Instructions.Add(val);
    }

    // ============ SsaVariableState 协作 API ============

    /// <summary>构造一个 phi 节点（放入 block.Phis，不进 Instructions）。
    /// 分配全局唯一 Id，臂（ExtraArgs）由调用方随后填充。</summary>
    internal SsaValue NewPhi(SsaBlock block, ScriptType type, object? aux)
    {
        var phi = new SsaValue(_nextValueId++, SsaOp.Phi, type)
        {
            Block = block,
            Aux = aux,
        };
        block.Phis.Add(phi);
        return phi;
    }

    /// <summary>把 old 在本函数所有用处（Arg0/Arg1/ExtraArgs/BranchCondition）重定向到 @new，
    /// 并维护 Uses（old.Uses--，@new.Uses++）。供 phi 简化使用。</summary>
    internal void ReplaceAllUses(SsaValue oldVal, SsaValue newVal)
    {
        if (oldVal == newVal) return;
        foreach (var block in _blocks)
        {
            foreach (var inst in block.Instructions)
                ReplaceOperand(inst, oldVal, newVal);
            foreach (var phi in block.Phis)
                ReplaceOperand(phi, oldVal, newVal);
            if (block.BranchCondition == oldVal)
            {
                block.BranchCondition = newVal;
                newVal.Uses++;
                oldVal.Uses--;
            }
        }
    }

    private static void ReplaceOperand(SsaValue inst, SsaValue oldVal, SsaValue newVal)
    {
        if (inst.Arg0 == oldVal) { inst.Arg0 = newVal; newVal.Uses++; oldVal.Uses--; }
        if (inst.Arg1 == oldVal) { inst.Arg1 = newVal; newVal.Uses++; oldVal.Uses--; }
        if (inst.ExtraArgs != null)
        {
            for (int i = 0; i < inst.ExtraArgs.Count; i++)
            {
                if (inst.ExtraArgs[i] == oldVal)
                {
                    inst.ExtraArgs[i] = newVal;
                    newVal.Uses++;
                    oldVal.Uses--;
                }
            }
        }
    }

    private SsaBlock GetOrCreateLabelBlock(BoundLabel label)
    {
        if (!_labelBlocks.TryGetValue(label, out var block))
        {
            block = CreateBlock();
            _labelBlocks[label] = block;
        }
        return block;
    }
}