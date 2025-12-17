using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Folding;

namespace EasyCon2.Helper;

public class CustomFoldingStrategy
{
    // 定义代码块开始和结束标记
    private readonly Dictionary<string, string> blockPairs = new()
    {
        { "if", "endif" },
        { "for", "next" },
        { "func", "endfunc" }
    };
    public IEnumerable<NewFolding> CreateNewFoldings(TextDocument document, out int firstErrorOffset)
    {
        firstErrorOffset = -1; // 没有语法错误，设为-1
        var newFoldings = new List<NewFolding>();

        var startLines = new Stack<(int lineNumber, string blockType)>();

        for (int i = 0; i < document.LineCount; i++)
        {
            var line = document.GetText(document.Lines[i]);
            var trimmedLine = line.Trim().ToLower();

            // 检查是否是块开始
            foreach (var pair in blockPairs)
            {
                if (trimmedLine.StartsWith(pair.Key))
                {
                    startLines.Push((i, pair.Key));
                    break;
                }
            }

            // 检查是否是块结束
            if (startLines.Count > 0)
            {
                var currentBlock = startLines.Peek();
                var endMarker = blockPairs[currentBlock.blockType];

                if (trimmedLine.StartsWith(endMarker) || trimmedLine == endMarker)
                {
                    var startLine = startLines.Pop();

                    // 创建折叠区域
                    var startOffset = document.Lines[startLine.lineNumber].Offset;
                    var endOffset = document.Lines[i].EndOffset;

                    // 跳过空行
                    //while (i + 1 < document.Lines.Count &&
                    //       string.IsNullOrWhiteSpace(document.GetText(document.Lines[i + 1])))
                    //{
                    //    i++;
                    //    endOffset = document.Lines[i].EndOffset;
                    //}

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

        // 按开始位置排序
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
        // 此方法可用于在文档变化后更新折叠区域
        int firstErrorOffset;
        var newFoldings = CreateNewFoldings(document, out firstErrorOffset);
        manager.UpdateFoldings(newFoldings, firstErrorOffset);
    }
}