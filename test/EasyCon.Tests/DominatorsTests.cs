using EasyCon.Script;
using EasyCon.Script.Binding;
using EasyCon.Script.Ssa;
using EasyCon.Script.Symbols;

namespace EasyCon.Tests;

/// <summary>
/// Dominators 模块（Cooper-Harvey-Kennedy 支配算法）单元测试。
/// 用真实的 SsaBlock/SsaFunction 图结构验证 idom / Dominates / NCA 的正确性。
/// </summary>
[TestFixture]
public class DominatorsTests
{
    /// <summary>造一个空块（自带递增 Id）。</summary>
    private static SsaBlock B(int id) => new(id);

    /// <summary>无条件边 from → to。</summary>
    private static void Jump(SsaBlock from, SsaBlock to)
    {
        from.JumpTarget = to;
        to.Predecessors.Add(from);
    }

    /// <summary>条件分支 from → {trueSucc, falseSucc}。BranchCondition 用非 null 占位即可（GetSuccessors 只判 != null）。</summary>
    private static void Branch(SsaBlock from, SsaBlock trueSucc, SsaBlock falseSucc)
    {
        from.BranchCondition = new SsaValue(0, SsaOp.ConstBool, ScriptType.Bool); // 占位
        from.TrueSuccessor = trueSucc;
        from.FalseSuccessor = falseSucc;
        trueSucc.Predecessors.Add(from);
        falseSucc.Predecessors.Add(from);
    }

    /// <summary>把若干块组装成一个函数（Entry = 第 0 块）。</summary>
    private static SsaFunction Func(params SsaBlock[] blocks)
    {
        var sym = new FunctionSymbol("test", [], ScriptType.Int);
        var func = new SsaFunction(sym);
        foreach (var b in blocks) func.Blocks.Add(b);
        return func;
    }

    // ============================================================
    // 钻石 CFG：entry → {L, R} → join
    // idom(L)=entry, idom(R)=entry, idom(join)=entry
    // ============================================================
    [Test]
    public void Diamond_Idom_And_Dominates()
    {
        var entry = B(0);
        var left = B(1);
        var right = B(2);
        var join = B(3);

        Branch(entry, left, right);
        Jump(left, join);
        Jump(right, join);

        var dom = Dominators.Compute(Func(entry, left, right, join));

        Assert.That(dom.ImmediateDominator(left), Is.EqualTo(entry));
        Assert.That(dom.ImmediateDominator(right), Is.EqualTo(entry));
        Assert.That(dom.ImmediateDominator(join), Is.EqualTo(entry));
        Assert.That(dom.ImmediateDominator(entry), Is.EqualTo(entry)); // 自环哨兵

        Assert.That(dom.Dominates(entry, join), Is.True);
        Assert.That(dom.Dominates(left, join), Is.False, "left 不支配 join（right 也能到达）");
        Assert.That(dom.Dominates(join, entry), Is.False);
        Assert.That(dom.Dominates(entry, entry), Is.True, "自支配");
    }

    [Test]
    public void Diamond_NCA()
    {
        var entry = B(0);
        var left = B(1);
        var right = B(2);
        var join = B(3);

        Branch(entry, left, right);
        Jump(left, join);
        Jump(right, join);

        var dom = Dominators.Compute(Func(entry, left, right, join));

        Assert.That(dom.NCA(left, right), Is.EqualTo(entry));
        Assert.That(dom.NCA(left, join), Is.EqualTo(entry));
        Assert.That(dom.NCA(left, left), Is.EqualTo(left));
    }

    // ============================================================
    // 循环：entry → header → body → latch → header（回边）
    // idom(header)=entry, idom(body)=header, idom(latch)=header
    // ============================================================
    [Test]
    public void Loop_Dominates_Body_And_Latch()
    {
        var entry = B(0);
        var header = B(1);
        var body = B(2);
        var latch = B(3);
        var exit = B(4);

        Jump(entry, header);
        Branch(header, body, exit); // header 条件跳 body 或退出
        Jump(body, latch);
        Jump(latch, header); // 回边

        var dom = Dominators.Compute(Func(entry, header, body, latch, exit));

        Assert.That(dom.ImmediateDominator(header), Is.EqualTo(entry));
        Assert.That(dom.ImmediateDominator(body), Is.EqualTo(header), "循环头支配循环体");
        Assert.That(dom.ImmediateDominator(latch), Is.EqualTo(body), "latch 唯一前驱是 body → idom 是 body");
        Assert.That(dom.Dominates(header, latch), Is.True, "header 经 body 传递支配 latch");
        Assert.That(dom.Dominates(header, body), Is.True);
    }

    // ============================================================
    // 嵌套钻石 + 循环：验证 NCA 在多层结构下正确
    // entry → A → {B, C}; B → D; C → D; D → A（循环回边）
    // ============================================================
    [Test]
    public void Nested_NCA_Picks_Common_Ancestor()
    {
        var entry = B(0);
        var a = B(1);
        var bb = B(2);
        var c = B(3);
        var d = B(4);

        Jump(entry, a);
        Branch(a, bb, c);
        Jump(bb, d);
        Jump(c, d);
        Jump(d, a); // 回边

        var dom = Dominators.Compute(Func(entry, a, bb, c, d));

        Assert.That(dom.ImmediateDominator(d), Is.EqualTo(a));
        Assert.That(dom.NCA(bb, c), Is.EqualTo(a), "B、C 的 NCA 是 A");
        Assert.That(dom.Dominates(a, d), Is.True);
        Assert.That(dom.Dominates(entry, d), Is.True);
    }
}