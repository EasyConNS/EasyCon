namespace EasyScript;

public class ScriptException : Exception
{
    public int Address { get; private set; }

    public ScriptException(string message, int address)
        : base(message)
    {
        Address = address;
    }
}

public class ScriptAbortException : ScriptException
{
    public ScriptAbortException(string message = "aborted!") : base(message, 0) {}
}
