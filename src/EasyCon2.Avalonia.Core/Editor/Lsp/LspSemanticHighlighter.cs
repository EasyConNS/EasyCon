using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace EasyCon2.Avalonia.Core.Editor.Lsp;

public class LspSemanticHighlighter : IVisualLineTransformer
{
    private readonly LspTokenTypeMapper _mapper;
    private Dictionary<int, List<(int col, int len, int type, int mod)>>? _lineTokens;

    public LspSemanticHighlighter(LspTokenTypeMapper mapper)
    {
        _mapper = mapper;
    }

    public bool IsDark
    {
        get => _mapper.IsDark;
        set => _mapper.IsDark = value;
    }

    public void UpdateTokens(SemanticTokens tokens)
    {
        _lineTokens = DecodeTokens(tokens);
    }

    public void ClearTokens()
    {
        _lineTokens = null;
    }

    private static Dictionary<int, List<(int col, int len, int type, int mod)>> DecodeTokens(SemanticTokens tokens)
    {
        var result = new Dictionary<int, List<(int, int, int, int)>>();
        if (tokens.Data == null) return result;

        var data = tokens.Data.ToArray();
        int line = 0, col = 0;

        for (int i = 0; i + 5 <= data.Length; i += 5)
        {
            int deltaLine = data[i];
            int deltaStart = data[i + 1];
            int length = data[i + 2];
            int tokenType = data[i + 3];
            int tokenModifiers = data[i + 4];

            if (deltaLine > 0)
            {
                line += deltaLine;
                col = deltaStart;
            }
            else
            {
                col += deltaStart;
            }

            if (!result.ContainsKey(line))
                result[line] = [];

            result[line].Add((col, length, tokenType, tokenModifiers));
        }

        return result;
    }

    public void Transform(ITextRunConstructionContext context, IList<VisualLineElement> elements)
    {
        if (_lineTokens == null) return;

        var documentLine = context.VisualLine.FirstDocumentLine;
        int lineNumber = documentLine.LineNumber - 1;

        if (!_lineTokens.TryGetValue(lineNumber, out var tokens)) return;

        foreach (var (tokCol, tokLen, tokType, tokMod) in tokens)
        {
            var brush = _mapper.GetBrush(tokType, tokMod);
            int tokEnd = tokCol + tokLen;

            foreach (var element in elements)
            {
                if (element is not VisualLineText textElement) continue;

                int elemStart = textElement.RelativeTextOffset;
                int elemEnd = elemStart + textElement.VisualLength;

                if (elemEnd > tokCol && elemStart < tokEnd)
                    textElement.TextRunProperties.SetForegroundBrush(brush);
            }
        }
    }
}