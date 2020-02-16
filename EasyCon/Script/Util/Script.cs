using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EasyCon.Script
{
    public class Script
    {
        List<Statement> _statements;
        Processor _processor;

        public void Compile(string code)
        {
            _statements = new Compiler().Compile(code);
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
            return string.Join(Environment.NewLine, _statements);
        }

        public byte[] Assemble()
        {
            return Assembler.Assemble(_statements);
        }
    }
}
