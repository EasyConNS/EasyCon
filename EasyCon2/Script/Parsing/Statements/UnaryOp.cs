using System.Text.RegularExpressions;

namespace EasyCon2.Script.Parsing.Statements
{
    abstract class UnaryOp : Statement
    {
        protected class Meta
        {
            public readonly Type StatementType;
            public readonly Type InstructionType;
            public readonly string KeyWord;
            public readonly Func<int, int> Function;

            public Meta(Type statementType, Type instructionType, string keyword, Func<int, int> function)
            {
                StatementType = statementType;
                InstructionType = instructionType;
                KeyWord = keyword;
                Function = function;
            }
        }

        protected class UnaryOpParser : IStatementParser
        {
            readonly Meta _meta;
            readonly bool _lhs;

            public UnaryOpParser(Meta meta, bool lhs = true)
            {
                _meta = meta;
                _lhs = lhs;
            }

            public Statement Parse(ParserArgument args)
            {
                var m = Regex.Match(args.Text, $@"^{_meta.KeyWord}\s+{Formats.RegisterEx}$", RegexOptions.IgnoreCase);
                
                if (m.Success)
                    return Activator.CreateInstance(_meta.StatementType, FormatterUtil.GetRegEx(m.Groups[1].Value, _lhs)) as Statement;
                return null;
            }
        }

        protected abstract Meta MetaInfo { get; }
        public readonly ValRegEx RegDst;

        public UnaryOp(ValRegEx regdst)
        {
            RegDst = regdst;
        }

        protected override string _GetString(Formatter formatter)
        {
            return $"{MetaInfo.KeyWord} {RegDst.GetCodeText(formatter)}";
        }

        public override void Exec(Processor processor)
        {
            processor.Register[RegDst] = MetaInfo.Function(processor.Register[RegDst]);
        }

        public override void Assemble(Assembly.Assembler assembler)
        {
            if (RegDst is ValReg)
                assembler.Add(Assembly.Instruction.CreateInstance(MetaInfo.InstructionType, (RegDst as ValReg).Index));
            else 
                throw new Assembly.AssembleException(ErrorMessage.NotSupported);
        }
    }

    abstract class UnaryOpEx : Statement
    {
        protected class Meta
        {
            public readonly Type StatementType;
            public readonly Type InstructionType;
            public readonly string Operator;
            public readonly Func<int, int> Function;

            public Meta(Type statementType, Type instructionType, string @operator, Func<int, int> function)
            {
                StatementType = statementType;
                InstructionType = instructionType;
                Operator = @operator;
                Function = function;
            }
        }

        protected class UnaryOpParser : IStatementParser
        {
            readonly Meta _meta;

            public UnaryOpParser(Meta meta)
            {
                _meta = meta;
            }

            public Statement Parse(ParserArgument args)
            {
                var m = Regex.Match(args.Text, $@"^{Formats.RegisterEx}\s*\=\s*{_meta.Operator}\s*{Formats.RegisterEx}$", RegexOptions.IgnoreCase);
                if (m.Success)
                    return Activator.CreateInstance(_meta.StatementType, FormatterUtil.GetRegEx(m.Groups[1].Value, true), FormatterUtil.GetRegEx(m.Groups[2].Value, false)) as Statement;
                return null;
            }
        }

        protected abstract Meta MetaInfo { get; }
        public readonly ValRegEx RegDst;
        public readonly ValRegEx RegSrc;

        public UnaryOpEx(ValRegEx regdst, ValRegEx regsrc)
        {
            RegDst = regdst;
            RegSrc = regsrc;
        }

        protected override string _GetString(Formatter formatter)
        {
            return $"{RegDst.GetCodeText(formatter)} = {MetaInfo.Operator}{RegSrc.GetCodeText(formatter)}";
        }

        public override void Exec(Processor processor)
        {
            processor.Register[RegDst] = MetaInfo.Function(processor.Register[RegSrc]);
        }

        public override void Assemble(Assembly.Assembler assembler)
        {
            if (RegDst is ValReg)
            {
                var val = RegDst as ValReg;
                assembler.Add(Assembly.Instructions.AsmMov.Create(val.Index, RegSrc));
                assembler.Add(Assembly.Instruction.CreateInstance(MetaInfo.InstructionType, val.Index));
            }else
            {
                throw new Assembly.AssembleException(ErrorMessage.NotSupported);
            }
        }
    }

    class Not : UnaryOpEx
    {
        static readonly Meta _Meta = new(typeof(Not), typeof(Assembly.Instructions.AsmNot), "~", a => ~a);
        protected override Meta MetaInfo => _Meta;
        public static readonly IStatementParser Parser = new UnaryOpParser(_Meta);

        public Not(ValReg regdst, ValReg regsrc)
            : base(regdst, regsrc)
        { }
    }

    class Negative : UnaryOpEx
    {
        static readonly Meta _Meta = new(typeof(Negative), typeof(Assembly.Instructions.AsmNegative), "-", a => -a);
        protected override Meta MetaInfo => _Meta;
        public static readonly IStatementParser Parser = new UnaryOpParser(_Meta);

        public Negative(ValReg regdst, ValReg regsrc)
            : base(regdst, regsrc)
        { }
    }

    class Push : UnaryOp
    {
        static readonly Meta _Meta = new(typeof(Push), typeof(Assembly.Instructions.AsmPush), "PUSH", null);
        protected override Meta MetaInfo => _Meta;
        public static readonly IStatementParser Parser = new UnaryOpParser(_Meta, false);

        public Push(ValReg regdst)
            : base(regdst)
        { }

        public override void Exec(Processor processor)
        {
            processor.Stack.Push((short)processor.Register[RegDst]);
        }
    }

    class Pop : UnaryOp
    {
        static readonly Meta _Meta = new(typeof(Pop), typeof(Assembly.Instructions.AsmPop), "POP", null);
        protected override Meta MetaInfo => _Meta;
        public static readonly IStatementParser Parser = new UnaryOpParser(_Meta);

        public Pop(ValReg regdst)
            : base(regdst)
        { }

        public override void Exec(Processor processor)
        {
            if (processor.Stack.Count <= 0)
                throw new ScriptException("栈为空，无法出栈", Address);
            processor.Register[RegDst] = processor.Stack.Pop();
        }
    }

    class Bool : UnaryOp
    {
        static readonly Meta _Meta = new(typeof(Bool), typeof(Assembly.Instructions.AsmBool), "BOOL", a => a == 0 ? 0 : 1);
        protected override Meta MetaInfo => _Meta;
        public static readonly IStatementParser Parser = new UnaryOpParser(_Meta);

        public Bool(ValReg regdst)
            : base(regdst)
        { }
    }

    class Rand : UnaryOp
    {
        static Random _rand = new();
        static readonly Meta _Meta = new(typeof(Rand), typeof(Assembly.Instructions.AsmRand), "RAND", a => _rand.Next(a));
        protected override Meta MetaInfo => _Meta;
        public static readonly IStatementParser Parser = new UnaryOpParser(_Meta);

        public Rand(ValReg regdst)
            : base(regdst)
        { }
    }
}
