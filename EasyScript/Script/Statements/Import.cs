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
            scripter.Parse(FileContent, processor.extVars);
            scripter.Run(processor.Output, processor.GamePad);
        }

        override public void Assemble(Assembly.Assembler assembler)
        { }

        
    }
}
