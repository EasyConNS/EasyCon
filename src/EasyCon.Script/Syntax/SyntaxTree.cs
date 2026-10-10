using EasyCon.Script.Text;
using System.Collections.Immutable;

namespace EasyCon.Script.Syntax;

public sealed class SyntaxTree
{
    private delegate void ParseHandler(SyntaxTree syntaxTree,
                                       out CompilationUnit root,
                                       out ImmutableArray<Diagnostic> diagnostics);
    public SourceText Text { get; init; }
    public ImmutableArray<Diagnostic> Diagnostics { get; init; }

    internal CompilationUnit Root { get; init; }

    private SyntaxTree(SourceText text, ParseHandler handler, string? importBase)
    {
        Text = text;
        ImportBase = importBase ?? (Text.FileName != ""
            ? Path.GetDirectoryName(Path.GetFullPath(Text.FileName))!
            : null);

        handler(this, out var root, out var diagnostics);

        Diagnostics = diagnostics;
        Root = root;
    }

    /// <summary>IMPORT 解析基准（导入基准 = 导入文件所在目录（docs/ModuleImportRules.md R2））。
    /// null = 单文件无路径场景（IMPORT 一律报不存在）。</summary>
    public string? ImportBase { get; }

    public static SyntaxTree Load(string fileName)
    {
        var text = File.ReadAllText(fileName);
        var sourceText = SourceText.From(text, fileName);
        return Parse(sourceText);
    }

    private static void Parse(SyntaxTree syntaxTree, out CompilationUnit root, out ImmutableArray<Diagnostic> diagnostics)
    {
        var parser = new Parser(syntaxTree);
        root = parser.ParseProgram();
        diagnostics = [.. parser.Diagnostics];
    }

    public static SyntaxTree Parse(string text)
    {
        return Parse(SourceText.From(text));
    }

    public static SyntaxTree Parse(SourceText text, string? importBase = null)
    {
        return new SyntaxTree(text, Parse, importBase);
    }

    public static ImmutableArray<Token> ParseTokens(string text, string? importBase = null)
    {
        var sourceText = SourceText.From(text);
        return ParseTokens(sourceText, out _, importBase);
    }

    /// <summary>带文件名的词法扫描（轻量导入扫描用；诊断位置归属正确）。</summary>
    internal static ImmutableArray<Token> ParseTokens(SourceText text, string? importBase = null)
        => ParseTokens(text, out _, importBase);

    private static ImmutableArray<Token> ParseTokens(SourceText text, out ImmutableArray<Diagnostic> diagnostics, string? importBase = null)
    {
        var tokens = new ImmutableArray<Token>();

        void ParseTokensHandler(SyntaxTree st, out CompilationUnit _, out ImmutableArray<Diagnostic> d)
        {
            _ = new([]);
            d = default;
            var l = new Lexer(st);
            tokens = l.Tokenize();
            d = [.. l.Diagnostics];
        }

        var syntaxTree = new SyntaxTree(text, ParseTokensHandler, importBase);
        diagnostics = syntaxTree.Diagnostics;
        return tokens;
    }
}
