using EasyCon.Script.Assembly;
using EasyCon.Script.Assembly.Instructions;
using EasyCon.Script.Parsing;
using PTDevice;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace EasyCon.Script.Parsing.Statements
{
    abstract class StickAction : Statement
    {
        public static readonly IStatementParser Parser = new StatementParser(Parse);
        public readonly NintendoSwitch.Key Key;
        public readonly string KeyName;
        public readonly string Direction;

        public StickAction(NintendoSwitch.Key key, string keyname, string direcion)
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
                var key = GetKey(keyname);
                if (key == null)
                    return null;
                return new StickUp(key, keyname);
            }
            m = Regex.Match(args.Text, @"^([lr]s)\s+([a-z0-9]+)$", RegexOptions.IgnoreCase);
            if (m.Success)
            {
                var keyname = m.Groups[1].Value;
                var direction = m.Groups[2].Value;
                var key = GetKey(keyname, direction);
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
                var key = GetKey(keyname, direction);
                if (key == null)
                    return null;
                return new StickPress(key, keyname, direction, args.Formatter.GetValueEx(duration));
            }
            return null;
        }

        static NintendoSwitch.Key GetKey(string keyname, string direction = "0")
        {
            int degree;
            if (int.TryParse(direction, out degree))
            {
                if (keyname.Equals("LS", StringComparison.OrdinalIgnoreCase))
                    return NintendoSwitch.Key.LStick(degree);
                else
                    return NintendoSwitch.Key.RStick(degree);
            }
            else
            {
                var dk = NSKeys.GetDirection(direction);
                if (dk == NintendoSwitch.DirectionKey.None)
                    return null;
                if (keyname.Equals("LS", StringComparison.OrdinalIgnoreCase))
                    return NintendoSwitch.Key.LStick(dk);
                else
                    return NintendoSwitch.Key.RStick(dk);
            }
        }

        protected void ReleasePrevious(Assembler assembler)
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

        public StickPress(NintendoSwitch.Key key, string keyname, string direction, ValBase duration)
            : base(key, keyname, direction)
        {
            Duration = duration;
        }

        public override void Exec(Processor processor)
        {
            var duration = Duration.Evaluate(processor);
            if (duration > 0)
            {
                NintendoSwitch.GetInstance().Press(Key, duration);
                Thread.Sleep(duration);
            }
        }

        protected override string _GetString(Formats.Formatter formatter)
        {
            return $"{KeyName} {Direction},{Duration.GetCodeText(formatter)}";
        }

        public override void Assemble(Assembler assembler)
        {
            int keycode = Key.KeyCode;
            int dindex = Assembler.GetDirectionIndex(Key);
            if (Duration is ValRegEx)
            {
                if (Duration is ValReg32)
                    throw new AssembleException(ErrorMessage.NotSupported);
                var reg = Duration as ValRegEx;
                assembler.Add(AsmStoreOp.Create(reg.Index));
                assembler.Add(AsmStick_Standard.Create(keycode, dindex, 0));
                ReleasePrevious(assembler);
            }
            else if (Duration is ValInstant)
            {
                int duration = (Duration as ValInstant).Val;
                var ins = AsmStick_Standard.Create(keycode, dindex, duration);
                if (ins.Success)
                {
                    assembler.Add(ins);
                    ReleasePrevious(assembler);
                }
                else if (ins == Instruction.Failed.OutOfRange)
                {
                    assembler.Add(AsmStick_Hold.Create(keycode, dindex));
                    ReleasePrevious(assembler);
                    assembler.StickMapping[keycode] = assembler.Last() as AsmStick_Hold;
                    assembler.Add(AsmWait.Create(duration));
                    assembler.Add(AsmEmpty.Create());
                    ReleasePrevious(assembler);
                }
            }
            else
                throw new AssembleException(ErrorMessage.NotImplemented);
        }
    }

    class StickDown : StickAction
    {
        public StickDown(NintendoSwitch.Key key, string keyname, string direction)
            : base(key, keyname, direction)
        { }

        public override void Exec(Processor processor)
        {
            NintendoSwitch.GetInstance().Down(Key);
        }

        protected override string _GetString(Formats.Formatter formatter)
        {
            return $"{KeyName} {Direction}";
        }

        public override void Assemble(Assembler assembler)
        {
            assembler.Add(AsmStick_Hold.Create(Key.KeyCode, Assembler.GetDirectionIndex(Key)));
            ReleasePrevious(assembler);
            assembler.StickMapping[Key.KeyCode] = assembler.Last() as AsmStick_Hold;
        }
    }

    class StickUp : StickAction
    {
        public StickUp(NintendoSwitch.Key key, string keyname)
            : base(key, keyname, null)
        { }

        public override void Exec(Processor processor)
        {
            NintendoSwitch.GetInstance().Up(Key);
        }

        protected override string _GetString(Formats.Formatter formatter)
        {
            return $"{KeyName} RESET";
        }

        public override void Assemble(Assembler assembler)
        {
            assembler.Add(AsmEmpty.Create());
            ReleasePrevious(assembler);
        }
    }
}
