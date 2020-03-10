using EasyCon.Script.Assembly;
using EasyCon.Script.Parsing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace EasyCon.Script.Parsing.Statements
{
    class Print : Statement
    {
        public static readonly IStatementParser Parser = new StatementParser(Parse);

        public abstract class Content
        {
            public abstract string GetPrintString(Processor processor);
            public abstract string GetCodeString(Formats.Formatter formatter);
        }

        public class TextContent : Content
        {
            public readonly string Text;
            public readonly string CodeText;

            public TextContent(string text, string codetext = null)
            {
                Text = text;
                CodeText = codetext ?? text;
            }

            public override string GetPrintString(Processor processor)
            {
                return Text;
            }

            public override string GetCodeString(Formats.Formatter formatter)
            {
                return CodeText;
            }
        }

        public class RegContent : Content
        {
            public readonly ValRegEx Reg;

            public RegContent(ValRegEx reg)
            {
                Reg = reg;
            }

            public override string GetPrintString(Processor processor)
            {
                return processor.Register[Reg].ToString();
            }

            public override string GetCodeString(Formats.Formatter formatter)
            {
                return Reg.GetCodeText(formatter);
            }
        }

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
                List<Content> contents = new List<Content>();
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
                        contents.Add(new RegContent(args.Formatter.GetRegEx(s)));
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

        protected override string _GetString(Formats.Formatter formatter)
        {
            return Contents.Length == 0 ? "PRINT" : $"PRINT {string.Join(" & ", Contents.Select(u => u.GetCodeString(formatter)))}";
        }

        public override void Exec(Processor processor)
        {
            processor.Output.Print(string.Join("", Contents.Select(u => u.GetPrintString(processor))), !processor.CancelLineBreak);
            processor.CancelLineBreak = CancelLineBreak;
        }

        public override void Assemble(Assembler assembler)
        { }
    }
}
