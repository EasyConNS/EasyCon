using System.Collections;

namespace EasyCon2.Avalonia.FileTree;

/// <summary>序数、忽略大小写的文件名自然比较；仅将 ASCII 数字段按数值比较。</summary>
public sealed class NaturalFileNameComparer : IComparer<string>, IComparer
{
    public static NaturalFileNameComparer Instance { get; } = new();

    private NaturalFileNameComparer()
    {
    }

    public int Compare(string? left, string? right)
    {
        if (ReferenceEquals(left, right))
            return 0;
        if (left is null)
            return -1;
        if (right is null)
            return 1;

        int leftIndex = 0;
        int rightIndex = 0;
        while (leftIndex < left.Length && rightIndex < right.Length)
        {
            bool leftIsDigit = IsAsciiDigit(left[leftIndex]);
            bool rightIsDigit = IsAsciiDigit(right[rightIndex]);
            if (leftIsDigit && rightIsDigit)
            {
                int leftEnd = FindDigitRunEnd(left, leftIndex);
                int rightEnd = FindDigitRunEnd(right, rightIndex);
                int numberComparison = CompareDigitRuns(left, leftIndex, leftEnd, right, rightIndex, rightEnd);
                if (numberComparison != 0)
                    return numberComparison;

                leftIndex = leftEnd;
                rightIndex = rightEnd;
                continue;
            }

            if (leftIsDigit != rightIsDigit)
                return CompareCharsIgnoreCase(left[leftIndex], right[rightIndex]);

            int leftTextEnd = FindTextRunEnd(left, leftIndex);
            int rightTextEnd = FindTextRunEnd(right, rightIndex);
            int textComparison = string.Compare(
                left,
                leftIndex,
                right,
                rightIndex,
                Math.Min(leftTextEnd - leftIndex, rightTextEnd - rightIndex),
                StringComparison.OrdinalIgnoreCase);
            if (textComparison != 0)
                return textComparison;

            int leftTextLength = leftTextEnd - leftIndex;
            int rightTextLength = rightTextEnd - rightIndex;
            if (leftTextLength != rightTextLength)
                return leftTextLength.CompareTo(rightTextLength);

            leftIndex = leftTextEnd;
            rightIndex = rightTextEnd;
        }

        return (left.Length - leftIndex).CompareTo(right.Length - rightIndex);
    }

    int IComparer.Compare(object? x, object? y)
    {
        if (x is null && y is null)
            return 0;
        if (x is string left && y is string right)
            return Compare(left, right);
        throw new ArgumentException("自然文件名比较器只接受字符串。");
    }

    private static int CompareDigitRuns(string left, int leftStart, int leftEnd, string right, int rightStart, int rightEnd)
    {
        int leftSignificantStart = leftStart;
        int rightSignificantStart = rightStart;
        while (leftSignificantStart < leftEnd - 1 && left[leftSignificantStart] == '0')
            leftSignificantStart++;
        while (rightSignificantStart < rightEnd - 1 && right[rightSignificantStart] == '0')
            rightSignificantStart++;

        int leftSignificantLength = leftEnd - leftSignificantStart;
        int rightSignificantLength = rightEnd - rightSignificantStart;
        if (leftSignificantLength != rightSignificantLength)
            return leftSignificantLength.CompareTo(rightSignificantLength);

        for (int offset = 0; offset < leftSignificantLength; offset++)
        {
            int digitComparison = left[leftSignificantStart + offset].CompareTo(right[rightSignificantStart + offset]);
            if (digitComparison != 0)
                return digitComparison;
        }

        int leftRunLength = leftEnd - leftStart;
        int rightRunLength = rightEnd - rightStart;
        return leftRunLength.CompareTo(rightRunLength);
    }

    private static int FindDigitRunEnd(string value, int start)
    {
        int index = start;
        while (index < value.Length && IsAsciiDigit(value[index]))
            index++;
        return index;
    }

    private static int FindTextRunEnd(string value, int start)
    {
        int index = start;
        while (index < value.Length && !IsAsciiDigit(value[index]))
            index++;
        return index;
    }

    private static bool IsAsciiDigit(char value) => value is >= '0' and <= '9';

    private static int CompareCharsIgnoreCase(char left, char right)
    {
        return char.ToUpperInvariant(left).CompareTo(char.ToUpperInvariant(right));
    }
}