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
    public readonly ValReg RegDst;
    public readonly ValReg RegSrc;

    public UnaryOp(ValReg regdst, ValReg regsrc)
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
        if (RegDst is ValReg val)
        {
            if(val.Reg == 0)throw new Assembly.AssembleException(ErrorMessage.NotSupported);
            assembler.Add(Assembly.Instructions.AsmMov.Create(val.Reg, RegSrc));
            assembler.Add(Assembly.Instruction.CreateInstance(MetaInfo.InstructionType, val.Reg));
        }
    }
}

class Not : UnaryOp
{
    static readonly MetaU _Meta = new(typeof(Not), typeof(Assembly.Instructions.AsmNot), "~", a => ~a);
    protected override MetaU MetaInfo => _Meta;
    public static readonly IStatementParser Parser = new UnaryOpParser(_Meta);

    public Not(ValReg regdst, ValReg regsrc)
        : base(regdst, regsrc)
    { }
}

class Negative : UnaryOp
{
    static readonly MetaU _Meta = new(typeof(Negative), typeof(Assembly.Instructions.AsmNegative), "-", a => -a);
    protected override MetaU MetaInfo => _Meta;
    public static readonly IStatementParser Parser = new UnaryOpParser(_Meta);

    public Negative(ValReg regdst, ValReg regsrc)
        : base(regdst, regsrc)
    { }
}
