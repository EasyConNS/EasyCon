using EasyCon.Script.Syntax;
using EmmyLua.LanguageServer.Framework.Protocol.Capabilities.Client.ClientCapabilities;
using EmmyLua.LanguageServer.Framework.Protocol.Capabilities.Server;
using EmmyLua.LanguageServer.Framework.Protocol.Message.DocumentSymbol;
using EmmyLua.LanguageServer.Framework.Protocol.Model;
using EmmyLua.LanguageServer.Framework.Protocol.Model.Kind;
using EmmyLua.LanguageServer.Framework.Server.Handler;

namespace EasyCon.Lsp.Handlers;

internal sealed class EcsDocumentSymbolHandler(DocumentManager docManager) : DocumentSymbolHandlerBase
{
    protected override Task<DocumentSymbolResponse> Handle(DocumentSymbolParams request, CancellationToken token)
    {
        var uri = request.TextDocument.Uri;
        var root = docManager.GetRoot(uri);
        if (root == null)
            return Task.FromResult(new DocumentSymbolResponse((List<DocumentSymbol>?)null));

        var symbols = new List<DocumentSymbol>();
        foreach (var stmt in root.Members)
        {
            var sym = ToDocumentSymbol(stmt);
            if (sym != null)
                symbols.Add(sym);
        }

        return Task.FromResult(new DocumentSymbolResponse(symbols));
    }

    public override void RegisterCapability(ServerCapabilities serverCapabilities, ClientCapabilities clientCapabilities)
    {
        serverCapabilities.DocumentSymbolProvider = true;
    }

    private static DocumentSymbol? ToDocumentSymbol(Statement stmt)
    {
        switch (stmt)
        {
            case ConstantDeclStmt c:
                return new()
                {
                    Name = c.Constant.Tag,
                    Kind = SymbolKind.Constant,
                    Range = StmtRange(c),
                    SelectionRange = TokenRange(c.Constant.Syntax),
                };
            case AssignmentStmt a:
                return new()
                {
                    Name = a.DestVariable.Tag,
                    Kind = SymbolKind.Variable,
                    Range = StmtRange(a),
                    SelectionRange = TokenRange(a.DestVariable.Syntax),
                };
            case FuncDeclBlock fb:
                var funcSymbol = new DocumentSymbol
                {
                    Name = fb.Declare.Name,
                    Kind = SymbolKind.Function,
                    Range = BlockRange(fb, fb.End),
                    SelectionRange = TokenRange(fb.Declare.Identifier),
                    Children = [],
                };
                foreach (var p in fb.Declare.Paramters)
                {
                    funcSymbol.Children!.Add(new()
                    {
                        Name = p.Identifier.Tag,
                        Kind = SymbolKind.Variable,
                        Range = TokenRange(p.Identifier.Syntax),
                        SelectionRange = TokenRange(p.Identifier.Syntax),
                    });
                }
                return funcSymbol;
            case ExternFuncStmt ef:
                return new()
                {
                    Name = ef.Name,
                    Kind = SymbolKind.Function,
                    Detail = "EXTERN",
                    Range = StmtRange(ef),
                    SelectionRange = TokenRange(ef.Identifier),
                };
            case IfBlock ib:
                return new()
                {
                    Name = "IF",
                    Kind = SymbolKind.Namespace,
                    Range = BlockRangeFromTokens(ib.Condition.Syntax, ib.End.Syntax),
                    SelectionRange = TokenRange(ib.Condition.Syntax),
                };
            case ForBlock fb:
                return new()
                {
                    Name = "FOR",
                    Kind = SymbolKind.Namespace,
                    Range = BlockRangeFromTokens(fb.Condition.Syntax, fb.End.Syntax),
                    SelectionRange = TokenRange(fb.Condition.Syntax),
                };
            case WhileBlock wb:
                return new()
                {
                    Name = "WHILE",
                    Kind = SymbolKind.Namespace,
                    Range = BlockRangeFromTokens(wb.Condition.Syntax, wb.End.Syntax),
                    SelectionRange = TokenRange(wb.Condition.Syntax),
                };
            default:
                return null;
        }
    }

    private static DocumentRange StmtRange(Statement stmt)
    {
        var line = stmt.Line - 1;
        return new(new(line, 0), new(line, stmt.GetCodeText().Length));
    }

    private static DocumentRange TokenRange(Token? token)
    {
        if (token == null) return new(new(0, 0), new(0, 0));
        var line = token.Location.StartLine;
        var col = CharOffset(token);
        return new(new(line, col), new(line, col + token.Value.Length));
    }

    private static DocumentRange BlockRange(Statement start, EndBlockStmt end)
    {
        var startLine = start.Line - 1;
        var endLine = end.Line - 1;
        return new(new(startLine, 0), new(endLine, end.GetCodeText().Length));
    }

    private static DocumentRange BlockRangeFromTokens(Token? start, Token? end)
    {
        var s = start?.Location.StartLine ?? 0;
        var e = end?.Location.StartLine ?? s;
        return new(new(s, 0), new(e, 0));
    }

    private static int CharOffset(Token token)
    {
        if (token.Text == null) return 0;
        var line = token.Location.StartLine;
        if (line >= 0 && line < token.Text.Lines.Length)
            return token.Span.Start - token.Text.Lines[line].Start;
        return 0;
    }
}