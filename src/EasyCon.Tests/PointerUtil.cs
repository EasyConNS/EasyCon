namespace ECScript.Tests;

static class PointerUtil
{
    public const ulong INVALID_PTR = 0;

    public static ulong GetPointerAddress(string ptr, bool heaprealtive=true)
    {
        if (string.IsNullOrWhiteSpace(ptr) || ptr.IndexOfAny(new[] { '-', '/', '*' }) != -1)
            return INVALID_PTR;
        while (ptr.Contains("]]"))
            ptr = ptr.Replace("]]", "]+0]");
        string? finadd = null;
        if (!ptr.EndsWith("]"))
        {
            finadd = ptr.Split('+').Last();
            ptr = ptr[..ptr.LastIndexOf('+')];
        }
        var jumps = ptr.Replace("main", "").Replace("[", "").Replace("]", "").Split(new[] { "+" }, StringSplitOptions.RemoveEmptyEntries);
        if (jumps.Length == 0)
            return INVALID_PTR;

        Console.WriteLine(string.Join(" ", jumps));
        return 0;
    }

    public static string ExtendPointer(this string pointer, params uint[] jumps)
    {
        foreach (var jump in jumps)
            pointer = $"[{pointer}]+{jump:X}";
        return pointer;
    }
}