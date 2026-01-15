using EasyDevice;

namespace EasyCon.Script.Parsing;

abstract class StickAction(string keyname, string direction) : KeyAction(keyname)
{
    public override ECKey Key => NSKeys.GetKey(keyname, direction);
    protected readonly string Direction = direction.ToUpper();

    //protected virtual void ReleasePrevious(Assembly.Assembler assembler)
    //{
    //    if (!assembler.StickMapping.ContainsKey(Key.KeyCode))
    //        return;
    //    assembler.StickMapping[Key.KeyCode].HoldUntil = assembler.Last();
    //    assembler.StickMapping.Remove(Key.KeyCode);
    //}


    static int GetDirectionIndex(int x, int y)
    {
        if (x == SwitchStick.STICK_CENTER && y == SwitchStick.STICK_CENTER)
            return -1;
        x = (int)Math.Round(x / 32d);
        y = (int)Math.Round(y / 32d);
        return x >= y ? x + y : 32 - x - y;
    }
}

class StickPress(string keyname, string direction, ExprBase duration) : StickAction(keyname, direction)
{
    public readonly ExprBase Duration = duration;

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
        return $"{KeyName} {Direction},{Duration.GetCodeText()}";
    }

    //public override void Assemble(Assembly.Assembler assembler)
    //{
    //    int keycode = Key.KeyCode;
    //    int dindex = GetDirectionIndex(Key.StickX, Key.StickY);   
    //    if (Duration is VariableExpr reg)
    //    {
    //        if(reg.Reg == 0)throw new Assembly.AssembleException(ErrorMessage.NotSupported);
    //        assembler.Add(Assembly.Instructions.AsmStoreOp.Create(reg.Reg));
    //        assembler.Add(Assembly.Instructions.AsmStick_Standard.Create(keycode, dindex, 0));
    //        ReleasePrevious(assembler);
    //    }
    //    else if (Duration is InstantExpr dur)
    //    {
    //        int duration = dur.Val;
    //        var ins = Assembly.Instructions.AsmStick_Standard.Create(keycode, dindex, duration);
    //        if (ins.Success)
    //        {
    //            assembler.Add(ins);
    //            ReleasePrevious(assembler);
    //        }
    //        else if (ins == Assembly.Instruction.Failed.OutOfRange)
    //        {
    //            assembler.Add(Assembly.Instructions.AsmStick_Hold.Create(keycode, dindex));
    //            ReleasePrevious(assembler);
    //            assembler.StickMapping[keycode] = assembler.Last() as Assembly.Instructions.AsmStick_Hold;
    //            assembler.Add(Assembly.Instructions.AsmWait.Create(duration));
    //            assembler.Add(Assembly.Instructions.AsmEmpty.Create());
    //            ReleasePrevious(assembler);
    //        }
    //    }
    //    else
    //        throw new Assembly.AssembleException(ErrorMessage.NotSupported);
    //}
}

class StickDown(string keyname, string direction) : StickAction(keyname, direction)
{
    //public override void Exec(Processor processor)
    //{
    //    processor.GamePad.PressButtons(Key);
    //}

    protected override string _GetString()
    {
        return $"{KeyName} {Direction}";
    }

    //public override void Assemble(Assembly.Assembler assembler)
    //{
    //    assembler.Add(Assembly.Instructions.AsmStick_Hold.Create(Key.KeyCode, GetDirectionIndex(Key.StickX, Key.StickY)));
    //    ReleasePrevious(assembler);
    //    assembler.StickMapping[Key.KeyCode] = assembler.Last() as Assembly.Instructions.AsmStick_Hold;
    //}
}

class StickUp(string keyname) : StickAction(keyname, "0")
{

    //public override void Exec(Processor processor)
    //{
    //    processor.GamePad.ReleaseButtons(Key);
    //}

    protected override string _GetString()
    {
        return $"{KeyName} RESET";
    }

    //public override void Assemble(Assembly.Assembler assembler)
    //{
    //    assembler.Add(Assembly.Instructions.AsmEmpty.Create());
    //    ReleasePrevious(assembler);
    //}
}
