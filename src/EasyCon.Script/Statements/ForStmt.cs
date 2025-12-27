using EasyScript.Parsing;

namespace EasyScript.Statements;

abstract class ForStmt(ValBase count) : Statement
{
    public readonly ValBase Count = count;
    public Next Next;

    protected virtual void Init(Processor processor)
    { }

    protected abstract bool Cond(Processor processor);

    protected virtual void Step(Processor processor)
    { }

    public override sealed void Exec(Processor processor)
    {
        var liteScop = processor.GetScope();
        if (liteScop.LoopStack.Count == 0 || liteScop.LoopStack.Peek() != this)
        {
            // entering
            liteScop.LoopStack.Push(this);
            Init(processor);
        }
        else
        {
            // looping
            Step(processor);
        }
        if (!Cond(processor))
            processor.PC = liteScop.LoopStack.Pop().Next.Address + 1;
    }
}

class For_Infinite : ForStmt
{
    public For_Infinite()
        : base(0)
    { }

    protected override bool Cond(Processor _)
    {
        return true;
    }

    protected override string _GetString()
    {
        return $"FOR";
    }

    public override void Assemble(Assembly.Assembler assembler)
    {
        assembler.Add(Assembly.Instructions.AsmFor.Create());
        assembler.ForMapping[this] = assembler.Last() as Assembly.Instructions.AsmFor;
    }
}

class For_Static : ForStmt
{
    public For_Static(ValBase count)
        : base(count)
    { }

    protected override void Init(Processor processor)
    {
        var liteScop = processor.GetScope();
        liteScop.LoopTime[this] = 0;
        liteScop.LoopCount[this] = Count.Get(processor);
    }

    protected override bool Cond(Processor processor)
    {
        var liteScop = processor.GetScope();
        return liteScop.LoopTime[this] < liteScop.LoopCount[this];
    }

    protected override void Step(Processor processor)
    {
        var liteScop = processor.GetScope();
        liteScop.LoopTime[this]++;
    }

    protected override string _GetString()
    {
        return $"FOR {Count.GetCodeText()}";
    }

    public override void Assemble(Assembly.Assembler assembler)
    {
        if (Count is ValReg vr)
        {
            if(vr.Reg == 0)throw new Assembly.AssembleException(ErrorMessage.NotSupported);
            assembler.Add(Assembly.Instructions.AsmMov.Create(Assembly.Assembler.IReg, (int)(vr.Reg << 4)));
            assembler.Add(Assembly.Instructions.AsmStoreOp.Create(Assembly.Assembler.IReg));
        }
        assembler.Add(Assembly.Instructions.AsmFor.Create());
        assembler.ForMapping[this] = assembler.Last() as Assembly.Instructions.AsmFor;
    }
}

class For_Full : ForStmt
{
    public ValReg RegIter;
    public ValBase InitVal;

    public For_Full(ValReg regiter, ValBase initval, ValBase count)
        : base(count)
    {
        RegIter = regiter;
        InitVal = initval;
    }

    protected override void Init(Processor processor)
    {
        var liteScop = processor.GetScope();
        processor.Register[RegIter] = InitVal.Get(processor);
        liteScop.LoopCount[this] = Count.Get(processor);
    }

    protected override bool Cond(Processor processor)
    {
        var liteScop = processor.GetScope();
        return processor.Register[RegIter] < liteScop.LoopCount[this];
    }

    protected override void Step(Processor processor)
    {
        processor.Register[RegIter]++;
    }

    protected override string _GetString()
    {
        return $"FOR {RegIter.GetCodeText()} = {InitVal.GetCodeText()} TO {Count.GetCodeText()}";
    }

    public override void Assemble(Assembly.Assembler assembler)
    {
        if (RegIter is ValReg reg)
            assembler.Add(Assembly.Instructions.AsmMov.Create(reg.Reg, InitVal));
        else 
            throw new Assembly.AssembleException(ErrorMessage.NotSupported);
        
        if (Count is ValReg countval)
        {
            uint e_val = countval.Reg;
            e_val |= countval.Reg << 4;
            assembler.Add(Assembly.Instructions.AsmMov.Create(Assembly.Assembler.IReg, (int)e_val));
            assembler.Add(Assembly.Instructions.AsmStoreOp.Create(Assembly.Assembler.IReg));
            assembler.Add(Assembly.Instructions.AsmFor.Create());
            assembler.ForMapping[this] = assembler.Last() as Assembly.Instructions.AsmFor;
        } 
        else
            throw new Assembly.AssembleException(ErrorMessage.NotSupported);
    }
}

class Next : Statement
{
    public ForStmt For;

    protected override string _GetString()
    {
        return $"NEXT";
    }

    public override void Exec(Processor processor)
    {
        processor.PC = For.Address;
    }

    public override void Assemble(Assembly.Assembler assembler)
    {
        int val = 0;
        if (For.Count is ValInstant)
            val = (For.Count as ValInstant).Val;
        assembler.Add(Assembly.Instructions.AsmNext.Create(val));
        assembler.ForMapping[For].Next = assembler.Last() as Assembly.Instructions.AsmNext;
    }
}

abstract class LoopControl : Statement
{
    public readonly ValInstant Level;
    protected bool _omitted;

    public LoopControl()
    {
        Level = 1;
        _omitted = true;
    }

    public LoopControl(ValInstant level)
    {
        Level = level;
        _omitted = false;
    }
}

class Break : LoopControl
{
    public Break()
    { }

    public Break(ValInstant level)
        : base(level)
    { }

    protected override string _GetString()
    {
        return _omitted || Level.Val == 1 ? $"BREAK" : $"BREAK {Level.GetCodeText()}";
    }

    public override void Exec(Processor processor)
    {
        var liteScop = processor.GetScope();
        for (int i = 0; i < Level.Val - 1; i++)
            liteScop.LoopStack.Pop();
        processor.PC = liteScop.LoopStack.Pop().Next.Address + 1;
    }

    public override void Assemble(Assembly.Assembler assembler)
    {
        if (Level.Val <= 0)
            return;
        assembler.Add(Assembly.Instructions.AsmBreak.Create(0, Level.Val - 1));
    }
}

class Continue : LoopControl
{
    public Continue()
    { }

    protected override string _GetString()
    {
        return _omitted ? $"CONTINUE" : $"CONTINUE {Level.GetCodeText()}";
    }

    public override void Exec(Processor processor)
    {
        var liteScop = processor.GetScope();
        for (int i = 0; i < Level.Val - 1; i++)
            liteScop.LoopStack.Pop();
        processor.PC = liteScop.LoopStack.Peek().Next.Address;
    }

    public override void Assemble(Assembly.Assembler assembler)
    {
        if (Level.Val <= 0)
            return;
        assembler.Add(Assembly.Instructions.AsmContinue.Create(0, Level.Val - 1));
    }
}
