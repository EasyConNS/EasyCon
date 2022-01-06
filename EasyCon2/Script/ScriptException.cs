namespace EasyCon2.Script
{
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
        public ScriptAbortException(string message) : base(message, 0) {}
    }
}
