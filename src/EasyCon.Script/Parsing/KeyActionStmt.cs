using EasyScript;

namespace EasyCon.Script.Syntax;

abstract class KeyActionStmt(Token syntax, bool up = false) : Statement(syntax)
{
    protected virtual string KeyName => Syntax.Value.ToUpper();
    public virtual GamePadKey Key => NSKeys.GetKey(KeyName);

    public virtual bool Up => up;

    protected string upText => Up ? "UP" : "DOWN";

    //protected virtual void ReleasePrevious(Assembly.Assembler assembler)
    //{
    //    if (!assembler.KeyMapping.ContainsKey(Key.KeyCode))
    //        return;
    //    assembler.KeyMapping[Key.KeyCode].HoldUntil = assembler.Last();
    //    assembler.KeyMapping.Remove(Key.KeyCode);
    //}
}

interface IDurationKey
{
    public ExprBase Duration { get; }
}

class KeyPress : KeyActionStmt, IDurationKey
{
    private const int DefaultDuration = 50;

    public ExprBase Duration { get; }
    private readonly bool _omitted = false;

    public KeyPress(Token syntax)
        : base(syntax)
    {
        _omitted = true;
        Duration = new LiteralExpr(syntax, DefaultDuration);
    }

    public KeyPress(Token syntax, ExprBase duration)
        : base(syntax)
    {
        Duration = duration;
    }

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

class KeyAct(Token syntax, bool up = false) : KeyActionStmt(syntax, up)
{
    //public override void Exec(Processor processor)
    //{
    //    processor.GamePad.PressButtons(Key);
    //}

    protected override string _GetString()
    {
        return $"{KeyName} {upText}";
    }

    //public override void Assemble(Assembly.Assembler assembler)
    //{
    //    assembler.Add(Assembly.Instructions.AsmKey_Hold.Create(Key.KeyCode));
    //    ReleasePrevious(assembler);
    //    assembler.KeyMapping[Key.KeyCode] = assembler.Last() as Assembly.Instructions.AsmKey_Hold;
    //}

    //public override void Exec(Processor processor)
    //{
    //    processor.GamePad.ReleaseButtons(Key);
    //}

    //public override void Assemble(Assembly.Assembler assembler)
    //{
    //    assembler.Add(Assembly.Instructions.AsmEmpty.Create());
    //    ReleasePrevious(assembler);
    //}
}

abstract class StickActionStmt : KeyActionStmt
{
    protected readonly string Direction;
    public readonly int Degree;

    public StickActionStmt(Token syntax, string direction) : base(syntax)
    {
        Direction = direction;
        _ = NSKeys.CheckDirection(Direction, out var degree);
        Degree = degree;
    }

    //protected virtual void ReleasePrevious(Assembly.Assembler assembler)
    //{
    //    if (!assembler.StickMapping.ContainsKey(Key.KeyCode))
    //        return;
    //    assembler.StickMapping[Key.KeyCode].HoldUntil = assembler.Last();
    //    assembler.StickMapping.Remove(Key.KeyCode);
    //}


    // static int GetDirectionIndex(int x, int y)
    // {
    //     if (x == SwitchStick.STICK_CENTER && y == SwitchStick.STICK_CENTER)
    //         return -1;
    //     x = (int)Math.Round(x / 32d);
    //     y = (int)Math.Round(y / 32d);
    //     return x >= y ? x + y : 32 - x - y;
    // }
}

class StickPress(Token syntax, string direction, ExprBase duration) : StickActionStmt(syntax, direction), IDurationKey
{
    public ExprBase Duration => duration;

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

class StickAct(Token syntax, string direction) : StickActionStmt(syntax, direction)
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

    //public override void Exec(Processor processor)
    //{
    //    processor.GamePad.ReleaseButtons(Key);
    //}

    //public override void Assemble(Assembly.Assembler assembler)
    //{
    //    assembler.Add(Assembly.Instructions.AsmEmpty.Create());
    //    ReleasePrevious(assembler);
    //}
}