namespace EasyCon2.Avalonia.FileTree;

public sealed class FileTreeSortComparer : IComparer<FileTreeSortKey>
{
    private readonly FileTreeSortMode _mode;

    public FileTreeSortComparer(FileTreeSortMode mode)
    {
        _mode = mode;
    }

    public int Compare(FileTreeSortKey left, FileTreeSortKey right)
    {
        if (left.IsDirectory != right.IsDirectory)
            return left.IsDirectory ? -1 : 1;

        if (left.IsDirectory)
        {
            int directoryComparison = CompareNameKey(left, right);
            return _mode == FileTreeSortMode.NameDescending ? -directoryComparison : directoryComparison;
        }

        return _mode switch
        {
            FileTreeSortMode.NameAscending => CompareNameKey(left, right),
            FileTreeSortMode.NameDescending => -CompareNameKey(left, right),
            FileTreeSortMode.ModifiedNewestFirst => CompareModified(left, right, descending: true),
            FileTreeSortMode.ModifiedOldestFirst => CompareModified(left, right, descending: false),
            FileTreeSortMode.ExtensionAscending => CompareExtension(left, right, descending: false),
            FileTreeSortMode.ExtensionDescending => CompareExtension(left, right, descending: true),
            _ => CompareNameKey(left, right)
        };
    }

    private static int CompareModified(FileTreeSortKey left, FileTreeSortKey right, bool descending)
    {
        if (left.LastWriteTimeUtc.HasValue != right.LastWriteTimeUtc.HasValue)
            return left.LastWriteTimeUtc.HasValue ? -1 : 1;

        if (left.LastWriteTimeUtc.HasValue)
        {
            int modifiedComparison = left.LastWriteTimeUtc.Value.CompareTo(right.LastWriteTimeUtc!.Value);
            if (modifiedComparison != 0)
                return descending ? -modifiedComparison : modifiedComparison;
        }

        return CompareNameKey(left, right);
    }

    private static int CompareExtension(FileTreeSortKey left, FileTreeSortKey right, bool descending)
    {
        bool leftHasExtension = !string.IsNullOrEmpty(left.Extension);
        bool rightHasExtension = !string.IsNullOrEmpty(right.Extension);
        if (leftHasExtension != rightHasExtension)
            return leftHasExtension ? -1 : 1;

        if (leftHasExtension)
        {
            int extensionComparison = StringComparer.OrdinalIgnoreCase.Compare(left.Extension, right.Extension);
            if (extensionComparison != 0)
                return descending ? -extensionComparison : extensionComparison;
        }

        return CompareNameKey(left, right);
    }

    private static int CompareNameKey(FileTreeSortKey left, FileTreeSortKey right)
    {
        int nameComparison = NaturalFileNameComparer.Instance.Compare(left.Name, right.Name);
        if (nameComparison != 0)
            return nameComparison;

        int ordinalNameComparison = StringComparer.Ordinal.Compare(left.Name, right.Name);
        if (ordinalNameComparison != 0)
            return ordinalNameComparison;

        return StringComparer.Ordinal.Compare(left.FullPath, right.FullPath);
    }
}