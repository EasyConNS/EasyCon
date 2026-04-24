using AvaloniaEdit.Document;
using AvaloniaEdit.Folding;

namespace EasyCon2.Avalonia.Core.Editor;

public class CustomFoldingStrategy
{
    private readonly Dictionary<string, string> blockPairs = new()
    {
        { "if", "endif" },
        { "for", "next" },
        { "func", "endfunc" }
    };

    public void UpdateFoldings(FoldingManager manager, TextDocument document)
    {
        manager.UpdateFoldings(CreateNewFoldings(document), -1);
    }

    private List<NewFolding> CreateNewFoldings(TextDocument document)
    {
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
                    newFoldings.Add(new NewFolding
                    {
                        StartOffset = document.Lines[startLine.lineNumber].Offset,
                        EndOffset = document.Lines[i].EndOffset,
                        Name = $"{currentBlock.blockType.ToUpper()}: {document.GetText(document.Lines[startLine.lineNumber]).Trim()[currentBlock.blockType.Length..]}",
                        DefaultClosed = false
                    });
                }
            }
        }

        newFoldings.Sort((a, b) => a.StartOffset.CompareTo(b.StartOffset));
        return newFoldings;
    }
}