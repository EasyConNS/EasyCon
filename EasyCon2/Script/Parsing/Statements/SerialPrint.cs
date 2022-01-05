using System.Text.RegularExpressions;

namespace EasyCon2.Script.Parsing.Statements
{
    class SerialPrint : Statement
    {
#if DEBUG
        public static readonly IStatementParser Parser = new StatementParser(Parse);
#endif
        public readonly uint Value;
        public readonly bool Mem;

        public SerialPrint(uint value, bool mem)
        {
            Value = value;
            Mem = mem;
        }

        public static Statement Parse(ParserArgument args)
        {
            var m = Regex.Match(args.Text, @"^sprint\s+(\d+)$", RegexOptions.IgnoreCase);
            if (m.Success)
                return new SerialPrint(uint.Parse(m.Groups[1].Value), false);
            m = Regex.Match(args.Text, @"^smem\s+(\d+)$", RegexOptions.IgnoreCase);
            if (m.Success)
                return new SerialPrint(uint.Parse(m.Groups[1].Value), true);
            return null;
        }

        protected override string _GetString(Formats.Formatter formatter)
        {
            return Mem ? $"SMEM {Value}" : $"SPRINT {Value}";
        }

        public override void Exec(Processor processor)
        {
            //processor.Output.Print(Value.ToString());
        }

        public override void Assemble(Assembly.Assembler assembler)
        {
            assembler.Add(Assembly.Instructions.AsmSerialPrint.Create(Mem ? 1u : 0, Value));
        }
    }
}
