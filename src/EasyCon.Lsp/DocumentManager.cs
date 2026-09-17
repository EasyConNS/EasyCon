using EasyCon.Script;
using EasyCon.Script.Syntax;
using EasyCon.Script.Text;
using EmmyLua.LanguageServer.Framework.Protocol.Model;
using System.Collections.Concurrent;

#nullable enable

namespace EasyCon.Lsp;

internal sealed class DocumentManager
{
    private const int MaxDocuments = 64;

    private sealed class DocumentState
    {
        public DocumentUri Uri { get; init; } = new(new Uri("file://"));
        public string Source { get; init; } = "";
        public SyntaxTree? SyntaxTree { get; init; }
        public SourceText? SourceText => SyntaxTree?.Text;
    }

    private readonly ConcurrentDictionary<string, DocumentState> _documents = new();

    public void OpenDocument(DocumentUri uri, string source)
    {
        var key = uri.UnescapeUri;
        if (_documents.Count >= MaxDocuments && !_documents.ContainsKey(key))
        {
            var oldest = _documents.Keys.FirstOrDefault();
            if (oldest != null) _documents.TryRemove(oldest, out _);
        }
        var tree = SyntaxTree.Parse(source);
        _documents[key] = new() { Uri = uri, Source = source, SyntaxTree = tree };
    }

    public void UpdateDocument(DocumentUri uri, string source)
    {
        var key = uri.UnescapeUri;
        var tree = SyntaxTree.Parse(source);
        _documents[key] = new() { Uri = uri, Source = source, SyntaxTree = tree };
    }

    public void CloseDocument(DocumentUri uri)
    {
        _documents.TryRemove(uri.UnescapeUri, out _);
    }

    public SyntaxTree? GetSyntaxTree(DocumentUri uri)
    {
        return _documents.TryGetValue(uri.UnescapeUri, out var state) ? state.SyntaxTree : null;
    }

    public string? GetSource(DocumentUri uri)
    {
        return _documents.TryGetValue(uri.UnescapeUri, out var state) ? state.Source : null;
    }

    public SourceText? GetSourceText(DocumentUri uri)
    {
        return _documents.TryGetValue(uri.UnescapeUri, out var state) ? state.SourceText : null;
    }

    public string? GetLineText(DocumentUri uri, int line)
    {
        var sourceText = GetSourceText(uri);
        if (sourceText == null || line < 0 || line >= sourceText.Lines.Length) return null;
        return sourceText.Lines[line].Text;
    }

    public CompicationUnit? GetRoot(DocumentUri uri)
    {
        return _documents.TryGetValue(uri.UnescapeUri, out var state) ? state.SyntaxTree?.Root : null;
    }
}