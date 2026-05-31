namespace EasyCon.Script.Binding.Ssa;

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

    public override string ToString() => $"BB{Id}";
}