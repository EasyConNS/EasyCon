using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using System.Threading;
using EasyCon.Script.Assembly;
using EasyCon.Script.Parsing;
using EasyCon.Script.Assembly.Instructions;

namespace EasyCon.Script.Parsing.Statements
{
    class Wait : Statement
    {
        public static readonly IStatementParser Parser = new StatementParser(Parse);
        public readonly ValBase Duration;
        bool _omitted;

        public Wait(ValBase duration, bool omitted)
        {
            Duration = duration;
            _omitted = omitted;
        }

        public static Statement Parse(ParserArgument args)
        {
            int duration;
            if (int.TryParse(args.Text, out duration))
                return new Wait(duration, true);
            var m = Regex.Match(args.Text, $@"^wait\s+{Formats.ValueEx}$", RegexOptions.IgnoreCase);
            if (m.Success)
                return new Wait(args.Formatter.GetValueEx(m.Groups[1].Value), false);
            return null;
        }

        protected override string _GetString(Formats.Formatter formatter)
        {
            return _omitted ? $"{Duration.GetCodeText(formatter)}" : $"WAIT {Duration.GetCodeText(formatter)}";
        }

        public override void Exec(Processor processor)
        {
            Thread.Sleep(Duration.Get(processor));
        }

        public override void Assemble(Assembler assembler)
        {
            if (Duration is ValReg)
            {
                assembler.Add(AsmStoreOp.Create((Duration as ValReg).Index));
                assembler.Add(AsmWait.Create(0));
            }
            else if (Duration is ValInstant)
                assembler.Add(AsmWait.Create((Duration as ValInstant).Val));
            else
                throw new AssembleException(ErrorMessage.NotSupported);
        }
    }
}
