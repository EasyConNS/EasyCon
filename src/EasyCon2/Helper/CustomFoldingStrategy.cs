using AvaloniaEdit.Document;
using AvaloniaEdit.Folding;

namespace EasyCon2.Helper;

public class CustomFoldingStrategy
{
    private readonly Dictionary<string, string> blockPairs = new()
    {
        { "if", "endif" },
        { "for", "next" },
        { "func", "endfunc" }
    };

    public IEnumerable<NewFolding> CreateNewFoldings(TextDocument document, out int firstErrorOffset)
    {
        firstErrorOffset = -1;
        var newFoldings = new List<NewFolding>();
        var startLines = new Stack<(int lineNumber, string blockType)>();

        for (int i = 0; i < document.LineCount; i++)
        {
            var line = document.GetText(document.Lines[i]);
            var trimmedLine = line.Trim().ToLower();

            foreach (var pair in blockPairs)
            {
                if (trimmedLine.StartsWith(pair.Key))
                {
                    startLines.Push((i, pair.Key));
                    break;
                }
            }

            if (startLines.Count > 0)
            {
                var currentBlock = startLines.Peek();
                var endMarker = blockPairs[currentBlock.blockType];

                if (trimmedLine.StartsWith(endMarker) || trimmedLine == endMarker)
                {
                    var startLine = startLines.Pop();
                    var startOffset = document.Lines[startLine.lineNumber].Offset;
                    var endOffset = document.Lines[i].EndOffset;

                    newFoldings.Add(new NewFolding
                    {
                        StartOffset = startOffset,
                        EndOffset = endOffset,
                        Name = GetFoldingName(document, startLine.lineNumber, currentBlock.blockType),
                        DefaultClosed = false
                    });
                }
            }
        }

        newFoldings.Sort((a, b) => a.StartOffset.CompareTo(b.StartOffset));
        return newFoldings;
    }

    private string GetFoldingName(TextDocument document, int lineNumber, string blockType)
    {
        var line = document.GetText(document.Lines[lineNumber]);
        return $"{blockType.ToUpper()}: {line.Trim()[blockType.Length..]}";
    }

    public void UpdateFoldings(FoldingManager manager, TextDocument document)
    {
        int firstErrorOffset;
        var newFoldings = CreateNewFoldings(document, out firstErrorOffset);
        manager.UpdateFoldings(newFoldings, firstErrorOffset);
    }
}
