using System.Collections.Immutable;

namespace EasyCon.Script.Syntax;

internal sealed class ForBlock(ForStmt condition, ImmutableArray<Statement> statements, EndBlockStmt end) : Statement(condition.Syntax)
{
    public readonly ForStmt Condition = condition;
    public ImmutableArray<Statement> Statements = statements;
    public readonly EndBlockStmt End = end;

    protected override string _GetString()
    {
        throw new NotImplementedException();
    }
}

abstract class ForStmt(Token syntax, ExprBase lower, ExprBase upper) : StartBlockStmt(syntax)
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
    public For_Infinite(Token syntax)
        : base(syntax, 0,0)
    { }

    protected override string _GetString() => "FOR";

    //public override void Assemble(Assembly.Assembler assembler)
    //{
    //    assembler.Add(Assembly.Instructions.AsmFor.Create());
    //    assembler.ForMapping[this] = assembler.Last() as Assembly.Instructions.AsmFor;
    //}
}

class For_Static(Token syntax, ExprBase count) : ForStmt(syntax, 1, count)
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

class For_Range(Token syntax, VariableExpr regiter, ExprBase iter): ForStmt(syntax, iter, 1)
{
    public VariableExpr RegIter = regiter;
    protected override string _GetString()
    {
        return $"FOR {RegIter.GetCodeText()} = RANGE {Lower.GetCodeText()}";
    }
}

class For_Full(Token syntax, VariableExpr regiter, ExprBase lower, ExprBase upper) : ForStmt(syntax, lower, upper)
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

class Next(Token syntax) : EndBlockStmt(syntax)
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

internal sealed class WhileBlock(WhileStmt condition, ImmutableArray<Statement> statements, EndBlockStmt end) : Statement(condition.Syntax)
{
    public readonly WhileStmt Condition = condition;
    public ImmutableArray<Statement> Statements = statements;
    public readonly EndBlockStmt End = end;

    protected override string _GetString()
    {
        throw new NotImplementedException();
    }
}

class WhileStmt(Token syntax, ExprBase conds) : StartBlockStmt(syntax)
{
    public readonly ExprBase Condition = conds;

    protected override string _GetString()
    {
        return $"WHILE {Condition.GetCodeText()}";
    }
}

abstract class LoopCtrl(Token syntax, uint level) : Statement(syntax)
{
    public readonly uint Level = level;
}

class Break : LoopCtrl
{
    public Break(Token syntax) :base(syntax, 1) { }

    public Break(Token syntax, uint level) :base(syntax, level) {}

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
    public Continue(Token syntax) : base(syntax, 1) { }

    protected override string _GetString() => "CONTINUE";

    //public override void Assemble(Assembly.Assembler assembler)
    //{
    //    if (Level.Val <= 0)
    //        return;
    //    assembler.Add(Assembly.Instructions.AsmContinue.Create(0, Level.Val - 1));
    //}
}
