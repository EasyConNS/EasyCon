
namespace EasyScript.Parsing.Lexers
{
    internal static partial class KeywordLexer
    {
        public static readonly Dictionary<string, Func<ParserArgument, Statement?>>
            KEYWORDS = new();

        internal static Statement? Parse(ParserArgument args)
        {
            if (args.Arguments.Count > 0)
            {
                string word = args.Arguments[0];
                if (KEYWORDS.TryGetValue(word.ToUpper(), out var func))
                {
                    return func(args);
                }
            }
            return null;
        }

        internal static void Register(string keyword, Func<ParserArgument, Statement?> func)
        {
            if (KEYWORDS.ContainsKey(keyword.ToUpper())) 
            { 
                throw new ArgumentException("Keyword already registered");
            }
            KEYWORDS.Add(keyword.ToUpper(), func);
        }
        
        static KeywordLexer()
        {
            KeyParser.Init();
            MsgParser.Init();
            ImportParser.Init();
            IfElseParser.Init();
            ForParser.Init();
        }

        
    }

}
