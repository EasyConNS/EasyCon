namespace EasyCon2.Avalonia.FileTree;

public static class FileTreeSortModeSettings
{
    public static string ToSettingValue(FileTreeSortMode mode)
    {
        return mode switch
        {
            FileTreeSortMode.NameAscending => "name-asc",
            FileTreeSortMode.NameDescending => "name-desc",
            FileTreeSortMode.ModifiedNewestFirst => "modified-desc",
            FileTreeSortMode.ModifiedOldestFirst => "modified-asc",
            FileTreeSortMode.ExtensionAscending => "extension-asc",
            FileTreeSortMode.ExtensionDescending => "extension-desc",
            _ => "name-asc"
        };
    }

    public static FileTreeSortMode FromSettingValue(string? value)
    {
        return value switch
        {
            "name-asc" => FileTreeSortMode.NameAscending,
            "name-desc" => FileTreeSortMode.NameDescending,
            "modified-desc" => FileTreeSortMode.ModifiedNewestFirst,
            "modified-asc" => FileTreeSortMode.ModifiedOldestFirst,
            "extension-asc" => FileTreeSortMode.ExtensionAscending,
            "extension-desc" => FileTreeSortMode.ExtensionDescending,
            _ => FileTreeSortMode.NameAscending
        };
    }
}