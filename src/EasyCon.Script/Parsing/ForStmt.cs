using System.Collections.Immutable;

namespace EasyCon.Script.Parsing;

internal sealed class ForBlock(ForStmt condition, ImmutableArray<Statement> statements, Next end) : Statement
{
    public readonly ForStmt Condition = condition;
    public ImmutableArray<Statement> Statements = statements;
    public readonly Next End = end;

    protected override string _GetString()
    {
        throw new NotImplementedException();
    }
}

abstract class ForStmt(ExprBase upper) : Statement
{
    public readonly ExprBase Upper = upper;
    [Obsolete]
    public Next Next;

    //protected virtual void Init(Processor processor)
    //{ }

    //protected abstract bool Cond(Processor processor);

    //protected virtual void Step(Processor processor)
    //{ }
    //public override sealed void Exec(Processor processor)
    //{
    //    var liteScop = processor.GetScope();
    //    if (liteScop.LoopStack.Count == 0 || liteScop.LoopStack.Peek() != this)
    //    {
    //        // entering
    //        liteScop.LoopStack.Push(this);
    //        Init(processor);
    //    }
    //    else
    //    {
    //        // looping
    //        Step(processor);
    //    }
    //    if (!Cond(processor))
    //        processor.PC = liteScop.LoopStack.Pop().Next.Address + 1;
    //}
}

class For_Infinite : ForStmt
{
    public For_Infinite()
        : base(0)
    { }

    //protected override bool Cond(Processor _) => true;

    protected override string _GetString() => "FOR";

    //public override void Assemble(Assembly.Assembler assembler)
    //{
    //    assembler.Add(Assembly.Instructions.AsmFor.Create());
    //    assembler.ForMapping[this] = assembler.Last() as Assembly.Instructions.AsmFor;
    //}
}

class For_Static(ExprBase count) : ForStmt(count)
{
    //protected override void Init(Processor processor)
    //{
    //    var liteScop = processor.GetScope();
    //    liteScop.LoopTime[this] = 0;
    //    liteScop.LoopCount[this] = Upper.Get(processor);
    //}

    //protected override bool Cond(Processor processor)
    //{
    //    var liteScop = processor.GetScope();
    //    return liteScop.LoopTime[this] < liteScop.LoopCount[this];
    //}

    //protected override void Step(Processor processor)
    //{
    //    var liteScop = processor.GetScope();
    //    liteScop.LoopTime[this]++;
    //}

    protected override string _GetString()
    {
        return $"FOR {Upper.GetCodeText()}";
    }

    //public override void Assemble(Assembly.Assembler assembler)
    //{
    //    if (Count is VariableExpr vr)
    //    {
    //        if(vr.Reg == 0)throw new Assembly.AssembleException(ErrorMessage.NotSupported);
    //        assembler.Add(Assembly.Instructions.AsmMov.Create(Assembly.Assembler.IReg, (int)(vr.Reg << 4)));
    //        assembler.Add(Assembly.Instructions.AsmStoreOp.Create(Assembly.Assembler.IReg));
    //    }
    //    assembler.Add(Assembly.Instructions.AsmFor.Create());
    //    assembler.ForMapping[this] = assembler.Last() as Assembly.Instructions.AsmFor;
    //}
}

class For_Full(VariableExpr regiter, ExprBase lower, ExprBase upper) : ForStmt(upper)
{
    public VariableExpr RegIter = regiter;
    public ExprBase InitVal = lower;

    //protected override void Init(Processor processor)
    //{
    //    var liteScop = processor.GetScope();
    //    processor.Register[RegIter] = InitVal.Get(processor);
    //    liteScop.LoopCount[this] = Upper.Get(processor);
    //}

    //protected override bool Cond(Processor processor)
    //{
    //    var liteScop = processor.GetScope();
    //    return processor.Register[RegIter] < liteScop.LoopCount[this];
    //}

    //protected override void Step(Processor processor)
    //{
    //    processor.Register[RegIter]++;
    //}

    protected override string _GetString()
    {
        return $"FOR {RegIter.GetCodeText()} = {InitVal.GetCodeText()} TO {Upper.GetCodeText()}";
    }

    //public override void Assemble(Assembly.Assembler assembler)
    //{
    //    if (RegIter is VariableExpr reg)
    //        assembler.Add(Assembly.Instructions.AsmMov.Create(reg.Reg, InitVal));
    //    else 
    //        throw new Assembly.AssembleException(ErrorMessage.NotSupported);
        
    //    if (Count is VariableExpr countval)
    //    {
    //        uint e_val = countval.Reg;
    //        e_val |= countval.Reg << 4;
    //        assembler.Add(Assembly.Instructions.AsmMov.Create(Assembly.Assembler.IReg, (int)e_val));
    //        assembler.Add(Assembly.Instructions.AsmStoreOp.Create(Assembly.Assembler.IReg));
    //        assembler.Add(Assembly.Instructions.AsmFor.Create());
    //        assembler.ForMapping[this] = assembler.Last() as Assembly.Instructions.AsmFor;
    //    } 
    //    else
    //        throw new Assembly.AssembleException(ErrorMessage.NotSupported);
    //}
}

class Next : Statement
{
    protected override string _GetString() => "NEXT";

    [Obsolete]
    public ForStmt For;

    //public override void Assemble(Assembly.Assembler assembler)
    //{
    //    int val = 0;
    //    if (For.Count is InstantExpr)
    //        val = (For.Count as InstantExpr).Val;
    //    assembler.Add(Assembly.Instructions.AsmNext.Create(val));
    //    assembler.ForMapping[For].Next = assembler.Last() as Assembly.Instructions.AsmNext;
    //}
}

abstract class LoopCtrl(InstantExpr level) : Statement
{
    public readonly InstantExpr Level = level;
}

class Break : LoopCtrl
{
    public Break() :base(1) { }

    public Break(InstantExpr level) :base(level) {}

    protected override string _GetString()
    {
        return  Level.Value == 1 ? "BREAK" : $"BREAK {Level.GetCodeText()}";
    }

    //public override void Assemble(Assembly.Assembler assembler)
    //{
    //    if (Level.Val <= 0)
    //        return;
    //    assembler.Add(Assembly.Instructions.AsmBreak.Create(0, Level.Val - 1));
    //}
}

class Continue : LoopCtrl
{
    public Continue() : base(1) { }

    protected override string _GetString() => "CONTINUE";

    //public override void Assemble(Assembly.Assembler assembler)
    //{
    //    if (Level.Val <= 0)
    //        return;
    //    assembler.Add(Assembly.Instructions.AsmContinue.Create(0, Level.Val - 1));
    //}
}
