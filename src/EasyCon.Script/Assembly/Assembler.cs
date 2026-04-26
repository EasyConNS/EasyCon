using EasyCon.Script.Binding;
using static EasyCon.Script.Binding.BoundNodeKind;

namespace EasyCon.Script.Asm;

class Assembler
{
    public const uint IReg = 7;

    readonly List<Instruction> _instructions = [];
    public readonly Dictionary<int, Instructions.AsmKey_Hold> KeyMapping = [];
    public readonly Dictionary<int, Instructions.AsmStick_Hold> StickMapping = [];
    public readonly Dictionary<string, Instructions.AsmBranch> FunctionMapping = [];
    public readonly Dictionary<string, Instructions.AsmEmpty> CallMapping = [];


    static Instruction Assert(Instruction ins)
    {
        if (ins is Instruction.Failed fins)
            throw new AssembleException(fins.Message);
        return ins;
    }

    public void Add(Instruction ins)
    {
        _instructions.Add(Assert(ins));
    }

    public Instruction Last()
    {
        return _instructions.Last();
    }

    private static void Assemble(TextWriter f, BoundBlockStatement body)
    {
        var index = 0;
        while (index < body.Statements.Length)
        {
            var s = body.Statements[index];
            switch (s.Kind)
            {
                case NopStatement:
                    f.WriteLine("NOP");
                    break;
                case ExpressionStatement:
                    f.WriteLine("EXP");
                    break;
                case KeyAction:
                    f.WriteLine("KEY");
                    break;
                case GotoStatement:
                    f.WriteLine("GOTO");
                    break;
                case ConditionGotoStatement:
                    f.WriteLine("JGOTO");
                    break;
                case Label:
                    f.WriteLine("LBL:");
                    break;
                case Return:
                    f.WriteLine("RET");
                    break;
                default:
                    f.WriteLine("UNK");
                    break;
            }
        }
    }

    public byte[] Assemble(BoundProgram prog, bool autoRun)
    {
        // initialize
        _instructions.Clear();
        //ForMapping.Clear();
        //IfMapping.Clear();
        //ElseMapping.Clear();
        KeyMapping.Clear();
        StickMapping.Clear();
        FunctionMapping.Clear();
        CallMapping.Clear();

        // compile into instructions
        using var f = new StringWriter();
        foreach (var body in prog.Functions.Values)
        {
            Assemble(f, body);
        }
        File.WriteAllText("code.tmp", f.ToString());

        throw new AssembleException("此版本暂不支持编译");
        // optimize
        var discarded = new HashSet<Instruction>();
        List<Instruction> list = new();
        foreach (var item in _instructions)
        {
            list.Add(item);

            // 1 Instruction
            var ins1 = list[^1]; // list.Count - 1

            // 2 Instructions
            if (list.Count < 2)
                continue;
            var ins2 = ins1;
            ins1 = list[^2]; // list.Count - 2
            // keypress-wait => compressed keypress
            if (ins1 is Instructions.AsmKey_Standard press && ins2 is Instructions.AsmWait wait)
            {
                var ins = Instructions.AsmKey_Compressed.Create(press.KeyCode, press.RealDuration, wait.RealDuration);
                if (ins.Success)
                {
                    list.Add(ins);
                    discarded.Add(ins1);
                    discarded.Add(ins2);
                    continue;
                }
            }

            // 3 Instructions
            if (list.Count < 3)
                continue;
            var ins3 = ins2;
            ins2 = ins1;
            ins1 = list[^3];
            // if-loopcontrol-endif => loopcontrol_cf
            if (ins1 is Instructions.AsmBranchFalse falseins && ins2 is Instructions.AsmLoopControl ctlins && ins3 is Instructions.AsmEmpty && falseins.Target == ins3)
            {
                ctlins.CheckFlag = 1;
                discarded.Add(ins1);
                continue;
            }

            // 4 Instructions
            if (list.Count < 4)
                continue;
            var ins4 = ins3;
            ins3 = ins2;
            ins2 = ins1;
            ins1 = list[list.Count - 4];
            // if-loopcontrol-else-endif => loopcontrol_cf-ifnot-endif
            if (ins1 is Instructions.AsmBranchFalse falsein && ins2 is Instructions.AsmLoopControl ctlin && ins3 is Instructions.AsmBranch branch3 && ins4 is Instructions.AsmEmpty && falsein.Target == ins4)
            {
                ctlin.CheckFlag = 1;
                list.Add(Instructions.AsmBranchTrue.Create(branch3.Target));
                discarded.Add(ins1);
                discarded.Add(ins3);
                continue;
            }
        }
        _instructions.Clear();

        // update address and index
        int addr = 2;
        int index = 0;
        for (int i = 0; i < list.Count; i++)
        {
            var ins = list[i];
            ins.Address = addr;
            ins.Index = index;
            if (!discarded.Contains(ins))
            {
                addr += ins.ByteCount;
                index += ins.InsCount;
                _instructions.Add(ins);
            }
        }

        // generate bytecode
        using var stream = new MemoryStream();
        // preserved for length
        stream.WriteByte(0);
        stream.WriteByte(0);
        // write instructions
        foreach (var ins in _instructions)
            if (!discarded.Contains(ins))
                ins.WriteBytes(stream);
        // get array
        var bytes = stream.ToArray();
        // write length
        bytes[0] = (byte)(bytes.Length & 0xFF);
        bytes[1] = (byte)(bytes.Length >> 8);

        if (!autoRun)
        {
            bytes[1] |= 0x80;
        }
        return bytes;
    }
}

public class AssembleException : Exception
{
    public int Index;

    public AssembleException(string message, int index = -1)
        : base(message)
    {
        Index = index;
    }
}