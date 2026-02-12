using System.Collections.Immutable;

namespace EasyCon.Script.Parsing;

internal sealed class ForBlock(ForStmt condition, ImmutableArray<Statement> statements, EndBlockStmt end) : Statement
{
    public readonly ForStmt Condition = condition;
    public ImmutableArray<Statement> Statements = statements;
    public readonly EndBlockStmt End = end;

    protected override string _GetString()
    {
        throw new NotImplementedException();
    }
}

abstract class ForStmt(ExprBase lower, ExprBase upper) : StartBlockStmt
{
    public readonly ExprBase Lower = lower;
    public readonly ExprBase Upper = upper;

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
        : base(0,0)
    { }

    protected override string _GetString() => "FOR";

    //public override void Assemble(Assembly.Assembler assembler)
    //{
    //    assembler.Add(Assembly.Instructions.AsmFor.Create());
    //    assembler.ForMapping[this] = assembler.Last() as Assembly.Instructions.AsmFor;
    //}
}

class For_Static(ExprBase count) : ForStmt(0, count)
{
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

class For_Full(VariableExpr regiter, ExprBase lower, ExprBase upper) : ForStmt(lower, upper)
{
    public VariableExpr RegIter = regiter;

    protected override string _GetString()
    {
        return $"FOR {RegIter.GetCodeText()} = {Lower.GetCodeText()} TO {Upper.GetCodeText()}";
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

class Next : EndBlockStmt
{
    protected override string _GetString() => "NEXT";

    //public override void Assemble(Assembly.Assembler assembler)
    //{
    //    int val = 0;
    //    if (For.Count is InstantExpr)
    //        val = (For.Count as InstantExpr).Val;
    //    assembler.Add(Assembly.Instructions.AsmNext.Create(val));
    //    assembler.ForMapping[For].Next = assembler.Last() as Assembly.Instructions.AsmNext;
    //}
}

internal sealed class WhileBlock(WhileStmt condition, ImmutableArray<Statement> statements, EndBlockStmt end) : Statement
{
    public readonly WhileStmt Condition = condition;
    public ImmutableArray<Statement> Statements = statements;
    public readonly EndBlockStmt End = end;

    protected override string _GetString()
    {
        throw new NotImplementedException();
    }
}

class WhileStmt(ExprBase conds) : StartBlockStmt
{
    public readonly ExprBase Condition = conds;

    protected override string _GetString()
    {
        return $"WHILE {Condition.GetCodeText()}";
    }
}

abstract class LoopCtrl(uint level) : Statement
{
    public readonly uint Level = level;
}

class Break : LoopCtrl
{
    public Break() :base(1) { }

    public Break(uint level) :base(level) {}

    protected override string _GetString()
    {
        if (Level == 1) return "BREAK";
        return  $"BREAK {Level}";
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
