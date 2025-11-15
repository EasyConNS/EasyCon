using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace EasyScript.Parsing.Statements
{
    class Import : Statement
    {
        readonly string FilePath;
        readonly string FileContent;


        public Import(string path, string content)
        {
            FilePath = path;
            FileContent = content;
        }

        protected override string _GetString(Formatter formatter)
        {
            return $"IMPORT <{FilePath}>";
        }

        override public void Exec(Processor processor)
        {
            Scripter scripter = new();
            scripter.load(FileContent, processor.extVars);
            scripter.explain(processor.Output, processor.GamePad, out var _subProcessor);
            foreach(var kvp in _subProcessor.FunctionDefinitions)
            {
                processor.FunctionDefinitions[kvp.Key] = kvp.Value;
            }
        }

        override public void Assemble(Assembly.Assembler assembler)
        { }

        
    }
}
