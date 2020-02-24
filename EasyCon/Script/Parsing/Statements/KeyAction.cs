using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using EasyCon.Script.Assembly;
using EasyCon.Script.Assembly.Instructions;
using EasyCon.Script.Parsing;
using PTDevice;

namespace EasyCon.Script.Parsing.Statements
{
    abstract class KeyAction : Statement
    {
        public static readonly IStatementParser Parser = new StatementParser(Parse);
        public readonly NintendoSwitch.Key Key;

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
            m = Regex.Match(args.Text, $@"^([a-z]+)\s+{Formats.Value}$", RegexOptions.IgnoreCase);
            if (m.Success && (key = NSKeys.Get(m.Groups[1].Value)) != null)
                return new KeyPress(key, args.Formatter.GetValue(m.Groups[2].Value));
            m = Regex.Match(args.Text, @"^([a-z]+)\s+down$", RegexOptions.IgnoreCase);
            if (m.Success && (key = NSKeys.Get(m.Groups[1].Value)) != null)
                return new KeyDown(key);
            m = Regex.Match(args.Text, @"^([a-z]+)\s+up$", RegexOptions.IgnoreCase);
            if (m.Success && (key = NSKeys.Get(m.Groups[1].Value)) != null)
                return new KeyUp(key);
            return null;
        }

        protected void ReleasePrevious(Assembler assembler)
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
            var duration = Duration.Evaluate(processor);
            if (duration > 0)
            {
                NintendoSwitch.GetInstance().Press(Key, duration);
                Thread.Sleep(duration);
            }
        }

        protected override string _GetString(Formats.Formatter formatter)
        {
            return _omitted ? $"{NSKeys.GetName(Key)}" : $"{NSKeys.GetName(Key)} {Duration.GetCodeText(formatter)}";
        }

        public override void Assemble(Assembler assembler)
        {
            int keycode = Key.KeyCode;
            var reg = Duration as ValReg;
            if (reg != null)
            {
                assembler.Add(AsmStoreOp.Create(reg.Index));
                assembler.Add(AsmKey_Standard.Create(keycode, 0));
                ReleasePrevious(assembler);
            }
            else
            {
                int duration = (Duration as ValInstant).Val;
                var ins = AsmKey_Standard.Create(keycode, duration);
                if (ins.Success)
                {
                    assembler.Add(ins);
                    ReleasePrevious(assembler);
                }
                else if (ins == Instruction.Failed.OutOfRange)
                {
                    assembler.Add(AsmKey_Hold.Create(keycode));
                    ReleasePrevious(assembler);
                    assembler.KeyMapping[keycode] = assembler.Last() as AsmKey_Hold;
                    assembler.Add(AsmWait.Create(duration));
                    assembler.Add(AsmEmpty.Create());
                    ReleasePrevious(assembler);
                }
            }
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

        protected override string _GetString(Formats.Formatter formatter)
        {
            return $"{NSKeys.GetName(Key)} DOWN";
        }

        public override void Assemble(Assembler assembler)
        {
            assembler.Add(AsmKey_Hold.Create(Key.KeyCode));
            ReleasePrevious(assembler);
            assembler.KeyMapping[Key.KeyCode] = assembler.Last() as AsmKey_Hold;
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

        protected override string _GetString(Formats.Formatter formatter)
        {
            return $"{NSKeys.GetName(Key)} UP";
        }

        public override void Assemble(Assembler assembler)
        {
            assembler.Add(AsmEmpty.Create());
            ReleasePrevious(assembler);
        }
    }
}
