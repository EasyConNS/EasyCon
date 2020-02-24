using EasyCon.Script.Assembly;
using EasyCon.Script.Parsing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EasyCon.Script
{
    public class Script
    {
        public Dictionary<string, int> Constants = new Dictionary<string, int>();

        List<Statement> _statements;
        Processor _processor;

        public void Parse(string code)
        {
            _statements = new Parser(Constants).Parse(code);
        }

        public void Run(IOutputAdapter output)
        {
            _processor = new Processor();
            _processor.Output = output;
            while (_processor.PC < _statements.Count)
            {
                var cmd = _statements[_processor.PC];
                _processor.PC++;
                cmd.Exec(_processor);
            }
        }

        public string ToCode()
        {
            var formatter = new Formats.Formatter(Constants);
            return string.Join(Environment.NewLine, _statements.Select(u => u.GetString(formatter)));
        }

        public byte[] Assemble()
        {
            return new Assembler().Assemble(_statements);
        }
    }
}
