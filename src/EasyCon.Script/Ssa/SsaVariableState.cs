using EasyCon.Script.Binding;
using EasyCon.Script.Symbols;

namespace EasyCon.Script.Ssa;

/// <summary>
/// Braun 增量 SSA 构造的变量状态机。
/// 实现 Braun et al. (2013) 的「简单高效的 SSA 构造」算法：
///   - 边遍历 Bound IR 边建 CFG，per-block 维护每个变量的当前定义。
///   - 单前驱块：读取穿透到前驱（可能递归回溯）。
///   - 多前驱块（合并点）：插入 phi。
///   - 未封闭块（前驱尚未全部连边，如循环头在回边接好前）：插入「占位 phi」，
///     记入 _incompletePhis，待 SealBlock 时用真值回填。
///
/// 关键正确性：phi 的 ExtraArgs 必须与 phi 所在块的 Predecessors 一一对齐。
/// 本实现遵循该不变量——phi 创建后立即按块前驱数填充臂，占位 phi 在封闭时回填。
/// </summary>
internal sealed class SsaVariableState
{
    // 每个 (block, variable) 的当前到达定义。命中即直接返回（局部 SSA）。
    private readonly Dictionary<(SsaBlock block, VariableSymbol var), SsaValue> _defs = new();
    // 未封闭块里每个变量对应的占位 phi，待 SealBlock 时回填臂。
    private readonly Dictionary<(SsaBlock block, VariableSymbol var), SsaValue> _incompletePhis = new();
    // 已封闭的块（所有前驱已连边）。
    private readonly HashSet<SsaBlock> _sealed = new();
    // 未封闭块的占位 phi 列表（按块分组，方便 SealBlock 时遍历）。
    private readonly Dictionary<SsaBlock, List<(VariableSymbol var, SsaValue phi)>> _incompleteByBlock = new();

    private readonly SsaCodeGenerator _gen;

    public SsaVariableState(SsaCodeGenerator gen) { _gen = gen; }

    public bool IsSealed(SsaBlock block) => _sealed.Contains(block);

    // ============ 核心 API ============

    /// <summary>记录变量在 block 的当前定义（写入）。</summary>
    public void WriteVariable(VariableSymbol variable, SsaValue value, SsaBlock block)
    {
        _defs[(block, variable)] = value;
    }

    /// <summary>读取变量在 block 当前路径上的值（读取）。
    /// 局部命中直接返回；否则递归回溯前驱（可能触发 phi 插入）。</summary>
    public SsaValue ReadVariable(VariableSymbol variable, ScriptType type, SsaBlock block)
    {
        if (_defs.TryGetValue((block, variable), out var def))
            return def;
        return ReadVariableRecursive(variable, type, block);
    }

    private SsaValue ReadVariableRecursive(VariableSymbol variable, ScriptType type, SsaBlock block)
    {
        // 未封闭：插入占位 phi，回填推迟到 SealBlock（避免依赖尚未连好的前驱）
        if (!_sealed.Contains(block))
        {
            var phi = _gen.NewPhi(block, type, variable);
            _incompletePhis[(block, variable)] = phi;
            if (!_incompleteByBlock.TryGetValue(block, out var list))
            {
                list = new List<(VariableSymbol, SsaValue)>();
                _incompleteByBlock[block] = list;
            }
            list.Add((variable, phi));
            // 占位 phi 自身即变量在此块的「当前定义」（防止无限递归）
            _defs[(block, variable)] = phi;
            return phi;
        }

        // 已封闭且单前驱：穿透到前驱
        if (block.Predecessors.Count == 1)
        {
            var val = ReadVariable(variable, type, block.Predecessors[0]);
            _defs[(block, variable)] = val; // 缓存
            return val;
        }

        // 已封闭且多前驱：插入 phi（可能尚未存在），按前驱数填充臂
        if (_incompletePhis.TryGetValue((block, variable), out var existingPhi))
        {
            // 之前作为占位插入过（块封闭时本应已回填，这里兜底回填）
            FillPhiArms(existingPhi, variable, type, block);
            return TryOptimizeTrivialPhi(existingPhi, variable, type, block);
        }

        var phi2 = _gen.NewPhi(block, type, variable);
        _defs[(block, variable)] = phi2; // 先写防无限递归
        FillPhiArms(phi2, variable, type, block);
        return TryOptimizeTrivialPhi(phi2, variable, type, block);
    }

    /// <summary>用每个前驱路径上的当前值填充 phi 的 ExtraArgs（与 Predecessors 对齐）。
    /// 若 phi 已有占位臂（AddPredecessor 追加的自引用占位），先清掉。</summary>
    private void FillPhiArms(SsaValue phi, VariableSymbol variable, ScriptType type, SsaBlock block)
    {
        // 清掉旧占位臂（自引用占位不增 Uses，无需递减；但为防御仍清空）
        phi.ExtraArgs?.Clear();
        phi.ExtraArgs = new List<SsaValue>(block.Predecessors.Count);
        foreach (var pred in block.Predecessors)
        {
            var arm = ReadVariable(variable, type, pred);
            phi.ExtraArgs.Add(arm);
            arm.Uses++;
        }
    }

    /// <summary>平凡 phi 优化：所有臂相同（允许自引用）→ 用该臂替换 phi，删除 phi。
    /// Braun 的「removeTrivialPhi」核心规则。</summary>
    private SsaValue TryOptimizeTrivialPhi(SsaValue phi, VariableSymbol variable, ScriptType type, SsaBlock block)
    {
        if (phi.ExtraArgs == null || phi.ExtraArgs.Count == 0)
            return phi;

        // 取第一个非自引用臂作为基准
        SsaValue? same = null;
        foreach (var arm in phi.ExtraArgs)
        {
            if (arm == phi) continue;
            if (same == null) same = arm;
            else if (same != arm) { same = null; break; }
        }
        // 所有臂都是 phi 自身（未定义变量在所有路径上首次读取）→ 保留 phi（值未定义，运行时为默认）
        if (same == null)
        {
            // 检查是否所有臂都是 phi 自身
            bool allSelf = true;
            foreach (var arm in phi.ExtraArgs)
                if (arm != phi) { allSelf = false; break; }
            if (allSelf) return phi;
            return phi; // 臂不全相同，保留
        }

        // 全同（除自引用外）：用 same 替换 phi
        ReplacePhiWith(phi, same, variable, block);
        return same;
    }

    /// <summary>把 phi 在所有用处处替换为 value，从块中删除 phi，递减臂 Uses。</summary>
    private void ReplacePhiWith(SsaValue phi, SsaValue value, VariableSymbol variable, SsaBlock block)
    {
        // 把 _defs 里【所有】指向 phi 的缓存项重定向到 value。
        // phi 在递归回填期间可能被缓存为多个 (block, var) 的到达定义（穿透过该 phi 的块
        // 在 ReadVariableRecursive 的单前驱路径上会把它写进自己的 _defs 项作为缓存）。
        // 仅更新 (block, variable) 单点会留下指向已删除 phi 的悬空缓存，
        // 后续 ReadVariable 命中缓存即返回死值，生成悬空 phi 臂（嵌套循环死循环的根因）。
        List<(SsaBlock, VariableSymbol)>? staleKeys = null;
        foreach (var kv in _defs)
        {
            if (kv.Value == phi)
            {
                staleKeys ??= new List<(SsaBlock, VariableSymbol)>();
                staleKeys.Add(kv.Key);
            }
        }
        if (staleKeys != null)
            foreach (var k in staleKeys)
                _defs[k] = value;

        // 用 _gen 提供的全局替换：把 phi 的所有用处重定向到 value
        _gen.ReplaceAllUses(phi, value);

        // 从 block.Phis 移除 phi，并递减臂 Uses
        block.Phis.Remove(phi);
        if (phi.ExtraArgs != null)
        {
            foreach (var arm in phi.ExtraArgs)
                if (arm != null && arm != phi) arm.Uses--;
        }
        phi.ExtraArgs?.Clear();

        // 清理占位记录
        _incompletePhis.Remove((block, variable));
    }

    /// <summary>封闭块：所有前驱已连边，回填占位 phi 的臂。</summary>
    public void SealBlock(SsaBlock block)
    {
        if (!_sealed.Add(block)) return; // 已封闭
        if (!_incompleteByBlock.TryGetValue(block, out var list)) return;

        foreach (var (variable, phi) in list)
        {
            FillPhiArms(phi, variable, phi.Type, block);
        }
        // 对刚回填的占位 phi 跑一遍平凡优化（可能会级联消掉）
        // 复制一份避免在遍历中修改集合
        var snapshot = list.ToList();
        _incompleteByBlock.Remove(block);
        foreach (var (variable, phi) in snapshot)
        {
            _incompletePhis.Remove((block, variable));
            TryOptimizeTrivialPhi(phi, variable, phi.Type, block);
        }
    }
}