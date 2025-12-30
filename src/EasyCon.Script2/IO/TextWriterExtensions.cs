using EasyCon.Script2.Syntax;
using System.Diagnostics;

namespace EasyCon.Script2.IO;

public static class TextWriterExtensions
{
    public static void WriteIdentifier(this TextWriter writer, string text)
    {
        //writer.SetForeground(ConsoleColor.DarkYellow);
        writer.Write(text);
        //writer.ResetColor();
    }
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
    public static void WriteSpace(this TextWriter writer)
    {
        writer.WritePunctuation(" ");
    }
    public static void WritePunctuation(this TextWriter writer, TokenType kind)
    {
        var text = TokenFacts.GetText(kind);
        Debug.Assert(text != null);

        writer.WritePunctuation(text);
    }

    public static void WritePunctuation(this TextWriter writer, string text)
    {
        //writer.SetForeground(ConsoleColor.DarkGray);
        writer.Write(text);
        //writer.ResetColor();
    }
}