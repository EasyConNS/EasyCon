using System.Text.RegularExpressions;
using EasyScript.Parsing;

namespace EasyScript.Statements;

class MetaU
{
    public readonly Type StatementType;
    public readonly Type InstructionType;
    public readonly string KeyWord;
    public readonly Func<int, int> Function;

    public MetaU(Type statementType, Type instructionType, string keyword, Func<int, int> function)
    {
        StatementType = statementType;
        InstructionType = instructionType;
        KeyWord = keyword;
        Function = function;
    }
}

abstract class UnaryOp : Statement
{
    protected class UnaryOpParser : IStatementParser
    {
        readonly MetaU _meta;
        readonly bool _lhs;

        public UnaryOpParser(MetaU meta, bool lhs = true)
        {
            _meta = meta;
            _lhs = lhs;
        }

        public Parsing.Statement Parse(ParserArgument args)
        {
            var m = Regex.Match(args.Text, $@"^{_meta.KeyWord}\s+{Formats.RegisterEx}$", RegexOptions.IgnoreCase);
            
            if (m.Success)
                return Activator.CreateInstance(_meta.StatementType, FormatterUtil.GetRegEx(m.Groups[1].Value, _lhs)) as Parsing.Statement;
            return null;
        }
    }

    protected abstract MetaU MetaInfo { get; }
    public readonly ValRegEx RegDst;

    public UnaryOp(ValRegEx regdst)
    {
        RegDst = regdst;
    }

    protected override string _GetString()
    {
        return $"{MetaInfo.KeyWord} {RegDst.GetCodeText()}";
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
    protected class UnaryOpParser : IStatementParser
    {
        readonly MetaU _meta;

        public UnaryOpParser(MetaU meta)
        {
            _meta = meta;
        }

        public Parsing.Statement Parse(ParserArgument args)
        {
            var m = Regex.Match(args.Text, $@"^{Formats.RegisterEx}\s*\=\s*{_meta.KeyWord}\s*{Formats.RegisterEx}$", RegexOptions.IgnoreCase);
            if (m.Success)
                return Activator.CreateInstance(_meta.StatementType, FormatterUtil.GetRegEx(m.Groups[1].Value, true), FormatterUtil.GetRegEx(m.Groups[2].Value, false)) as Parsing.Statement;
            return null;
        }
    }

    protected abstract MetaU MetaInfo { get; }
    public readonly ValRegEx RegDst;
    public readonly ValRegEx RegSrc;

    public UnaryOpEx(ValRegEx regdst, ValRegEx regsrc)
    {
        RegDst = regdst;
        RegSrc = regsrc;
    }

    protected override string _GetString()
    {
        return $"{RegDst.GetCodeText()} = {MetaInfo.KeyWord}{RegSrc.GetCodeText()}";
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
    static readonly MetaU _Meta = new(typeof(Not), typeof(Assembly.Instructions.AsmNot), "~", a => ~a);
    protected override MetaU MetaInfo => _Meta;
    public static readonly IStatementParser Parser = new UnaryOpParser(_Meta);

    public Not(ValReg regdst, ValReg regsrc)
        : base(regdst, regsrc)
    { }
}

class Negative : UnaryOpEx
{
    static readonly MetaU _Meta = new(typeof(Negative), typeof(Assembly.Instructions.AsmNegative), "-", a => -a);
    protected override MetaU MetaInfo => _Meta;
    public static readonly IStatementParser Parser = new UnaryOpParser(_Meta);

    public Negative(ValReg regdst, ValReg regsrc)
        : base(regdst, regsrc)
    { }
}

class Push : UnaryOp
{
    static readonly MetaU _Meta = new(typeof(Push), typeof(Assembly.Instructions.AsmPush), "PUSH", null);
    protected override MetaU MetaInfo => _Meta;
    public static readonly IStatementParser Parser = new UnaryOpParser(_Meta, false);

    public Push(ValReg regdst)
        : base(regdst)
    { }

    public override void Exec(Processor processor)
    {
        processor.Push((short)processor.Register[RegDst]);
    }
}

class Pop : UnaryOp
{
    static readonly MetaU _Meta = new(typeof(Pop), typeof(Assembly.Instructions.AsmPop), "POP", null);
    protected override MetaU MetaInfo => _Meta;
    public static readonly IStatementParser Parser = new UnaryOpParser(_Meta);

    public Pop(ValReg regdst)
        : base(regdst)
    { }

    public override void Exec(Processor processor)
    {
        processor.Register[RegDst] = processor.Pop();
    }
}

class Bool : UnaryOp
{
    static readonly MetaU _Meta = new(typeof(Bool), typeof(Assembly.Instructions.AsmBool), "BOOL", a => a == 0 ? 0 : 1);
    protected override MetaU MetaInfo => _Meta;
    public static readonly IStatementParser Parser = new UnaryOpParser(_Meta);

    public Bool(ValReg regdst)
        : base(regdst)
    { }
}

class Rand : UnaryOp
{
    static Random _rand = new();
    static readonly MetaU _Meta = new(typeof(Rand), typeof(Assembly.Instructions.AsmRand), "RAND", a => _rand.Next(a));
    protected override MetaU MetaInfo => _Meta;
    public static readonly IStatementParser Parser = new UnaryOpParser(_Meta);

    public Rand(ValReg regdst)
        : base(regdst)
    { }
}
