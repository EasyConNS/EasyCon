namespace EasyCon2.Script
{
    public class Scripter
    {
        public Dictionary<string, int> Constants = new();
        public Dictionary<string, ExternalVariable> ExtVars = new();

        List<Parsing.Statement> _statements = new();

        public void Parse(string code, IEnumerable<ExternalVariable> extVars)
        {
            ExtVars = new();
            foreach (var ev in extVars)
                ExtVars[ev.Name] = ev;
            _statements = new Parsing.Parser(Constants, ExtVars).Parse(code);
        }

        public void Run(IOutputAdapter output, ICGamePad pad, CancellationToken token)
        {
            var _processor = new Processor
            {
                Output = output,
                GamePad = pad,
            };
            while (_processor.PC < _statements.Count)
            {
                if (token.IsCancellationRequested)
                {
                    throw new ScriptAbortException("task aborted!");
                }
                var cmd = _statements[_processor.PC];
                _processor.PC++;
                cmd.Exec(_processor);
            }
        }

        public string ToCode()
        {
            var formatter = new Parsing.Formatter(Constants, ExtVars);
            return string.Join(Environment.NewLine, _statements.Select(u => u.GetString(formatter)));
        }

        public byte[] Assemble(bool auto = true)
        {
            return new Assembly.Assembler().Assemble(_statements, auto);
        }
    }
}
