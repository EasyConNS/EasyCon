using EasyCon.Script.Assembly;
using EasyCon.Script.Assembly.Instructions;
using EasyCon.Script.Parsing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace EasyCon.Script.Parsing.Statements
{
    class Mov : Statement
    {
        public static readonly IStatementParser Parser = new StatementParser(Parse);
        public readonly uint RegDst;
        public readonly ValBase Value;

        public Mov(uint regdst, ValBase value)
        {
            RegDst = regdst;
            Value = value;
        }

        public static Statement Parse(ParserArgument args)
        {
            var m = Regex.Match(args.Text, $@"^{Formats.Register}\s*=\s*{Formats.Value}$", RegexOptions.IgnoreCase);
            if (m.Success)
                return new Mov(args.Formatter.GetReg(m.Groups[1].Value, true).Index, args.Formatter.GetValue(m.Groups[2].Value));
            return null;
        }

        protected override string _GetString(Formats.Formatter formatter)
        {
            return $"{formatter.GetRegText(RegDst)} = {Value.GetCodeText(formatter)}";
        }

        public override void Exec(Processor processor)
        {
            processor.Register[RegDst] = (short)Value.Evaluate(processor);
        }

        public override void Assemble(Assembler assembler)
        {
            assembler.Add(AsmMov.Create(RegDst, Value));
        }
    }
}
