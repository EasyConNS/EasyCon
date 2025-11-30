using EasyDevice;
using EasyScript.Parsing;

namespace EasyScript.Statements;

abstract class KeyAction(string keyName) : Statement
{
    protected readonly ECKey Key = NSKeys.Get(keyName);

    protected virtual void ReleasePrevious(Assembly.Assembler assembler)
    {
        if (!assembler.KeyMapping.ContainsKey(Key.KeyCode))
            return;
        assembler.KeyMapping[Key.KeyCode].HoldUntil = assembler.Last();
        assembler.KeyMapping.Remove(Key.KeyCode);
    }
}

class KeyPress : KeyAction
{
    public const int DefaultDuration = 50;

    public readonly ValBase Duration;
    private readonly bool _omitted = false;

    public KeyPress(string key)
        : base(key)
    {
        Duration = DefaultDuration;
        _omitted = true;
    }

    public KeyPress(string key, ValBase duration)
        : base(key)
    {
        Duration = duration;
    }

    public override void Exec(Processor processor)
    {
        var duration = Duration.Get(processor);
        if (duration > 0)
        {
            processor.GamePad.ClickButtons(Key, duration);
            Thread.Sleep(duration);
        }
    }

    protected override string _GetString(Formatter _)
    {
        return _omitted ? $"{NSKeys.GetName(Key)}" : $"{NSKeys.GetName(Key)} {Duration.GetCodeText()}";
    }

    public override void Assemble(Assembly.Assembler assembler)
    {
        int keycode = Key.KeyCode;
        if (Duration is ValRegEx)
        {
            if (Duration is ValReg reg)
            {
                assembler.Add(Assembly.Instructions.AsmStoreOp.Create(reg.Index));
                assembler.Add(Assembly.Instructions.AsmKey_Standard.Create(keycode, 0));
                ReleasePrevious(assembler);
                return;
            }
            throw new Assembly.AssembleException(ErrorMessage.NotSupported);
        }
        else if (Duration is ValInstant dur)
        {
            int duration = dur.Val;
            var ins = Assembly.Instructions.AsmKey_Standard.Create(keycode, duration);
            if (ins.Success)
            {
                assembler.Add(ins);
                ReleasePrevious(assembler);
            }
            else if (ins == Assembly.Instruction.Failed.OutOfRange)
            {
                assembler.Add(Assembly.Instructions.AsmKey_Hold.Create(keycode));
                ReleasePrevious(assembler);
                assembler.KeyMapping[keycode] = assembler.Last() as Assembly.Instructions.AsmKey_Hold;
                assembler.Add(Assembly.Instructions.AsmWait.Create(duration));
                assembler.Add(Assembly.Instructions.AsmEmpty.Create());
                ReleasePrevious(assembler);
            }
        }
        else
            throw new Assembly.AssembleException(ErrorMessage.NotImplemented);
    }
}

class KeyDown(string key) : KeyAction(key)
{
    public override void Exec(Processor processor)
    {
        processor.GamePad.PressButtons(Key);
    }

    protected override string _GetString(Formatter formatter)
    {
        return $"{NSKeys.GetName(Key)} DOWN";
    }

    public override void Assemble(Assembly.Assembler assembler)
    {
        assembler.Add(Assembly.Instructions.AsmKey_Hold.Create(Key.KeyCode));
        ReleasePrevious(assembler);
        assembler.KeyMapping[Key.KeyCode] = assembler.Last() as Assembly.Instructions.AsmKey_Hold;
    }
}

class KeyUp(string key) : KeyAction(key)
{
    public override void Exec(Processor processor)
    {
        processor.GamePad.ReleaseButtons(Key);
    }

    protected override string _GetString(Formatter formatter)
    {
        return $"{NSKeys.GetName(Key)} UP";
    }

    public override void Assemble(Assembly.Assembler assembler)
    {
        assembler.Add(Assembly.Instructions.AsmEmpty.Create());
        ReleasePrevious(assembler);
    }
}
