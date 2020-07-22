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
        public readonly ValRegEx RegDst;
        public readonly ValBase Value;

        public Mov(ValRegEx regdst, ValBase value)
        {
            RegDst = regdst;
            Value = value;
        }

        public static Statement Parse(ParserArgument args)
        {
            var m = Regex.Match(args.Text, $@"^{Formats.RegisterEx}\s*=\s*{Formats.ValueEx}$", RegexOptions.IgnoreCase);
            if (m.Success)
                return new Mov(args.Formatter.GetRegEx(m.Groups[1].Value, true), args.Formatter.GetValueEx(m.Groups[2].Value));
            return null;
        }

        protected override string _GetString(Formats.Formatter formatter)
        {
            return $"{RegDst.GetCodeText(formatter)} = {Value.GetCodeText(formatter)}";
        }

        public override void Exec(Processor processor)
        {
            processor.Register[RegDst] = Value.Get(processor);
        }

        public override void Assemble(Assembler assembler)
        {
            if (RegDst is ValReg32)
                throw new AssembleException(ErrorMessage.NotSupported);
            assembler.Add(AsmMov.Create(RegDst.Index, Value));
        }
    }
}
