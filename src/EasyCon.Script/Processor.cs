namespace EasyScript;

class Processor
{
    public const uint OfflineMaxRegisterCount = 8;

    public IOutputAdapter Output;
    public ICGamePad GamePad;

    public int PC = 0;
    public bool CancelLineBreak = false;
    public Stack<short> Stack = new();
    public Stack<int> CallStack = new();
    public Stack<Statements.For> LoopStack = new();
    public Dictionary<Statements.For, int> LoopTime = new();
    public Dictionary<Statements.For, int> LoopCount = new();
    public RegisterFile Register = new();

    public ExternTime et = new(DateTime.Now);
}

class RegisterFile
{
    private readonly Dictionary<string, int> _variables = new();

    public int this[string tag]
    {
        get => _variables.ContainsKey(tag)? _variables[tag]:0;
        set => _variables[tag] = value;
    }

    public int this[Parsing.ValRegEx val]
    {
        get => this[val.Tag];

        set => this[val.Tag] = value;
    }
}
