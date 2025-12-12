using EasyCon.Script2.Syntax;
using System.Diagnostics;

namespace EasyCon.Script2.IO;

public static class TextWriterExtensions
{
        public static void WriteKeyword(this TextWriter writer, TokenType kind)
        {
            var text = TokenFacts.GetText(kind);
            Debug.Assert(text != null);

            writer.WriteKeyword(text);
        }

        public static void WriteKeyword(this TextWriter writer, string text)
        {
            writer.Write(text);
        }
}