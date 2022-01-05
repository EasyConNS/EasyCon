using System.Text.RegularExpressions;

namespace EasyCon2.Script.Parsing.Statements
{
    abstract class StickAction : Statement
    {
        public static readonly IStatementParser Parser = new StatementParser(Parse);
        public readonly ECDevice.NintendoSwitch.Key Key;
        public readonly string KeyName;
        public readonly string Direction;

        public StickAction(ECDevice.NintendoSwitch.Key key, string keyname, string direcion)
        {
            Key = key;
            KeyName = keyname?.ToUpper();
            Direction = direcion?.ToUpper();
        }

        public static Statement Parse(ParserArgument args)
        {
            Match m;
            m = Regex.Match(args.Text, @"^([lr]s)\s+(reset)$", RegexOptions.IgnoreCase);
            if (m.Success)
            {
                var keyname = m.Groups[1].Value;
                var key = ScripterUtil.GetKey(keyname);
                if (key == null)
                    return null;
                return new StickUp(key, keyname);
            }
            m = Regex.Match(args.Text, @"^([lr]s)\s+([a-z0-9]+)$", RegexOptions.IgnoreCase);
            if (m.Success)
            {
                var keyname = m.Groups[1].Value;
                var direction = m.Groups[2].Value;
                var key = ScripterUtil.GetKey(keyname, direction);
                if (key == null)
                    return null;
                return new StickDown(key, keyname, direction);
            }
            m = Regex.Match(args.Text, $@"^([lr]s)\s+([a-z0-9]+)\s*,\s*({Formats.ValueEx})$", RegexOptions.IgnoreCase);
            if (m.Success)
            {
                var keyname = m.Groups[1].Value;
                var direction = m.Groups[2].Value;
                var duration = m.Groups[3].Value;
                var key = ScripterUtil.GetKey(keyname, direction);
                if (key == null)
                    return null;
                return new StickPress(key, keyname, direction, args.Formatter.GetValueEx(duration));
            }
            return null;
        }

        protected void ReleasePrevious(Assembly.Assembler assembler)
        {
            if (!assembler.StickMapping.ContainsKey(Key.KeyCode))
                return;
            assembler.StickMapping[Key.KeyCode].HoldUntil = assembler.Last();
            assembler.StickMapping.Remove(Key.KeyCode);
        }
    }

    class StickPress : StickAction
    {
        public readonly ValBase Duration;

        public StickPress(ECDevice.NintendoSwitch.Key key, string keyname, string direction, ValBase duration)
            : base(key, keyname, direction)
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

        protected override string _GetString(Formatter formatter)
        {
            return $"{KeyName} {Direction},{Duration.GetCodeText(formatter)}";
        }

        public override void Assemble(Assembly.Assembler assembler)
        {
            int keycode = Key.KeyCode;
            int dindex = ScripterUtil.GetDirectionIndex(Key);
            if (Duration is ValRegEx)
            {
                if (Duration is ValReg32)
                    throw new Assembly.AssembleException(ErrorMessage.NotSupported);
                var reg = Duration as ValRegEx;
                assembler.Add(Assembly.Instructions.AsmStoreOp.Create(reg.Index));
                assembler.Add(Assembly.Instructions.AsmStick_Standard.Create(keycode, dindex, 0));
                ReleasePrevious(assembler);
            }
            else if (Duration is ValInstant)
            {
                int duration = (Duration as ValInstant).Val;
                var ins = Assembly.Instructions.AsmStick_Standard.Create(keycode, dindex, duration);
                if (ins.Success)
                {
                    assembler.Add(ins);
                    ReleasePrevious(assembler);
                }
                else if (ins == Assembly.Instruction.Failed.OutOfRange)
                {
                    assembler.Add(Assembly.Instructions.AsmStick_Hold.Create(keycode, dindex));
                    ReleasePrevious(assembler);
                    assembler.StickMapping[keycode] = assembler.Last() as Assembly.Instructions.AsmStick_Hold;
                    assembler.Add(Assembly.Instructions.AsmWait.Create(duration));
                    assembler.Add(Assembly.Instructions.AsmEmpty.Create());
                    ReleasePrevious(assembler);
                }
            }
            else
                throw new Assembly.AssembleException(ErrorMessage.NotImplemented);
        }
    }

    class StickDown : StickAction
    {
        public StickDown(ECDevice.NintendoSwitch.Key key, string keyname, string direction)
            : base(key, keyname, direction)
        { }

        public override void Exec(Processor processor)
        {
            processor.GamePad.PressButtons(Key);
        }

        protected override string _GetString(Formatter formatter)
        {
            return $"{KeyName} {Direction}";
        }

        public override void Assemble(Assembly.Assembler assembler)
        {
            assembler.Add(Assembly.Instructions.AsmStick_Hold.Create(Key.KeyCode, ScripterUtil.GetDirectionIndex(Key)));
            ReleasePrevious(assembler);
            assembler.StickMapping[Key.KeyCode] = assembler.Last() as Assembly.Instructions.AsmStick_Hold;
        }
    }

    class StickUp : StickAction
    {
        public StickUp(ECDevice.NintendoSwitch.Key key, string keyname)
            : base(key, keyname, null)
        { }

        public override void Exec(Processor processor)
        {
            processor.GamePad.ReleaseButtons(Key);
        }

        protected override string _GetString(Formatter formatter)
        {
            return $"{KeyName} RESET";
        }

        public override void Assemble(Assembly.Assembler assembler)
        {
            assembler.Add(Assembly.Instructions.AsmEmpty.Create());
            ReleasePrevious(assembler);
        }
    }
}
