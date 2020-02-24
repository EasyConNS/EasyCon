using EasyCon.Script.Assembly;
using EasyCon.Script.Assembly.Instructions;
using EasyCon.Script.Parsing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace EasyCon.Script.Statements
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
                var m = Regex.Match(args.Text, $@"^{Formats.Register}\s*\{_meta.Operator}\s*{(_meta.OnlyInstant ? Formats.Instant : Formats.Value)}$", RegexOptions.IgnoreCase);
                if (m.Success)
                    return Activator.CreateInstance(_meta.StatementType, args.Formatter.GetReg(m.Groups[1].Value, true), args.Formatter.GetValue(m.Groups[2].Value)) as Statement;
                return null;
            }
        }

        protected abstract Meta MetaInfo { get; }
        public readonly ValReg RegDst;
        public readonly ValBase Value;

        public BinaryOp(ValReg regdst, ValBase value)
        {
            RegDst = regdst;
            Value = value;
        }

        protected override string _GetString(Formats.Formatter formatter)
        {
            return $"{RegDst.GetCodeText(formatter)} {MetaInfo.Operator} {Value.GetCodeText(formatter)}";
        }

        public override void Exec(Processor processor)
        {
            processor.Register[RegDst.Index] = (short)MetaInfo.Function(processor.Register[RegDst.Index], Value.Evaluate(processor));
        }

        public override void Assemble(Assembler assembler)
        {
            assembler.Add(Instruction.CreateInstance(MetaInfo.InstructionType, RegDst.Index, Value) as Instruction);
        }
    }

    class Add : BinaryOp
    {
        static readonly Meta _Meta = new Meta(typeof(Add), typeof(AsmAdd), "+=", (a, b) => a + b);
        protected override Meta MetaInfo => _Meta;
        public static readonly IStatementParser Parser = new BinaryOpParser(_Meta);

        public Add(ValReg regdst, ValBase value)
            : base(regdst, value)
        { }
    }

    class Sub : BinaryOp
    {
        static readonly Meta _Meta = new Meta(typeof(Sub), null, "-=", (a, b) => a - b);
        protected override Meta MetaInfo => _Meta;
        public static readonly IStatementParser Parser = new BinaryOpParser(_Meta);

        public Sub(ValReg regdst, ValBase value)
            : base(regdst, value)
        { }

        public override void Assemble(Assembler assembler)
        {
            if (Value is ValInstant)
            {
                assembler.Add(AsmMov.Create(Assembler.IReg, -(Value as ValInstant).Val));
                assembler.Add(AsmAdd.Create(RegDst.Index, new ValReg(Assembler.IReg)));
            }
            else
            {
                assembler.Add(AsmMov.Create(Assembler.IReg, Value));
                assembler.Add(AsmNegative.Create(Assembler.IReg));
                assembler.Add(AsmAdd.Create(RegDst.Index, new ValReg(Assembler.IReg)));
            }
        }
    }

    class Mul : BinaryOp
    {
        static readonly Meta _Meta = new Meta(typeof(Mul), typeof(AsmMul), "*=", (a, b) => a * b);
        protected override Meta MetaInfo => _Meta;
        public static readonly IStatementParser Parser = new BinaryOpParser(_Meta);

        public Mul(ValReg regdst, ValBase value)
            : base(regdst, value)
        { }
    }

    class Div : BinaryOp
    {
        static readonly Meta _Meta = new Meta(typeof(Div), typeof(AsmDiv), "/=", (a, b) => a / b);
        protected override Meta MetaInfo => _Meta;
        public static readonly IStatementParser Parser = new BinaryOpParser(_Meta);

        public Div(ValReg regdst, ValBase value)
            : base(regdst, value)
        { }
    }

    class Mod : BinaryOp
    {
        static readonly Meta _Meta = new Meta(typeof(Mod), typeof(AsmMod), "%=", (a, b) => a % b);
        protected override Meta MetaInfo => _Meta;
        public static readonly IStatementParser Parser = new BinaryOpParser(_Meta);

        public Mod(ValReg regdst, ValBase value)
            : base(regdst, value)
        { }
    }

    class And : BinaryOp
    {
        static readonly Meta _Meta = new Meta(typeof(And), typeof(AsmAnd), "&=", (a, b) => a & b);
        protected override Meta MetaInfo => _Meta;
        public static readonly IStatementParser Parser = new BinaryOpParser(_Meta);

        public And(ValReg regdst, ValBase value)
            : base(regdst, value)
        { }
    }

    class Or : BinaryOp
    {
        static readonly Meta _Meta = new Meta(typeof(Or), typeof(AsmOr), "|=", (a, b) => a | b);
        protected override Meta MetaInfo => _Meta;
        public static readonly IStatementParser Parser = new BinaryOpParser(_Meta);

        public Or(ValReg regdst, ValBase value)
            : base(regdst, value)
        { }
    }

    class Xor : BinaryOp
    {
        static readonly Meta _Meta = new Meta(typeof(Xor), typeof(AsmOr), "^=", (a, b) => a ^ b);
        protected override Meta MetaInfo => _Meta;
        public static readonly IStatementParser Parser = new BinaryOpParser(_Meta);

        public Xor(ValReg regdst, ValBase value)
            : base(regdst, value)
        { }
    }

    class ShiftLeft : BinaryOp
    {
        static readonly Meta _Meta = new Meta(typeof(ShiftLeft), typeof(AsmShiftLeft), "<<=", (a, b) => a << b, true);
        protected override Meta MetaInfo => _Meta;
        public static readonly IStatementParser Parser = new BinaryOpParser(_Meta);

        public ShiftLeft(ValReg regdst, ValBase value)
            : base(regdst, value)
        { }
    }

    class ShiftRight : BinaryOp
    {
        static readonly Meta _Meta = new Meta(typeof(ShiftRight), typeof(AsmShiftRight), ">>=", (a, b) => a >> b, true);
        protected override Meta MetaInfo => _Meta;
        public static readonly IStatementParser Parser = new BinaryOpParser(_Meta);

        public ShiftRight(ValReg regdst, ValBase value)
            : base(regdst, value)
        { }
    }
}
