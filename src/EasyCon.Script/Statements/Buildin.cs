using EasyScript.Parsing;

namespace EasyScript.Statements;

class BuildinFunc : Statement
{
    public readonly string FnName;
    public readonly Param[] Params = [];

    public BuildinFunc(string fnName, Param[] args)
    {
        FnName = fnName;
        Params = args;
    }

    protected override string _GetString()
    {
        var args = Params.Any() ? $" {string.Join(" & ", Params.Select(u => u.GetCodeText()))}" : "";
        return $"{FnName}{args}";
    }

    public override void Exec(Processor processor)
    {
        processor.BuildinCall(FnName, Params.ToArray());
    }

    public override void Assemble(Assembly.Assembler assembler)
    {
        throw new Assembly.AssembleException(ErrorMessage.NotSupported);

        //if (RegDst is ValReg)
        //    assembler.Add(Assembly.Instruction.CreateInstance(MetaInfo.InstructionType, (RegDst as ValReg).Index));
        //else
        //    throw new Assembly.AssembleException(ErrorMessage.NotSupported);
    }
}