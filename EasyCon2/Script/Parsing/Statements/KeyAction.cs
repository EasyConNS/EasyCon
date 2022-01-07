using ECDevice;
using System.Text.RegularExpressions;

namespace EasyCon2.Script.Parsing.Statements
{
    abstract class KeyAction : Statement
    {
        public static readonly IStatementParser Parser = new StatementParser(Parse);
        protected readonly NintendoSwitch.Key Key;

        public KeyAction(NintendoSwitch.Key key)
        {
            Key = key;
        }

        public static Statement Parse(ParserArgument args)
        {
            NintendoSwitch.Key key;
            Match m;
            m = Regex.Match(args.Text, @"^([a-z]+)$", RegexOptions.IgnoreCase);
            if (m.Success && (key = NSKeys.Get(m.Groups[1].Value)) != null)
                return new KeyPress(key);
            m = Regex.Match(args.Text, $@"^([a-z]+)\s+{Formats.ValueEx}$", RegexOptions.IgnoreCase);
            if (m.Success && (key = NSKeys.Get(m.Groups[1].Value)) != null)
                return new KeyPress(key, args.Formatter.GetValueEx(m.Groups[2].Value));
            m = Regex.Match(args.Text, @"^([a-z]+)\s+down$", RegexOptions.IgnoreCase);
            if (m.Success && (key = NSKeys.Get(m.Groups[1].Value)) != null)
                return new KeyDown(key);
            m = Regex.Match(args.Text, @"^([a-z]+)\s+up$", RegexOptions.IgnoreCase);
            if (m.Success && (key = NSKeys.Get(m.Groups[1].Value)) != null)
                return new KeyUp(key);
            return null;
        }

        protected void ReleasePrevious(Assembly.Assembler assembler)
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
        bool _omitted = false;

        public KeyPress(NintendoSwitch.Key key)
            : base(key)
        {
            Duration = DefaultDuration;
            _omitted = true;
        }

        public KeyPress(NintendoSwitch.Key key, ValBase duration)
            : base(key)
        {
            Duration = duration;
        }

        public override void Exec(Processor processor)
        {
            var duration = Duration.Get(processor);
            if (duration > 0)
            {
                NintendoSwitch.GetInstance().Press(Key, duration);
                Thread.Sleep(duration);
            }
        }

        protected override string _GetString(Formatter formatter)
        {
            return _omitted ? $"{NSKeys.GetName(Key)}" : $"{NSKeys.GetName(Key)} {Duration.GetCodeText(formatter)}";
        }

        public override void Assemble(Assembly.Assembler assembler)
        {
            int keycode = Key.KeyCode;
            if (Duration is ValRegEx)
            {
                if (Duration is ValReg32)
                    throw new Assembly.AssembleException(ErrorMessage.NotSupported);
                var reg = Duration as ValReg;
                assembler.Add(Assembly.Instructions.AsmStoreOp.Create(reg.Index));
                assembler.Add(Assembly.Instructions.AsmKey_Standard.Create(keycode, 0));
                ReleasePrevious(assembler);
            }
            else if (Duration is ValInstant)
            {
                int duration = (Duration as ValInstant).Val;
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

    class KeyDown : KeyAction
    {
        public KeyDown(NintendoSwitch.Key key)
            : base(key)
        { }

        public override void Exec(Processor processor)
        {
            NintendoSwitch.GetInstance().Down(Key);
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

    class KeyUp : KeyAction
    {
        public KeyUp(NintendoSwitch.Key key)
            : base(key)
        { }

        public override void Exec(Processor processor)
        {
            NintendoSwitch.GetInstance().Up(Key);
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
}
