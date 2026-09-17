using EasyCon.Script.Binding;
namespace EasyCon.Script.Ssa;

/// <summary>
/// 支配树（Dominator Tree）：Cooper-Harvey-Kennedy 迭代算法。
/// 「A Simple, Fast Dominance Algorithm」(2001)：在逆后序（RPO）上单趟迭代至不动点，
/// 用「指针对撞」法求最近公共祖先（NCA），避开显式支配前沿的复杂度。
///
/// 惰性构造：首次访问某函数时计算并缓存 idom，后续 O(1) 查询。
/// 项目已有 IsTooComplex（块数&gt;50）护栏，支配计算只在小函数上运行，性能非瓶颈。
///
/// 约定：idom[entry] = entry（自环，作为哨兵）；不可达块的 idom 不存在（不参与计算）。
/// postOrderNumber 按后序递增分配，故 entry（最后出栈）号最大 —— NCA 指针对撞据此向上走。
/// </summary>
internal sealed class Dominators
{
    /// <summary>立即支配者。entry 映射到自身；不可达块不在字典中。</summary>
    private readonly Dictionary<SsaBlock, SsaBlock> _idom = new();

    /// <summary>后序编号（entry 最大）。NCA 对撞时据此判断哪个指针需要上移。</summary>
    private readonly Dictionary<SsaBlock, int> _postOrderNumber = new();

    private readonly SsaBlock _entry;

    private Dominators(SsaFunction func)
    {
        _entry = func.Entry;
        ComputePostOrder(func);
        ComputeIdom();
    }

    /// <summary>惰性构造入口：为指定函数构建支配树。</summary>
    public static Dominators Compute(SsaFunction func) => new(func);

    // ============ 公共查询 ============

    /// <summary>b 的立即支配者。entry 返回自身；不可达块返回 entry（保守值）。</summary>
    public SsaBlock ImmediateDominator(SsaBlock b)
    {
        _idom.TryGetValue(b, out var d);
        return d ?? _entry;
    }

    /// <summary>a 是否支配 b（a == b 时为真；a 是 b 的某级支配者为真）。</summary>
    public bool Dominates(SsaBlock a, SsaBlock b)
    {
        var cur = b;
        // 沿 idom 链上行，至多走块数次即收敛
        while (true)
        {
            if (cur == a) return true;
            if (!_idom.TryGetValue(cur, out var parent)) return false; // 不可达 / 越界
            if (parent == cur) return parent == a; // 到达 entry 自环
            cur = parent;
        }
    }

    /// <summary>最近公共祖先（NCA）：a、b 的最近公共支配者。基于 CHK 指针对撞。</summary>
    public SsaBlock NCA(SsaBlock a, SsaBlock b)
    {
        var f1 = a;
        var f2 = b;
        // 不可达块缺后序号，回退到 entry
        while (f1 != f2)
        {
            while (Num(f1) < Num(f2)) f1 = Parent(f1);
            while (Num(f2) < Num(f1)) f2 = Parent(f2);
        }
        return f1;
    }

    private int Num(SsaBlock b) => _postOrderNumber.TryGetValue(b, out var n) ? n : -1;
    private SsaBlock Parent(SsaBlock b) => _idom.TryGetValue(b, out var p) ? p : _entry;

    // ============ 构造 ============

    /// <summary>迭代 DFS 求后序（避免深 CFG 递归栈溢出），并分配后序编号。</summary>
    private void ComputePostOrder(SsaFunction func)
    {
        var visited = new HashSet<SsaBlock>();
        var postOrder = new List<SsaBlock>();
        var stack = new Stack<(SsaBlock Block, bool Processed)>();
        stack.Push((func.Entry, false));

        while (stack.Count > 0)
        {
            var (block, processed) = stack.Pop();
            // 处理态优先：入后序。必须在 visited 检查之前，否则块已在「发现态」pop 时入 visited，
            // 此处 Add 返回 false 会让整个块的后序登记被跳过（postOrder 永远为空）。
            if (processed)
            {
                postOrder.Add(block);
                continue;
            }
            if (!visited.Add(block)) continue;
            stack.Push((block, true));
            foreach (var succ in block.GetSuccessors())
                stack.Push((succ, false));
        }

        for (int i = 0; i < postOrder.Count; i++)
            _postOrderNumber[postOrder[i]] = i;
    }

    /// <summary>CHK 迭代求 idom：在后序序列上反复扫描至不动点。</summary>
    private void ComputeIdom()
    {
        // 后序序列（entry 在最后）；RPO = 后序的逆序
        var rpo = new List<SsaBlock>(_postOrderNumber.Count);
        foreach (var kv in _postOrderNumber)
            rpo.Add(kv.Key);
        rpo.Sort((x, y) => _postOrderNumber[y].CompareTo(_postOrderNumber[x]));

        // entry 的 idom = entry（哨兵）
        _idom[_entry] = _entry;

        bool changed = true;
        while (changed)
        {
            changed = false;
            // RPO 跳过 entry
            for (int idx = 1; idx < rpo.Count; idx++)
            {
                var b = rpo[idx];
                SsaBlock? newIdom = null;
                foreach (var pred in b.Predecessors)
                {
                    // 仅已被处理的（已分配 idom 的）前驱参与
                    if (!_idom.ContainsKey(pred)) continue;
                    newIdom = newIdom == null ? pred : Intersect(pred, newIdom);
                }

                // 不可达块（无已处理前驱）跳过
                if (newIdom == null) continue;

                if (!_idom.TryGetValue(b, out var cur) || cur != newIdom)
                {
                    _idom[b] = newIdom;
                    changed = true;
                }
            }
        }
    }

    /// <summary>CHK 指针对撞：两块的 NCA。编号小者（远离 entry）向上走到与对方同级。</summary>
    private SsaBlock Intersect(SsaBlock b1, SsaBlock b2)
    {
        var f1 = b1;
        var f2 = b2;
        while (f1 != f2)
        {
            while (_postOrderNumber[f1] < _postOrderNumber[f2])
                f1 = _idom[f1];
            while (_postOrderNumber[f2] < _postOrderNumber[f1])
                f2 = _idom[f2];
        }
        return f1;
    }
}