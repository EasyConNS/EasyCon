using EasyCon.Script.Binding;
namespace EasyCon.Script.Ssa;

/// <summary>
/// 基本块：CFG 节点。
/// Phis 在块开头，Instructions 在 Phi 之后。
/// 出口由 BranchCondition/TrueSuccessor/FalseSuccessor/JumpTarget/IsReturn 互斥决定。
/// </summary>
public sealed class SsaBlock
{
    public readonly int Id;
    public readonly List<SsaValue> Phis = new();
    public readonly List<SsaValue> Instructions = new();

    // ---- 出口（互斥） ----

    /// <summary>条件分支的布尔值（非 null → CondBranch）</summary>
    public SsaValue? BranchCondition;
    public SsaBlock? TrueSuccessor;
    public SsaBlock? FalseSuccessor;

    /// <summary>无条件跳转目标</summary>
    public SsaBlock? JumpTarget;

    /// <summary>是否是 Return 块</summary>
    public bool IsReturn;

    // ---- 前驱 ----
    // 不变量（稳定点处成立）：Predecessors[i] 与本块每个 phi 的 ExtraArgs[i] 一一对应。
    // 构造期前驱先于 phi 建立时，phi 尚不存在；优化器对已有 phi 的块改前驱时，
    // 必须用下方 Remove/Replace helper（它们同步维护 phi 臂与 Uses）。
    public readonly List<SsaBlock> Predecessors = new();

    // ---- 辅助 ----
    public int RpoIndex;

    public SsaBlock(int id) { Id = id; }

    /// <summary>本块是否已终止（有出口指令）</summary>
    public bool IsTerminated => BranchCondition != null || JumpTarget != null || IsReturn;

    /// <summary>获取所有后继块</summary>
    public IEnumerable<SsaBlock> GetSuccessors()
    {
        if (BranchCondition != null)
        {
            yield return TrueSuccessor!;
            yield return FalseSuccessor!;
        }
        else if (JumpTarget != null)
        {
            yield return JumpTarget;
        }
    }

    // ============ 前驱维护 API（phi 臂 ↔ Predecessors 强制对齐）============
    // 这些方法是 Predecessors 列表在「phi 已存在」场景下的合法变更入口。

    /// <summary>追加一个前驱。
    /// 若块已有 phi（Braun 循环头：前向边连好后读取循环变量会插入占位 phi，
    /// 之后才连接回边），为每个 phi 追加一条「自引用」占位臂以维持对齐不变量；
    /// 该臂的真实值由后续 SealBlock→FillPhiArms 整体重建（替换 ExtraArgs）。</summary>
    public void AddPredecessor(SsaBlock pred)
    {
        Predecessors.Add(pred);
        if (Phis.Count == 0) return;
        foreach (var phi in Phis)
        {
            phi.ExtraArgs ??= new List<SsaValue>();
            // 占位臂先用 phi 自身（防止悬空 null）；FillPhiArms 会整体替换。
            phi.ExtraArgs.Add(phi);
            // 自引用不递增 Uses（phi 不应算作自身的使用，否则无法被消除）。
        }
    }

    /// <summary>替换前驱 oldPred→newPred（按下标）。
    /// phi 臂保持不变：沿同一条边到达本块的「值」语义未变，仅来源块引用被替换
    /// （用于 CFG 简化中前驱块本身被合并/重命名的情形）。</summary>
    public void ReplacePredecessor(SsaBlock oldPred, SsaBlock newPred)
    {
        int idx = Predecessors.IndexOf(oldPred);
        if (idx < 0) return;
        Predecessors[idx] = newPred;
        // 臂不变
    }

    /// <summary>移除一个前驱，并在每个 phi 中删除对应臂（递减其 Uses）。</summary>
    public void RemovePredecessor(SsaBlock pred)
    {
        int idx = Predecessors.IndexOf(pred);
        if (idx < 0) return;
        RemovePredecessorAt(idx);
    }

    /// <summary>按下标移除前驱，并在每个 phi 中删除对应臂。</summary>
    public void RemovePredecessorAt(int index)
    {
        Predecessors.RemoveAt(index);
        foreach (var phi in Phis)
        {
            if (phi.ExtraArgs == null || index >= phi.ExtraArgs.Count)
                continue;
            var arm = phi.ExtraArgs[index];
            if (arm != null) arm.Uses--;
            phi.ExtraArgs.RemoveAt(index);
        }
    }

    public override string ToString() => $"BB{Id}";
}