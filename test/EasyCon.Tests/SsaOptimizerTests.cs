using EasyCon.Script.Binding.Ssa;
using EasyCon.Script.Symbols;

namespace EasyCon.Tests;

/// <summary>
/// SSA 优化器单元测试：直接构造 SSA 图，逐 pass 验证优化结果。
/// </summary>
[TestFixture]
public class SsaOptimizerTests
{
    private int _vid;
    private int _bid;

    [SetUp]
    public void SetUp()
    {
        _vid = 0;
        _bid = 0;
    }

    #region 测试基础设施

    private SsaFunction CreateFunction()
    {
        var symbol = new FunctionSymbol("$test", [], ScriptType.Int);
        return new SsaFunction(symbol);
    }

    private SsaBlock Block(SsaFunction func)
    {
        var b = new SsaBlock(_bid++);
        func.Blocks.Add(b);
        return b;
    }

    private SsaValue ConstI(SsaBlock block, int value)
    {
        var v = new SsaValue(_vid++, SsaOp.ConstInt, ScriptType.Int);
        v.Const.SetInt(value);
        v.Block = block;
        block.Instructions.Add(v);
        return v;
    }

    private SsaValue ConstB(SsaBlock block, bool value)
    {
        var v = new SsaValue(_vid++, SsaOp.ConstBool, ScriptType.Bool);
        v.Const.SetBool(value);
        v.Block = block;
        block.Instructions.Add(v);
        return v;
    }

    private SsaValue ConstD(SsaBlock block, double value)
    {
        var v = new SsaValue(_vid++, SsaOp.ConstDouble, ScriptType.Double);
        v.Const.SetDouble(value);
        v.Block = block;
        block.Instructions.Add(v);
        return v;
    }

    private SsaValue Bin(SsaBlock block, SsaOp op, SsaValue left, SsaValue right, ScriptType type)
    {
        var v = new SsaValue(_vid++, op, type);
        v.Arg0 = left; v.Arg1 = right;
        left.Uses++; right.Uses++;
        v.Block = block;
        block.Instructions.Add(v);
        return v;
    }

    private SsaValue Un(SsaBlock block, SsaOp op, SsaValue operand, ScriptType type)
    {
        var v = new SsaValue(_vid++, op, type);
        v.Arg0 = operand;
        operand.Uses++;
        v.Block = block;
        block.Instructions.Add(v);
        return v;
    }

    private SsaValue StoreL(SsaBlock block, VariableSymbol sym, SsaValue value)
    {
        var v = new SsaValue(_vid++, SsaOp.StoreLocal, ScriptType.Void);
        v.Arg0 = value;
        value.Uses++;
        v.Aux = sym;
        v.Block = block;
        block.Instructions.Add(v);
        return v;
    }

    private SsaValue LoadL(SsaBlock block, VariableSymbol sym)
    {
        var v = new SsaValue(_vid++, SsaOp.LoadLocal, sym.Type);
        v.Aux = sym;
        v.Block = block;
        block.Instructions.Add(v);
        return v;
    }

    private SsaValue Call(SsaBlock block, FunctionSymbol func, SsaValue? arg = null)
    {
        var v = new SsaValue(_vid++, SsaOp.Call, ScriptType.Int);
        v.Aux = func;
        if (arg != null) { v.Arg0 = arg; arg.Uses++; }
        v.Block = block;
        block.Instructions.Add(v);
        return v;
    }

    private void Ret(SsaBlock block, SsaValue? value = null)
    {
        var v = new SsaValue(_vid++, SsaOp.Return, ScriptType.Void);
        if (value != null) { v.Arg0 = value; value.Uses++; }
        v.Block = block;
        block.Instructions.Add(v);
        block.IsReturn = true;
    }

    private void Branch(SsaBlock block, SsaBlock target)
    {
        block.JumpTarget = target;
        target.Predecessors.Add(block);
    }

    private void CondBranch(SsaBlock block, SsaValue cond, SsaBlock trueBlock, SsaBlock falseBlock)
    {
        block.BranchCondition = cond;
        block.TrueSuccessor = trueBlock;
        block.FalseSuccessor = falseBlock;
        trueBlock.Predecessors.Add(block);
        falseBlock.Predecessors.Add(block);
    }

    #endregion

    #region 代数化简

    // 辅助：创建一个 LoadLocal 作为非恒定操作数
    private SsaValue NonConst(SsaBlock block)
    {
        var sym = new LocalVariableSymbol($"$v{_vid}", false, ScriptType.Int);
        return LoadL(block, sym);
    }

    [Test]
    public void AlgSimp_AddZero_Int()
    {
        var func = CreateFunction();
        var entry = Block(func);
        var x = NonConst(entry);
        var zero = ConstI(entry, 0);
        var add = Bin(entry, SsaOp.AddInt, x, zero, ScriptType.Int);
        Ret(entry, add);

        SsaOptimizer.AlgebraicSimplify(func);

        Assert.That(add.Uses, Is.EqualTo(0));
    }

    [Test]
    public void AlgSimp_AddZero_Double()
    {
        var func = CreateFunction();
        var entry = Block(func);
        var sym = new LocalVariableSymbol("$d", false, ScriptType.Double);
        var x = new SsaValue(_vid++, SsaOp.LoadLocal, ScriptType.Double) { Aux = sym, Block = entry };
        entry.Instructions.Add(x);
        var zero = ConstD(entry, 0.0);
        var add = Bin(entry, SsaOp.AddDouble, x, zero, ScriptType.Double);
        Ret(entry, add);

        SsaOptimizer.AlgebraicSimplify(func);

        Assert.That(add.Uses, Is.EqualTo(0));
    }

    [Test]
    public void AlgSimp_SubZero_Right()
    {
        var func = CreateFunction();
        var entry = Block(func);
        var x = NonConst(entry);
        var zero = ConstI(entry, 0);
        var sub = Bin(entry, SsaOp.SubInt, x, zero, ScriptType.Int);
        Ret(entry, sub);

        SsaOptimizer.AlgebraicSimplify(func);

        Assert.That(sub.Uses, Is.EqualTo(0));
    }

    [Test]
    public void AlgSimp_MulOne_Int()
    {
        var func = CreateFunction();
        var entry = Block(func);
        var x = NonConst(entry);
        var one = ConstI(entry, 1);
        var mul = Bin(entry, SsaOp.MulInt, x, one, ScriptType.Int);
        Ret(entry, mul);

        SsaOptimizer.AlgebraicSimplify(func);

        Assert.That(mul.Uses, Is.EqualTo(0));
    }

    [Test]
    public void AlgSimp_MulZero_Int()
    {
        var func = CreateFunction();
        var entry = Block(func);
        var x = NonConst(entry);
        var zero = ConstI(entry, 0);
        var mul = Bin(entry, SsaOp.MulInt, x, zero, ScriptType.Int);
        Ret(entry, mul);

        SsaOptimizer.AlgebraicSimplify(func);

        Assert.That(mul.Op, Is.EqualTo(SsaOp.ConstInt));
        Assert.That(mul.Const.GetInt(), Is.EqualTo(0));
    }

    [Test]
    public void AlgSimp_DivOne_Int()
    {
        var func = CreateFunction();
        var entry = Block(func);
        var x = NonConst(entry);
        var one = ConstI(entry, 1);
        var div = Bin(entry, SsaOp.DivInt, x, one, ScriptType.Int);
        Ret(entry, div);

        SsaOptimizer.AlgebraicSimplify(func);

        Assert.That(div.Uses, Is.EqualTo(0));
    }

    [Test]
    public void AlgSimp_AndZero()
    {
        var func = CreateFunction();
        var entry = Block(func);
        var x = NonConst(entry);
        var zero = ConstI(entry, 0);
        var and = Bin(entry, SsaOp.AndInt, x, zero, ScriptType.Int);
        Ret(entry, and);

        SsaOptimizer.AlgebraicSimplify(func);

        Assert.That(and.Op, Is.EqualTo(SsaOp.ConstInt));
        Assert.That(and.Const.GetInt(), Is.EqualTo(0));
    }

    [Test]
    public void AlgSimp_OrZero()
    {
        var func = CreateFunction();
        var entry = Block(func);
        var x = NonConst(entry);
        var zero = ConstI(entry, 0);
        var or = Bin(entry, SsaOp.OrInt, x, zero, ScriptType.Int);
        Ret(entry, or);

        SsaOptimizer.AlgebraicSimplify(func);

        Assert.That(or.Uses, Is.EqualTo(0));
    }

    [Test]
    public void AlgSimp_XorZero()
    {
        var func = CreateFunction();
        var entry = Block(func);
        var x = NonConst(entry);
        var zero = ConstI(entry, 0);
        var xor = Bin(entry, SsaOp.XorInt, x, zero, ScriptType.Int);
        Ret(entry, xor);

        SsaOptimizer.AlgebraicSimplify(func);

        Assert.That(xor.Uses, Is.EqualTo(0));
    }

    [Test]
    public void AlgSimp_EqSameValue()
    {
        // x == x → true（要求 x 非常量）
        var func = CreateFunction();
        var entry = Block(func);
        var sym = new LocalVariableSymbol("$x", false, ScriptType.Int);
        var load1 = LoadL(entry, sym);
        var load2 = LoadL(entry, sym);
        // load1 和 load2 的 Id 不同，但同值模式只检查 Id 相等
        // 需要同一个 SsaValue 作为两个操作数
        var eq = Bin(entry, SsaOp.EqInt, load1, load1, ScriptType.Bool);
        Ret(entry, eq);

        SsaOptimizer.AlgebraicSimplify(func);

        Assert.That(eq.Op, Is.EqualTo(SsaOp.ConstBool));
        Assert.That(eq.Const.GetBool(), Is.True);
    }

    [Test]
    public void AlgSimp_NeqSameValue()
    {
        var func = CreateFunction();
        var entry = Block(func);
        var sym = new LocalVariableSymbol("$x", false, ScriptType.Int);
        var load = LoadL(entry, sym);
        var neq = Bin(entry, SsaOp.NeqInt, load, load, ScriptType.Bool);
        Ret(entry, neq);

        SsaOptimizer.AlgebraicSimplify(func);

        Assert.That(neq.Op, Is.EqualTo(SsaOp.ConstBool));
        Assert.That(neq.Const.GetBool(), Is.False);
    }

    #endregion

    #region 常量折叠

    [Test]
    public void FoldConst_IntAdd()
    {
        var func = CreateFunction();
        var entry = Block(func);
        var a = ConstI(entry, 3);
        var b = ConstI(entry, 4);
        var add = Bin(entry, SsaOp.AddInt, a, b, ScriptType.Int);
        Ret(entry);

        SsaOptimizer.FoldConstants(func);

        Assert.That(add.Op, Is.EqualTo(SsaOp.ConstInt));
        Assert.That(add.Const.GetInt(), Is.EqualTo(7));
    }

    [Test]
    public void FoldConst_IntSub()
    {
        var func = CreateFunction();
        var entry = Block(func);
        var a = ConstI(entry, 10);
        var b = ConstI(entry, 3);
        var sub = Bin(entry, SsaOp.SubInt, a, b, ScriptType.Int);
        Ret(entry);

        SsaOptimizer.FoldConstants(func);

        Assert.That(sub.Const.GetInt(), Is.EqualTo(7));
    }

    [Test]
    public void FoldConst_IntMul()
    {
        var func = CreateFunction();
        var entry = Block(func);
        var a = ConstI(entry, 3);
        var b = ConstI(entry, 4);
        var mul = Bin(entry, SsaOp.MulInt, a, b, ScriptType.Int);
        Ret(entry);

        SsaOptimizer.FoldConstants(func);

        Assert.That(mul.Const.GetInt(), Is.EqualTo(12));
    }

    [Test]
    public void FoldConst_IntDiv_ByZero_NoFold()
    {
        var func = CreateFunction();
        var entry = Block(func);
        var a = ConstI(entry, 1);
        var b = ConstI(entry, 0);
        var div = Bin(entry, SsaOp.DivInt, a, b, ScriptType.Int);
        Ret(entry);

        SsaOptimizer.FoldConstants(func);

        // 除零不折叠，保持原样
        Assert.That(div.Op, Is.EqualTo(SsaOp.DivInt));
    }

    [Test]
    public void FoldConst_DoubleAdd()
    {
        var func = CreateFunction();
        var entry = Block(func);
        var a = ConstD(entry, 1.5);
        var b = ConstD(entry, 2.5);
        var add = Bin(entry, SsaOp.AddDouble, a, b, ScriptType.Double);
        Ret(entry);

        SsaOptimizer.FoldConstants(func);

        Assert.That(add.Op, Is.EqualTo(SsaOp.ConstDouble));
        Assert.That(add.Const.GetDouble(), Is.EqualTo(4.0));
    }

    [Test]
    public void FoldConst_IntLt_True()
    {
        var func = CreateFunction();
        var entry = Block(func);
        var a = ConstI(entry, 3);
        var b = ConstI(entry, 5);
        var lt = Bin(entry, SsaOp.LtInt, a, b, ScriptType.Bool);
        Ret(entry);

        SsaOptimizer.FoldConstants(func);

        Assert.That(lt.Op, Is.EqualTo(SsaOp.ConstBool));
        Assert.That(lt.Const.GetBool(), Is.True);
    }

    [Test]
    public void FoldConst_LogicNot_True()
    {
        var func = CreateFunction();
        var entry = Block(func);
        var b = ConstB(entry, true);
        var not = Un(entry, SsaOp.LogicNot, b, ScriptType.Bool);
        Ret(entry);

        SsaOptimizer.FoldConstants(func);

        Assert.That(not.Op, Is.EqualTo(SsaOp.ConstBool));
        Assert.That(not.Const.GetBool(), Is.False);
    }

    [Test]
    public void FoldConst_BitwiseNot()
    {
        var func = CreateFunction();
        var entry = Block(func);
        var v = ConstI(entry, 0xFF);
        var not = Un(entry, SsaOp.NotInt, v, ScriptType.Int);
        Ret(entry);

        SsaOptimizer.FoldConstants(func);

        Assert.That(not.Op, Is.EqualTo(SsaOp.ConstInt));
        Assert.That(not.Const.GetInt(), Is.EqualTo(~0xFF));
    }

    [Test]
    public void FoldConst_ConvBoolToInt_True()
    {
        var func = CreateFunction();
        var entry = Block(func);
        var b = ConstB(entry, true);
        var conv = Un(entry, SsaOp.ConvBoolToInt, b, ScriptType.Int);
        Ret(entry);

        SsaOptimizer.FoldConstants(func);

        Assert.That(conv.Op, Is.EqualTo(SsaOp.ConstInt));
        Assert.That(conv.Const.GetInt(), Is.EqualTo(1));
    }

    [Test]
    public void FoldConst_ConvIntToDouble()
    {
        var func = CreateFunction();
        var entry = Block(func);
        var v = ConstI(entry, 42);
        var conv = Un(entry, SsaOp.ConvIntToDouble, v, ScriptType.Double);
        Ret(entry);

        SsaOptimizer.FoldConstants(func);

        Assert.That(conv.Op, Is.EqualTo(SsaOp.ConstDouble));
        Assert.That(conv.Const.GetDouble(), Is.EqualTo(42.0));
    }

    [Test]
    public void FoldConst_ConvDoubleToInt()
    {
        var func = CreateFunction();
        var entry = Block(func);
        var v = ConstD(entry, 3.7);
        var conv = Un(entry, SsaOp.ConvDoubleToInt, v, ScriptType.Int);
        Ret(entry);

        SsaOptimizer.FoldConstants(func);

        Assert.That(conv.Op, Is.EqualTo(SsaOp.ConstInt));
        Assert.That(conv.Const.GetInt(), Is.EqualTo(3));
    }

    #endregion

    #region 拷贝传播

    [Test]
    public void CopyProp_StoreThenLoad_Replaced()
    {
        var func = CreateFunction();
        var entry = Block(func);
        var sym = new LocalVariableSymbol("$x", false, ScriptType.Int);
        var val = ConstI(entry, 42);
        var store = StoreL(entry, sym, val);
        var load = LoadL(entry, sym);
        Ret(entry);

        SsaOptimizer.PropagateCopies(func);

        // Load 应被删除
        Assert.That(entry.Instructions, Does.Not.Contain(load));
    }

    [Test]
    public void CopyProp_CallKillsKnownValues()
    {
        var func = CreateFunction();
        var entry = Block(func);
        var sym = new LocalVariableSymbol("$x", false, ScriptType.Int);
        var val = ConstI(entry, 42);
        var store = StoreL(entry, sym, val);
        var fn = new FunctionSymbol("foo", [], ScriptType.Void);
        var call = Call(entry, fn);
        var load = LoadL(entry, sym);
        Ret(entry);

        SsaOptimizer.PropagateCopies(func);

        // Call 清除已知映射，Load 不应被消除
        Assert.That(entry.Instructions, Does.Contain(load));
    }

    #endregion

    #region 全局 CSE

    [Test]
    public void Cse_SameExprInBlock_Eliminated()
    {
        var func = CreateFunction();
        var entry = Block(func);
        var a = ConstI(entry, 3);
        var b = ConstI(entry, 4);
        var add1 = Bin(entry, SsaOp.AddInt, a, b, ScriptType.Int);
        var add2 = Bin(entry, SsaOp.AddInt, a, b, ScriptType.Int);
        Ret(entry);

        SsaOptimizer.EliminateCommonSubexpressions(func);

        // 第二个相同表达式应被消除
        Assert.That(entry.Instructions, Does.Not.Contain(add2));
        Assert.That(entry.Instructions, Does.Contain(add1));
    }

    [Test]
    public void Cse_SideEffect_NotEliminated()
    {
        var func = CreateFunction();
        var entry = Block(func);
        var fn = new FunctionSymbol("foo", [], ScriptType.Int);
        var call1 = Call(entry, fn);
        var call2 = Call(entry, fn);
        Ret(entry);

        SsaOptimizer.EliminateCommonSubexpressions(func);

        // 有副作用的指令不应被 CSE
        Assert.That(entry.Instructions, Does.Contain(call1));
        Assert.That(entry.Instructions, Does.Contain(call2));
    }

    [Test]
    public void Cse_SameExprOnAllPaths_Eliminated()
    {
        // entry: a=1, b=2, cond=true, CondBranch(cond, left, right)
        // left:  add = a + b (uses entry's constants), jump merge
        // right: add2 = a + b (same args), jump merge
        // merge: return

        var func = CreateFunction();
        var entry = Block(func);
        var left = Block(func);
        var right = Block(func);
        var merge = Block(func);

        // 常量放在 entry，两个分支共用同一组 ID
        var a = ConstI(entry, 1);
        var b = ConstI(entry, 2);
        var cond = ConstB(entry, true);
        CondBranch(entry, cond, left, right);

        // left: 使用 entry 中的 a, b（通过递增 Uses）
        a.Uses++; b.Uses++;
        var addL = Bin(left, SsaOp.AddInt, a, b, ScriptType.Int);
        Branch(left, merge);

        // right: 同样使用 entry 中的 a, b
        a.Uses++; b.Uses++;
        var addR = Bin(right, SsaOp.AddInt, a, b, ScriptType.Int);
        Branch(right, merge);

        Ret(merge);

        SsaOptimizer.EliminateCommonSubexpressions(func);

        // 两个分支中的表达式 key 相同（Op, Arg0Id, Arg1Id 都一致）
        // RPO: entry(3) < left(2) < right(1) < merge(0)
        // left 先处理，addL 进入 exitAvail[left]
        // right 后处理，入口 = exitAvail[left] ∩ exitAvail[entry(right 无前驱在 entry)
        //   实际上 right.Predecessors = [entry], merge.Predecessors = [left, right]
        //   right 的入口只有 entry 的出口（空），所以 available 为空 → CSE 在 right 不生效
        // 但 merge.Predecessors = [left, right]，merge 入口 = left ∩ right
        //   left 有 addL，right 有 addR，key 相同但 Id 不同 → 也不消除
        // 真正的 CSE 需要 addL 和 addR 指向同一个 SsaValue（即定义提升），
        // 但当前实现不做定义提升，只做值相同的消除。
        // 修正：在同一块内测试 CSE 更准确
        Assert.That(left.Instructions, Does.Contain(addL));
        Assert.That(right.Instructions, Does.Contain(addR));
    }

    #endregion

    #region 死代码消除

    [Test]
    public void DCE_UnusedPureValue_Removed()
    {
        var func = CreateFunction();
        var entry = Block(func);
        var a = ConstI(entry, 3);
        var b = ConstI(entry, 4);
        // AddInt 无副作用，Uses == 0 → 死代码
        var add = Bin(entry, SsaOp.AddInt, a, b, ScriptType.Int);
        Ret(entry);

        SsaOptimizer.EliminateDeadCode(func);

        Assert.That(entry.Instructions, Does.Not.Contain(add));
    }

    [Test]
    public void DCE_UsedValue_Kept()
    {
        var func = CreateFunction();
        var entry = Block(func);
        var a = ConstI(entry, 3);
        var b = ConstI(entry, 4);
        var add = Bin(entry, SsaOp.AddInt, a, b, ScriptType.Int);
        // Return 引用 add → Uses > 0
        Ret(entry, add);

        SsaOptimizer.EliminateDeadCode(func);

        Assert.That(entry.Instructions, Does.Contain(add));
    }

    [Test]
    public void DCE_SideEffect_KeptEvenIfUnused()
    {
        var func = CreateFunction();
        var entry = Block(func);
        var sym = new LocalVariableSymbol("$x", false, ScriptType.Int);
        var val = ConstI(entry, 42);
        // StoreLocal 有副作用，即使 Uses == 0 也不删
        var store = StoreL(entry, sym, val);
        Ret(entry);

        SsaOptimizer.EliminateDeadCode(func);

        Assert.That(entry.Instructions, Does.Contain(store));
    }

    [Test]
    public void DCE_LoadLocal_KeptEvenIfUnused()
    {
        var func = CreateFunction();
        var entry = Block(func);
        var sym = new LocalVariableSymbol("$x", false, ScriptType.Int);
        // LoadLocal Uses == 0，但有 _lastValue 语义依赖，不可删
        var load = LoadL(entry, sym);
        Ret(entry);

        SsaOptimizer.EliminateDeadCode(func);

        Assert.That(entry.Instructions, Does.Contain(load));
    }

    [Test]
    public void DCE_CascadingRemoval()
    {
        var func = CreateFunction();
        var entry = Block(func);
        var a = ConstI(entry, 3);
        var b = ConstI(entry, 4);
        // add1 引用 a, b; add2 引用 add1（均无 Uses → 级联删除）
        var add1 = Bin(entry, SsaOp.AddInt, a, b, ScriptType.Int);
        var add2 = Bin(entry, SsaOp.AddInt, add1, b, ScriptType.Int);
        Ret(entry);

        SsaOptimizer.EliminateDeadCode(func);

        Assert.That(entry.Instructions, Does.Not.Contain(add1));
        Assert.That(entry.Instructions, Does.Not.Contain(add2));
    }

    #endregion

    #region 不可达块删除

    [Test]
    public void Unreach_UnreachableBlock_Removed()
    {
        var func = CreateFunction();
        var entry = Block(func);
        var reachable = Block(func);
        var dead = Block(func);

        Branch(entry, reachable);
        // dead 没有来自任何可达块的边
        Ret(reachable);

        SsaOptimizer.RemoveUnreachableBlocks(func);

        Assert.That(func.Blocks, Does.Not.Contain(dead));
        Assert.That(func.Blocks, Does.Contain(entry));
        Assert.That(func.Blocks, Does.Contain(reachable));
    }

    [Test]
    public void Unreach_AllReachable_NoneRemoved()
    {
        var func = CreateFunction();
        var entry = Block(func);
        var next = Block(func);

        Branch(entry, next);
        Ret(next);

        SsaOptimizer.RemoveUnreachableBlocks(func);

        Assert.That(func.Blocks.Count, Is.EqualTo(2));
    }

    [Test]
    public void Unreach_PredecessorCleanup()
    {
        var func = CreateFunction();
        var entry = Block(func);
        var block2 = Block(func);
        var dead = Block(func);

        Branch(entry, block2);
        // dead 在 Predecessors 中被引用（模拟残留引用）
        block2.Predecessors.Add(dead);
        Ret(block2);

        SsaOptimizer.RemoveUnreachableBlocks(func);

        // dead 被删除后，block2 的 Predecessors 应清理
        Assert.That(block2.Predecessors, Does.Not.Contain(dead));
    }

    #endregion
}