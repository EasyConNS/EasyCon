using EasyDevice;

namespace EasyCon.Script.Parsing;

abstract class KeyAction(string keyname) : Statement
{
    protected readonly string KeyName = keyname.ToUpper();
    public readonly ECKey Key = NSKeys.Get(keyname);

    //protected virtual void ReleasePrevious(Assembly.Assembler assembler)
    //{
    //    if (!assembler.KeyMapping.ContainsKey(Key.KeyCode))
    //        return;
    //    assembler.KeyMapping[Key.KeyCode].HoldUntil = assembler.Last();
    //    assembler.KeyMapping.Remove(Key.KeyCode);
    //}
}

class KeyPress : KeyAction
{
    public const int DefaultDuration = 50;

    public readonly ExprBase Duration;
    private readonly bool _omitted = false;

    public KeyPress(string key)
        : base(key)
    {
        Duration = DefaultDuration;
        _omitted = true;
    }

    public KeyPress(string key, ExprBase duration)
        : base(key)
    {
        Duration = duration;
    }

    //public override void Exec(Processor processor)
    //{
    //    var duration = Duration.Get(processor);
    //    if (duration > 0)
    //    {
    //        processor.GamePad.ClickButtons(Key, duration);
    //        Thread.Sleep(duration);
    //    }
    //}

    protected override string _GetString()
    {
        return _omitted ? $"{KeyName}" : $"{KeyName} {Duration.GetCodeText()}";
    }

    //public override void Assemble(Assembly.Assembler assembler)
    //{
    //    int keycode = Key.KeyCode;
    //    if (Duration is VariableExpr reg)
    //    {
    //        if (reg.Reg == 0)throw new Assembly.AssembleException(ErrorMessage.NotSupported);
    //        assembler.Add(Assembly.Instructions.AsmStoreOp.Create(reg.Reg));
    //        assembler.Add(Assembly.Instructions.AsmKey_Standard.Create(keycode, 0));
    //        ReleasePrevious(assembler);
    //        return;
    //    }
    //    else if (Duration is InstantExpr dur)
    //    {
    //        int duration = dur.Val;
    //        var ins = Assembly.Instructions.AsmKey_Standard.Create(keycode, duration);
    //        if (ins.Success)
    //        {
    //            assembler.Add(ins);
    //            ReleasePrevious(assembler);
    //        }
    //        else if (ins == Assembly.Instruction.Failed.OutOfRange)
    //        {
    //            assembler.Add(Assembly.Instructions.AsmKey_Hold.Create(keycode));
    //            ReleasePrevious(assembler);
    //            assembler.KeyMapping[keycode] = assembler.Last() as Assembly.Instructions.AsmKey_Hold;
    //            assembler.Add(Assembly.Instructions.AsmWait.Create(duration));
    //            assembler.Add(Assembly.Instructions.AsmEmpty.Create());
    //            ReleasePrevious(assembler);
    //        }
    //    }
    //    else
    //        throw new Assembly.AssembleException(ErrorMessage.NotSupported);
    //}
}

class KeyDown(string key) : KeyAction(key)
{
    //public override void Exec(Processor processor)
    //{
    //    processor.GamePad.PressButtons(Key);
    //}

    protected override string _GetString()
    {
        return $"{KeyName} DOWN";
    }

    //public override void Assemble(Assembly.Assembler assembler)
    //{
    //    assembler.Add(Assembly.Instructions.AsmKey_Hold.Create(Key.KeyCode));
    //    ReleasePrevious(assembler);
    //    assembler.KeyMapping[Key.KeyCode] = assembler.Last() as Assembly.Instructions.AsmKey_Hold;
    //}
}

class KeyUp(string key) : KeyAction(key)
{
    //public override void Exec(Processor processor)
    //{
    //    processor.GamePad.ReleaseButtons(Key);
    //}

    protected override string _GetString()
    {
        return $"{KeyName} UP";
    }

    //public override void Assemble(Assembly.Assembler assembler)
    //{
    //    assembler.Add(Assembly.Instructions.AsmEmpty.Create());
    //    ReleasePrevious(assembler);
    //}
}
