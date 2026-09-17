using EasyCon.Script.Ssa;
using EasyCon.Script.Symbols;

namespace EasyCon.Tests;

/// <summary>
/// SCCP（稀疏条件常量传播）单元测试。
/// 直接构造 SSA 图，验证 SCCP 的分析→改写结果。
/// </summary>
[TestFixture]
public class SsaConstantPropagationTests
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

    private SsaValue Phi(SsaBlock block, ScriptType type, params SsaValue[] args)
    {
        var v = new SsaValue(_vid++, SsaOp.Phi, type);
        v.ExtraArgs = new List<SsaValue>(args);
        foreach (var a in args) a.Uses++;
        v.Block = block;
        block.Phis.Add(v);
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
        cond.Uses++;
        trueBlock.Predecessors.Add(block);
        falseBlock.Predecessors.Add(block);
    }

    #endregion

    #region 单块常量折叠

    [Test]
    public void Sccp_IntAdd_BothConst()
    {
        var func = CreateFunction();
        var entry = Block(func);
        var a = ConstI(entry, 3);
        var b = ConstI(entry, 4);
        var add = Bin(entry, SsaOp.AddInt, a, b, ScriptType.Int);
        Ret(entry, add);

        SsaConstantPropagation.Run(func);

        Assert.That(add.Op, Is.EqualTo(SsaOp.ConstInt));
        Assert.That(add.Const.GetInt(), Is.EqualTo(7));
    }

    [Test]
    public void Sccp_IntCompare_BothConst()
    {
        var func = CreateFunction();
        var entry = Block(func);
        var a = ConstI(entry, 3);
        var b = ConstI(entry, 5);
        var lt = Bin(entry, SsaOp.LtInt, a, b, ScriptType.Bool);
        Ret(entry, lt);

        SsaConstantPropagation.Run(func);

        Assert.That(lt.Op, Is.EqualTo(SsaOp.ConstBool));
        Assert.That(lt.Const.GetBool(), Is.True);
    }

    [Test]
    public void Sccp_ConvIntToDouble()
    {
        var func = CreateFunction();
        var entry = Block(func);
        var a = ConstI(entry, 42);
        var conv = Un(entry, SsaOp.ConvIntToDouble, a, ScriptType.Double);
        Ret(entry, conv);

        SsaConstantPropagation.Run(func);

        Assert.That(conv.Op, Is.EqualTo(SsaOp.ConstDouble));
        Assert.That(conv.Const.GetDouble(), Is.EqualTo(42.0));
    }

    [Test]
    public void Sccp_LogicNot()
    {
        var func = CreateFunction();
        var entry = Block(func);
        var b = ConstB(entry, true);
        var not = Un(entry, SsaOp.LogicNot, b, ScriptType.Bool);
        Ret(entry, not);

        SsaConstantPropagation.Run(func);

        Assert.That(not.Op, Is.EqualTo(SsaOp.ConstBool));
        Assert.That(not.Const.GetBool(), Is.False);
    }

    [Test]
    public void Sccp_DivByZero_NotFolded()
    {
        var func = CreateFunction();
        var entry = Block(func);
        var a = ConstI(entry, 10);
        var b = ConstI(entry, 0);
        var div = Bin(entry, SsaOp.DivInt, a, b, ScriptType.Int);
        Ret(entry, div);

        SsaConstantPropagation.Run(func);

        // 除零不折叠，保持原样
        Assert.That(div.Op, Is.EqualTo(SsaOp.DivInt));
    }

    #endregion

    #region 常量分支折叠

    [Test]
    public void Sccp_FoldBranch_True()
    {
        // entry: cond true → left, right
        // left: ret 42
        // right: ret 0
        // SCCP 应折叠为 br left，right 变不可达
        var func = CreateFunction();
        var entry = Block(func);
        var left = Block(func);
        var right = Block(func);

        var cond = ConstB(entry, true);
        CondBranch(entry, cond, left, right);

        var val42 = ConstI(left, 42);
        Ret(left, val42);
        var val0 = ConstI(right, 0);
        Ret(right, val0);

        SsaConstantPropagation.Run(func);

        // entry 应变为无条件跳转到 left
        Assert.That(entry.BranchCondition, Is.Null);
        Assert.That(entry.JumpTarget, Is.SameAs(left));
        Assert.That(entry.TrueSuccessor, Is.Null);
        Assert.That(entry.FalseSuccessor, Is.Null);
    }

    [Test]
    public void Sccp_FoldBranch_False()
    {
        var func = CreateFunction();
        var entry = Block(func);
        var left = Block(func);
        var right = Block(func);

        var cond = ConstB(entry, false);
        CondBranch(entry, cond, left, right);

        var val42 = ConstI(left, 42);
        Ret(left, val42);
        var val0 = ConstI(right, 0);
        Ret(right, val0);

        SsaConstantPropagation.Run(func);

        // entry 应变为无条件跳转到 right
        Assert.That(entry.BranchCondition, Is.Null);
        Assert.That(entry.JumpTarget, Is.SameAs(right));
    }

    [Test]
    public void Sccp_FoldBranch_ComputedCondition()
    {
        // entry: EqInt(1, 1) → true, cond → left, right
        // SCCP 应计算 EqInt(1,1) → ConstBool(true) 并折叠分支
        var func = CreateFunction();
        var entry = Block(func);
        var left = Block(func);
        var right = Block(func);

        var a = ConstI(entry, 1);
        var b = ConstI(entry, 1);
        var eq = Bin(entry, SsaOp.EqInt, a, b, ScriptType.Bool);
        CondBranch(entry, eq, left, right);

        Ret(left);
        Ret(right);

        SsaConstantPropagation.Run(func);

        Assert.That(entry.BranchCondition, Is.Null);
        Assert.That(entry.JumpTarget, Is.SameAs(left));
    }

    #endregion

    #region Phi 简化

    [Test]
    public void Sccp_Phi_AllSameValue_Simplified()
    {
        // entry → left → merge
        // entry → right → merge
        // merge: phi [val, left], [val, right] → 应简化为 val（所有入参相同）
        var func = CreateFunction();
        var entry = Block(func);
        var left = Block(func);
        var right = Block(func);
        var merge = Block(func);

        var cond = ConstB(entry, true);
        CondBranch(entry, cond, left, right);

        Branch(left, merge);
        Branch(right, merge);

        var val = ConstI(merge, 42);
        var phi = Phi(merge, ScriptType.Int, val, val);
        Ret(merge, phi);

        SsaConstantPropagation.Run(func);

        // Phi 应被简化（从 Phis 列表移除）
        Assert.That(merge.Phis.Count, Is.EqualTo(0));
    }

    #endregion

    #region 跨块传播

    [Test]
    public void Sccp_CrossBlock_ConstantProp()
    {
        // entry: const 10 → br body
        // body: AddInt(10, 5) → should be 15
        // SCCP 应跨块传播 const 10
        var func = CreateFunction();
        var entry = Block(func);
        var body = Block(func);

        var ten = ConstI(entry, 10);
        Branch(entry, body);

        var five = ConstI(body, 5);
        var add = Bin(body, SsaOp.AddInt, ten, five, ScriptType.Int);
        Ret(body, add);

        SsaConstantPropagation.Run(func);

        Assert.That(add.Op, Is.EqualTo(SsaOp.ConstInt));
        Assert.That(add.Const.GetInt(), Is.EqualTo(15));
    }

    [Test]
    public void Sccp_CrossBlock_BranchFolding()
    {
        // entry: const 5, br header
        // header: EqInt(5, 5) → cond ? body : exit
        // 模拟 FOR 循环头：常量上限的循环应该折叠
        var func = CreateFunction();
        var entry = Block(func);
        var header = Block(func);
        var body = Block(func);
        var exit = Block(func);

        Branch(entry, header);

        // header 使用 entry 中定义的常量（模拟 upper bound）
        var five = ConstI(header, 5);
        var five2 = ConstI(header, 5);
        var eq = Bin(header, SsaOp.EqInt, five, five2, ScriptType.Bool);
        CondBranch(header, eq, body, exit);

        Ret(body);
        Ret(exit);

        SsaConstantPropagation.Run(func);

        // EqInt(5, 5) → true → body 可达，exit 的前驱中不应有 header
        Assert.That(header.BranchCondition, Is.Null);
        Assert.That(header.JumpTarget, Is.SameAs(body));
    }

    #endregion

    #region Load/Store 不传播

    [Test]
    public void Sccp_LoadGlobal_NotPropagated()
    {
        // LoadGlobal 不应被 SCCP 视为常量（标准 SCCP 不追踪 store/load）
        var func = CreateFunction();
        var entry = Block(func);
        var sym = new GlobalVariableSymbol("$x", false, ScriptType.Int);

        var load = LoadL(entry, sym); // using LoadL for simplicity
        Ret(entry, load);

        SsaConstantPropagation.Run(func);

        // Load 不应被改写为常量
        Assert.That(load.Op, Is.EqualTo(SsaOp.LoadLocal));
    }

    #endregion

    #region 数组长度常量折叠

    private SsaValue ArrayInitOp(SsaBlock block, ScriptType elemType, params SsaValue[] elements)
    {
        var arrType = ScriptType.ArrayOf(elemType);
        var v = new SsaValue(_vid++, SsaOp.ArrayInit, arrType);
        if (elements.Length > 0) { v.Arg0 = elements[0]; elements[0].Uses++; }
        if (elements.Length > 1)
        {
            v.ExtraArgs = new List<SsaValue>();
            for (int i = 1; i < elements.Length; i++) { v.ExtraArgs.Add(elements[i]); elements[i].Uses++; }
        }
        v.Block = block;
        block.Instructions.Add(v);
        return v;
    }

    private SsaValue ArrayLenOp(SsaBlock block, SsaValue arr)
    {
        var v = new SsaValue(_vid++, SsaOp.ArrayLen, ScriptType.Int);
        v.Arg0 = arr; arr.Uses++;
        v.Block = block;
        block.Instructions.Add(v);
        return v;
    }

    private SsaValue ArrayAppendOp(SsaBlock block, SsaValue arr, SsaValue val, ScriptType elemType)
    {
        var v = new SsaValue(_vid++, SsaOp.ArrayAppend, ScriptType.ArrayOf(elemType));
        v.Arg0 = arr; v.Arg1 = val; arr.Uses++; val.Uses++;
        v.Block = block;
        block.Instructions.Add(v);
        return v;
    }

    private SsaValue ConcatOp(SsaBlock block, SsaValue left, SsaValue right, ScriptType elemType)
    {
        var v = new SsaValue(_vid++, SsaOp.Concat, ScriptType.ArrayOf(elemType));
        v.Arg0 = left; v.Arg1 = right; left.Uses++; right.Uses++;
        v.Block = block;
        block.Instructions.Add(v);
        return v;
    }

    [Test]
    public void Sccp_ArrayLen_FoldArrayInit()
    {
        var func = CreateFunction();
        var entry = Block(func);
        var arr = ArrayInitOp(entry, ScriptType.Int, ConstI(entry, 1), ConstI(entry, 2), ConstI(entry, 3));
        var len = ArrayLenOp(entry, arr);
        Ret(entry, len);

        SsaConstantPropagation.Run(func);

        Assert.That(len.Op, Is.EqualTo(SsaOp.ConstInt));
        Assert.That(len.Const.GetInt(), Is.EqualTo(3));
    }

    [Test]
    public void Sccp_ArrayLen_FoldEmpty()
    {
        var func = CreateFunction();
        var entry = Block(func);
        var arr = ArrayInitOp(entry, ScriptType.Int);
        var len = ArrayLenOp(entry, arr);
        Ret(entry, len);

        SsaConstantPropagation.Run(func);

        Assert.That(len.Op, Is.EqualTo(SsaOp.ConstInt));
        Assert.That(len.Const.GetInt(), Is.EqualTo(0));
    }

    [Test]
    public void Sccp_ArrayLen_FoldAppend()
    {
        var func = CreateFunction();
        var entry = Block(func);
        var arr = ArrayInitOp(entry, ScriptType.Int, ConstI(entry, 1), ConstI(entry, 2));
        var appended = ArrayAppendOp(entry, arr, ConstI(entry, 3), ScriptType.Int);
        var len = ArrayLenOp(entry, appended);
        Ret(entry, len);

        SsaConstantPropagation.Run(func);

        Assert.That(len.Op, Is.EqualTo(SsaOp.ConstInt));
        Assert.That(len.Const.GetInt(), Is.EqualTo(3));
    }

    [Test]
    public void Sccp_ArrayLen_ChainedAppend()
    {
        var func = CreateFunction();
        var entry = Block(func);
        var arr = ArrayInitOp(entry, ScriptType.Int, ConstI(entry, 1));
        var a1 = ArrayAppendOp(entry, arr, ConstI(entry, 2), ScriptType.Int);
        var a2 = ArrayAppendOp(entry, a1, ConstI(entry, 3), ScriptType.Int);
        var len = ArrayLenOp(entry, a2);
        Ret(entry, len);

        SsaConstantPropagation.Run(func);

        Assert.That(len.Op, Is.EqualTo(SsaOp.ConstInt));
        Assert.That(len.Const.GetInt(), Is.EqualTo(3));
    }

    [Test]
    public void Sccp_ArrayLen_NoFoldDynamic()
    {
        var func = CreateFunction();
        var entry = Block(func);
        var sym = new LocalVariableSymbol("$arr", false, ScriptType.ArrayOf(ScriptType.Int));
        sym.Slot = new SlotDesc(0);
        var load = LoadL(entry, sym);
        var len = ArrayLenOp(entry, load);
        Ret(entry, len);

        SsaConstantPropagation.Run(func);

        // LoadLocal 结果长度未知，ArrayLen 不应被折叠
        Assert.That(len.Op, Is.EqualTo(SsaOp.ArrayLen));
    }

    [Test]
    public void Sccp_ArrayLen_EnablesBranchFold()
    {
        // LEN([1,2,3]) == 3 → ConstBool(true) → 折叠分支
        var func = CreateFunction();
        var entry = Block(func);
        var trueBlock = Block(func);
        var falseBlock = Block(func);
        var merge = Block(func);

        var arr = ArrayInitOp(entry, ScriptType.Int, ConstI(entry, 1), ConstI(entry, 2), ConstI(entry, 3));
        var len = ArrayLenOp(entry, arr);
        var cmp = Bin(entry, SsaOp.EqInt, len, ConstI(entry, 3), ScriptType.Bool);
        CondBranch(entry, cmp, trueBlock, falseBlock);

        var tVal = ConstI(trueBlock, 42);
        Branch(trueBlock, merge);

        var fVal = ConstI(falseBlock, 0);
        Branch(falseBlock, merge);

        var phi = Phi(merge, ScriptType.Int, tVal, fVal);
        Ret(merge, phi);

        SsaConstantPropagation.Run(func);

        // ArrayLen 应被折叠为 ConstInt(3)
        Assert.That(len.Op, Is.EqualTo(SsaOp.ConstInt));
        Assert.That(len.Const.GetInt(), Is.EqualTo(3));
        // 比较结果应被折叠为 ConstBool(true)
        Assert.That(cmp.Op, Is.EqualTo(SsaOp.ConstBool));
        // 分支应被折叠为无条件跳转
        Assert.That(entry.BranchCondition, Is.Null);
        Assert.That(entry.JumpTarget, Is.SameAs(trueBlock));
    }

    [Test]
    public void Sccp_Concat_KnownLength()
    {
        var func = CreateFunction();
        var entry = Block(func);
        var left = ArrayInitOp(entry, ScriptType.Int, ConstI(entry, 1), ConstI(entry, 2));
        var right = ArrayInitOp(entry, ScriptType.Int, ConstI(entry, 3), ConstI(entry, 4));
        var concat = ConcatOp(entry, left, right, ScriptType.Int);
        var len = ArrayLenOp(entry, concat);
        Ret(entry, len);

        SsaConstantPropagation.Run(func);

        Assert.That(len.Op, Is.EqualTo(SsaOp.ConstInt));
        Assert.That(len.Const.GetInt(), Is.EqualTo(4));
    }

    #endregion
}