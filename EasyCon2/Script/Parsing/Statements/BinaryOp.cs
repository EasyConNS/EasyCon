using System.Text.RegularExpressions;

namespace EasyCon2.Script.Parsing.Statements
{
    abstract class BinaryOp : Statement
    {
        protected class Meta
        {
            public readonly Type StatementType;
            public readonly Type InstructionType;
            public readonly string Operator;
            public readonly Func<int, int, int> Function;
            public readonly bool OnlyInstant;

            public Meta(Type statementType, Type instructionType, string @operator, Func<int, int, int> function, bool onlyInstant = false)
            {
                StatementType = statementType;
                InstructionType = instructionType;
                Operator = @operator;
                Function = function;
                OnlyInstant = onlyInstant;
            }
        }

        protected class BinaryOpParser : IStatementParser
        {
            readonly Meta _meta;

            public BinaryOpParser(Meta meta)
            {
                _meta = meta;
            }

            public Statement Parse(ParserArgument args)
            {
                var m = Regex.Match(args.Text, $@"^{Formats.RegisterEx}\s*\{_meta.Operator}\s*{(_meta.OnlyInstant ? Formats.Instant : Formats.ValueEx)}$", RegexOptions.IgnoreCase);
                if (m.Success)
                    return Activator.CreateInstance(_meta.StatementType, Formatter.GetRegEx(m.Groups[1].Value, true), args.Formatter.GetValueEx(m.Groups[2].Value)) as Statement;
                return null;
            }
        }

        protected abstract Meta MetaInfo { get; }
        public readonly ValRegEx RegDst;
        public readonly ValBase Value;

        public BinaryOp(ValRegEx regdst, ValBase value)
        {
            RegDst = regdst;
            Value = value;
        }

        protected override string _GetString(Formatter formatter)
        {
            return $"{RegDst.GetCodeText(formatter)} {MetaInfo.Operator} {Value.GetCodeText(formatter)}";
        }

        public override void Exec(Processor processor)
        {
            processor.Register[RegDst] = MetaInfo.Function(processor.Register[RegDst], Value.Get(processor));
        }

        public override void Assemble(Assembly.Assembler assembler)
        {
            assembler.Add(Assembly.Instruction.CreateInstance(MetaInfo.InstructionType, RegDst.Index, Value));
        }
    }

    class Add : BinaryOp
    {
        static readonly Meta _Meta = new Meta(typeof(Add), typeof(Assembly.Instructions.AsmAdd), "+=", (a, b) => a + b);
        protected override Meta MetaInfo => _Meta;
        public static readonly IStatementParser Parser = new BinaryOpParser(_Meta);

        public Add(ValRegEx regdst, ValBase value)
            : base(regdst, value)
        { }
    }

    class Sub : BinaryOp
    {
        static readonly Meta _Meta = new Meta(typeof(Sub), null, "-=", (a, b) => a - b);
        protected override Meta MetaInfo => _Meta;
        public static readonly IStatementParser Parser = new BinaryOpParser(_Meta);

        public Sub(ValRegEx regdst, ValBase value)
            : base(regdst, value)
        { }

        public override void Assemble(Assembly.Assembler assembler)
        {
            if (Value is ValInstant)
            {
                assembler.Add(Assembly.Instructions.AsmMov.Create(Assembly.Assembler.IReg, -(Value as ValInstant).Val));
                assembler.Add(Assembly.Instructions.AsmAdd.Create(RegDst.Index, new ValReg(Assembly.Assembler.IReg)));
            }
            else if (Value is ValReg)
            {
                assembler.Add(Assembly.Instructions.AsmMov.Create(Assembly.Assembler.IReg, Value));
                assembler.Add(Assembly.Instructions.AsmNegative.Create(Assembly.Assembler.IReg));
                assembler.Add(Assembly.Instructions.AsmAdd.Create(RegDst.Index, new ValReg(Assembly.Assembler.IReg)));
            }
            else
                throw new Assembly.AssembleException(ErrorMessage.NotSupported);
        }
    }

    class Mul : BinaryOp
    {
        static readonly Meta _Meta = new Meta(typeof(Mul), typeof(Assembly.Instructions.AsmMul), "*=", (a, b) => a * b);
        protected override Meta MetaInfo => _Meta;
        public static readonly IStatementParser Parser = new BinaryOpParser(_Meta);

        public Mul(ValRegEx regdst, ValBase value)
            : base(regdst, value)
        { }
    }

    class Div : BinaryOp
    {
        static readonly Meta _Meta = new Meta(typeof(Div), typeof(Assembly.Instructions.AsmDiv), "/=", (a, b) => a / b);
        protected override Meta MetaInfo => _Meta;
        public static readonly IStatementParser Parser = new BinaryOpParser(_Meta);

        public Div(ValRegEx regdst, ValBase value)
            : base(regdst, value)
        { }
    }

    class RoundDiv : BinaryOp
    {
        static readonly Meta _Meta = new Meta(typeof(RoundDiv), typeof(Assembly.Instructions.AsmDiv), @"\=", (a, b) => (int)Math.Round((double)a / b) );
        protected override Meta MetaInfo => _Meta;
        public static readonly IStatementParser Parser = new BinaryOpParser(_Meta);

        public RoundDiv(ValRegEx regdst, ValBase value)
            : base(regdst, value)
        { }

        public override void Assemble(Assembly.Assembler assembler)
        {
            throw new Assembly.AssembleException(ErrorMessage.NotSupported);
        }
    }

    class Mod : BinaryOp
    {
        static readonly Meta _Meta = new Meta(typeof(Mod), typeof(Assembly.Instructions.AsmMod), "%=", (a, b) => a % b);
        protected override Meta MetaInfo => _Meta;
        public static readonly IStatementParser Parser = new BinaryOpParser(_Meta);

        public Mod(ValRegEx regdst, ValBase value)
            : base(regdst, value)
        { }
    }

    class And : BinaryOp
    {
        static readonly Meta _Meta = new Meta(typeof(And), typeof(Assembly.Instructions.AsmAnd), "&=", (a, b) => a & b);
        protected override Meta MetaInfo => _Meta;
        public static readonly IStatementParser Parser = new BinaryOpParser(_Meta);

        public And(ValRegEx regdst, ValBase value)
            : base(regdst, value)
        { }
    }

    class Or : BinaryOp
    {
        static readonly Meta _Meta = new Meta(typeof(Or), typeof(Assembly.Instructions.AsmOr), "|=", (a, b) => a | b);
        protected override Meta MetaInfo => _Meta;
        public static readonly IStatementParser Parser = new BinaryOpParser(_Meta);

        public Or(ValRegEx regdst, ValBase value)
            : base(regdst, value)
        { }
    }

    class Xor : BinaryOp
    {
        static readonly Meta _Meta = new Meta(typeof(Xor), typeof(Assembly.Instructions.AsmOr), "^=", (a, b) => a ^ b);
        protected override Meta MetaInfo => _Meta;
        public static readonly IStatementParser Parser = new BinaryOpParser(_Meta);

        public Xor(ValRegEx regdst, ValBase value)
            : base(regdst, value)
        { }
    }

    class ShiftLeft : BinaryOp
    {
        static readonly Meta _Meta = new Meta(typeof(ShiftLeft), typeof(Assembly.Instructions.AsmShiftLeft), "<<=", (a, b) => a << b, true);
        protected override Meta MetaInfo => _Meta;
        public static readonly IStatementParser Parser = new BinaryOpParser(_Meta);

        public ShiftLeft(ValRegEx regdst, ValBase value)
            : base(regdst, value)
        { }
    }

    class ShiftRight : BinaryOp
    {
        static readonly Meta _Meta = new Meta(typeof(ShiftRight), typeof(Assembly.Instructions.AsmShiftRight), ">>=", (a, b) => a >> b, true);
        protected override Meta MetaInfo => _Meta;
        public static readonly IStatementParser Parser = new BinaryOpParser(_Meta);

        public ShiftRight(ValRegEx regdst, ValBase value)
            : base(regdst, value)
        { }
    }
}
