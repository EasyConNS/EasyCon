

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
            Processor _subProcessor = scripter.explain(new Processor() { 
                    Output = processor.Output,
                    GamePad = processor.GamePad,
                    extVars = processor.extVars
            });
            
            foreach(var kvp in _subProcessor.FunctionDefinitions)
            {
                processor.FunctionDefinitions[kvp.Key] = kvp.Value;
            }
        }

        override public void Assemble(Assembly.Assembler assembler)
        { }

        
    }
}
