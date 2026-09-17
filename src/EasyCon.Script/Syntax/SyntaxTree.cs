using EasyCon.Script.Text;
using System.Collections.Immutable;

namespace EasyCon.Script.Syntax;

public sealed class SyntaxTree
{
    private delegate void ParseHandler(SyntaxTree syntaxTree,
                                           out CompicationUnit root,
                                           out ImmutableArray<Diagnostic> diagnostics);
    public SourceText Text { get; init; }
    /// <summary>旧版语法兼容（v1 PRINT/IF= 语义；默认 true 保持行为）。
    /// 经 CompileOptions.LegacySyntax 逐层下传（Parse/ParseTokens/BindProgram），无静态全局。</summary>
    public bool LegacySyntax { get; private set; } = true;
    public ImmutableArray<Diagnostic> Diagnostics { get; init; }

    internal CompicationUnit Root { get; init; }

    private SyntaxTree(SourceText text, ParseHandler handler, bool legacySyntax)
    {
        Text = text;
        LegacySyntax = legacySyntax;

        handler(this, out var root, out var diagnostics);

        Diagnostics = diagnostics;
        Root = root;
    }

    public static SyntaxTree Load(string fileName)
    {
        var text = File.ReadAllText(fileName);
        var sourceText = SourceText.From(text, fileName);
        return Parse(sourceText);
    }

    private static void Parse(SyntaxTree syntaxTree, out CompicationUnit root, out ImmutableArray<Diagnostic> diagnostics)
    {
        var parser = new Parser(syntaxTree);
        root = parser.ParseProgram();
        diagnostics = [.. parser.Diagnostics];
    }

    public static SyntaxTree Parse(string text, bool legacySyntax = true)
    {
        return Parse(SourceText.From(text), legacySyntax);
    }

    public static SyntaxTree Parse(SourceText text, bool legacySyntax = true)
    {
        return new SyntaxTree(text, Parse, legacySyntax);
    }

    public static ImmutableArray<Token> ParseTokens(string text, bool legacySyntax = true)
    {
        var sourceText = SourceText.From(text);
        return ParseTokens(sourceText, out _);
    }

    /// <summary>带文件名的词法扫描（轻量导入扫描用；诊断位置归属正确）。</summary>
    internal static ImmutableArray<Token> ParseTokens(SourceText text, bool legacySyntax = true)
        => ParseTokens(text, out _, legacySyntax);

    private static ImmutableArray<Token> ParseTokens(SourceText text, out ImmutableArray<Diagnostic> diagnostics, bool legacySyntax = true)
    {
        var tokens = new ImmutableArray<Token>();

        void ParseTokensHandler(SyntaxTree st, out CompicationUnit _, out ImmutableArray<Diagnostic> d)
        {
            _ = new([]);
            d = default;
            var l = new Lexer(st);
            tokens = l.Tokenize();
            d = [.. l.Diagnostics];
        }

        var syntaxTree = new SyntaxTree(text, ParseTokensHandler, legacySyntax);
        diagnostics = syntaxTree.Diagnostics;
        return tokens;
    }
}