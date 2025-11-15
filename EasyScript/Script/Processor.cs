using EasyScript.Parsing;
using EasyScript.Parsing.Statements;

namespace EasyScript {
    internal class Processor
    {
        // only for online
        public const uint RegisterCount = 64;
        public const uint OfflineMaxRegisterCount = 8;

        public IOutputAdapter Output;
        public ICGamePad GamePad;

        public int PC = 0;
        public bool CancelLineBreak = false;
        public Stack<short> Stack = new();
        //public Stack<int> CallStack = new();
        public RegisterFile Register = new(RegisterCount);

        public ExternTime et = new(DateTime.Now);

        public IEnumerable<ExternalVariable> extVars = new List<ExternalVariable>();

        public Stack<FunctionDefinition> FunctionDefinitionStack = new();
        public Dictionary<string, FunctionDefinition> FunctionDefinitions = new();

        public Stack<ControlStatement> ControlStack = new();
        //public Stack<BranchOp> IfStack = new();
        public bool SkipState = false;

        //public Stack<For> LoopStack = new();
        public Dictionary<int, int> LoopTime = new(); // For address -> Loop time
        public Dictionary<int, int> LoopCount = new(); // For address -> Loop count
    };

    

    class RegisterFile
    {
        private readonly short[] _register;
        private readonly Dictionary<string, int> _variables = new();

        public RegisterFile(uint length)
        {
            _register = new short[length];
        }

        public short this[uint index]
        {
            get => _register[index];
            set => _register[index] = value;
        }

        public int this[string tag]
        {
            get => _variables.ContainsKey(tag) ? _variables[tag] : 0;
            set => _variables[tag] = value;
        }

        public int this[Parsing.ValRegEx val]
        {
            get
            {
                if (val is Parsing.ValReg val1)
                    return this[val1.Index];
                else
                    return this[val.Tag];
                throw new ArgumentException();
            }

            set
            {
                if (val is Parsing.ValReg val1)
                    _register[val1.Index] = (short)value;
                else
                    this[val.Tag] = value;
            }
        }
    }
}

namespace EasyScript.Parsing
{
    internal record FunctionDefinition(
        string Label,
        int OffsetLineNo        
        )
    {
        public readonly IEnumerable<Statement> Statements = new List<Statement>();
        public void Push(Statement statement)
        {
            Statements.Append(statement);
        }

        public void Exec(Processor processor)
        {
            processor.PC = OffsetLineNo;
            foreach (var statement in Statements)
            {
                processor.PC++;
                statement.Exec(processor);
            }
        }
    };
}
