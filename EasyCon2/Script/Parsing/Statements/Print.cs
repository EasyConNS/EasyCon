using System.Text.RegularExpressions;

namespace EasyCon2.Script.Parsing.Statements
{
    class Print : Statement
    {
        public static readonly IStatementParser Parser = new StatementParser(Parse);

        public readonly Content[] Contents;
        public readonly bool CancelLineBreak;

        public Print(Content[] contents, bool cancellinebreak)
        {
            Contents = contents;
            CancelLineBreak = cancellinebreak;
        }

        public static Statement Parse(ParserArgument args)
        {
            if (args.Text.Equals("print", StringComparison.OrdinalIgnoreCase))
                return new Print(new Content[0], false);
            var m = Regex.Match(args.Text, @"^print\s+(.*)$", RegexOptions.IgnoreCase);
            if (m.Success)
            {
                var strs = m.Groups[1].Value.Split('&');
                var contents = new List<Content>();
                var cancellinebreak = false;
                for (int i = 0; i < strs.Length; i++)
                {
                    var s = strs[i].Trim();
                    if (i == strs.Length - 1 && s == "\\")
                    {
                        contents.Add(new TextContent("", "\\"));
                        cancellinebreak = true;
                        continue;
                    }
                    if (s.Length == 0 && strs.Length > 1)
                    {
                        contents.Add(new TextContent(" ", ""));
                        continue;
                    }
                    m = Regex.Match(s, Formats.RegisterEx_F);
                    if (m.Success)
                    {
                        contents.Add(new RegContent(Formatter.GetRegEx(s)));
                        continue;
                    }
                    m = Regex.Match(s, Formats.Constant_F);
                    if (m.Success)
                    {
                        var v = args.Formatter.GetConstant(s);
                        contents.Add(new TextContent(v.Val.ToString(), s));
                        continue;
                    }
                    contents.Add(new TextContent(s));
                }
                return new Print(contents.ToArray(), cancellinebreak);
            }
            return null;
        }

        protected override string _GetString(Formatter formatter)
        {
            return Contents.Length == 0 ? "PRINT" : $"PRINT {string.Join(" & ", Contents.Select(u => u.GetCodeString(formatter)))}";
        }

        public override void Exec(Processor processor)
        {
            processor.Output.Print(string.Join("", Contents.Select(u => u.GetPrintString(processor))), !processor.CancelLineBreak);
            processor.CancelLineBreak = CancelLineBreak;
        }

        public override void Assemble(Assembly.Assembler assembler)
        { }
    }
}
