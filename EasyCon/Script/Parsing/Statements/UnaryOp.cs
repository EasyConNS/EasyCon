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

            public UnaryOpParser(Meta meta)
            {
                _meta = meta;
            }

            public Statement Parse(ParserArgument args)
            {
                var m = Regex.Match(args.Text, $@"^{_meta.KeyWord}\s+{Formats.Register}$", RegexOptions.IgnoreCase);
                if (m.Success)
                    return Activator.CreateInstance(_meta.StatementType, args.Formatter.GetReg(m.Groups[1].Value, true)) as Statement;
                return null;
            }
        }

        protected abstract Meta MetaInfo { get; }
        public readonly ValReg RegDst;

        public UnaryOp(ValReg regdst)
        {
            RegDst = regdst;
        }

        protected override string _GetString(Formats.Formatter formatter)
        {
            return $"{MetaInfo.KeyWord} {RegDst.GetCodeText(formatter)}";
        }

        public override void Exec(Processor processor)
        {
            processor.Register[RegDst.Index] = (short)MetaInfo.Function(processor.Register[RegDst.Index]);
        }

        public override void Assemble(Assembler assembler)
        {
            assembler.Add(Instruction.CreateInstance(MetaInfo.InstructionType, RegDst.Index) as Instruction);
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
                var m = Regex.Match(args.Text, $@"^{Formats.Register}\s*\=\s*{_meta.Operator}\s*{Formats.Register}$", RegexOptions.IgnoreCase);
                if (m.Success)
                    return Activator.CreateInstance(_meta.StatementType, args.Formatter.GetReg(m.Groups[1].Value, true), args.Formatter.GetReg(m.Groups[2].Value, false)) as Statement;
                return null;
            }
        }

        protected abstract Meta MetaInfo { get; }
        public readonly ValReg RegDst;
        public readonly ValReg RegSrc;

        public UnaryOpEx(ValReg regdst, ValReg regsrc)
        {
            RegDst = regdst;
            RegSrc = regsrc;
        }

        protected override string _GetString(Formats.Formatter formatter)
        {
            return $"{RegDst.GetCodeText(formatter)} = {MetaInfo.Operator}{RegSrc.GetCodeText(formatter)}";
        }

        public override void Exec(Processor processor)
        {
            processor.Register[RegDst.Index] = (short)MetaInfo.Function(processor.Register[RegSrc.Index]);
        }

        public override void Assemble(Assembler assembler)
        {
            assembler.Add(AsmMov.Create(RegDst.Index, RegSrc));
            assembler.Add(Instruction.CreateInstance(MetaInfo.InstructionType, RegDst.Index) as Instruction);
        }
    }

    class Not : UnaryOpEx
    {
        static readonly Meta _Meta = new Meta(typeof(Not), typeof(AsmNot), "~", a => ~a);
        protected override Meta MetaInfo => _Meta;
        public static readonly IStatementParser Parser = new UnaryOpParser(_Meta);

        public Not(ValReg regdst, ValReg regsrc)
            : base(regdst, regsrc)
        { }
    }

    class Negative : UnaryOpEx
    {
        static readonly Meta _Meta = new Meta(typeof(Negative), typeof(AsmNegative), "-", a => -a);
        protected override Meta MetaInfo => _Meta;
        public static readonly IStatementParser Parser = new UnaryOpParser(_Meta);

        public Negative(ValReg regdst, ValReg regsrc)
            : base(regdst, regsrc)
        { }
    }

    class Push : UnaryOp
    {
        static readonly Meta _Meta = new Meta(typeof(Push), typeof(AsmPush), "PUSH", null);
        protected override Meta MetaInfo => _Meta;
        public static readonly IStatementParser Parser = new UnaryOpParser(_Meta);

        public Push(ValReg regdst)
            : base(regdst)
        { }

        public override void Exec(Processor processor)
        {
            processor.Stack.Push(processor.Register[RegDst.Index]);
        }
    }

    class Pop : UnaryOp
    {
        static readonly Meta _Meta = new Meta(typeof(Pop), typeof(AsmPop), "POP", null);
        protected override Meta MetaInfo => _Meta;
        public static readonly IStatementParser Parser = new UnaryOpParser(_Meta);

        public Pop(ValReg regdst)
            : base(regdst)
        { }

        public override void Exec(Processor processor)
        {
            if (processor.Stack.Count <= 0)
                throw new ScriptException("栈为空，无法出栈", Address);
            processor.Register[RegDst.Index] = processor.Stack.Pop();
        }
    }

    class Bool : UnaryOp
    {
        static readonly Meta _Meta = new Meta(typeof(Bool), typeof(AsmBool), "BOOL", a => a == 0 ? 0 : 1);
        protected override Meta MetaInfo => _Meta;
        public static readonly IStatementParser Parser = new UnaryOpParser(_Meta);

        public Bool(ValReg regdst)
            : base(regdst)
        { }
    }
}
