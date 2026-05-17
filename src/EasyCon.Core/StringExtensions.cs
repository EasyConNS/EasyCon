namespace EasyCon.Core;

public static class StringExtensions
{
    public static bool CanComment(this string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return false;

        int firstNonWhitespaceIndex = -1;
        for (int i = 0; i < input.Length; i++)
        {
            if (!char.IsWhiteSpace(input[i]))
            {
                firstNonWhitespaceIndex = i;
                break;
            }
        }

        if (firstNonWhitespaceIndex == -1)
            return false;

        return input[firstNonWhitespaceIndex] != '#';
    }

    public static string ToggleComment(this string input, bool comment)
    {
        if (string.IsNullOrWhiteSpace(input))
            return input ?? string.Empty;

        int firstNonWhitespaceIndex = -1;
        for (int i = 0; i < input.Length; i++)
        {
            if (!char.IsWhiteSpace(input[i]))
            {
                firstNonWhitespaceIndex = i;
                break;
            }
        }

        if (firstNonWhitespaceIndex == -1)
            return input;

        if (comment)
        {
            return input.Insert(firstNonWhitespaceIndex, "# ");
        }
        else
        {
            int removeCount = 1;
            if (firstNonWhitespaceIndex + 1 < input.Length && input[firstNonWhitespaceIndex + 1] == ' ')
                removeCount = 2;
            return input.Remove(firstNonWhitespaceIndex, removeCount);
        }
    }
}