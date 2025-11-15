namespace EasyScript;

class Processor
{
    // only for online
    public const uint RegisterCount = 64;
    public const uint OfflineMaxRegisterCount = 8;

    public IOutputAdapter Output;
    public ICGamePad GamePad;

    public int PC = 0;
    public bool CancelLineBreak = false;
    public Stack<short> Stack = new();
    public Stack<int> CallStack = new();
    public Stack<Parsing.Statements.For> LoopStack = new();
    public Dictionary<Parsing.Statements.For, int> LoopTime = new();
    public Dictionary<Parsing.Statements.For, int> LoopCount = new();
    public RegisterFile Register = new(RegisterCount);

    public ExternTime et = new(DateTime.Now);

    public IEnumerable<ExternalVariable> extVars = new List<ExternalVariable>();
}

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
        get => _variables.ContainsKey(tag)? _variables[tag]:0;
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
