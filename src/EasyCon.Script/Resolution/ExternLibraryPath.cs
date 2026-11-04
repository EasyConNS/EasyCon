namespace EasyCon.Script.Resolution;

/// <summary>
/// Resolves path-like EXTERN libraries relative to the ECS file that declares them.
/// Bare library names retain the operating system's normal loader search behavior.
/// </summary>
internal static class ExternLibraryPath
{
    public static string Resolve(string libraryName, string sourceFileName)
    {
        if (string.IsNullOrWhiteSpace(libraryName)
            || Path.IsPathFullyQualified(libraryName)
            || !LooksLikeRelativePath(libraryName)
            || string.IsNullOrWhiteSpace(sourceFileName)
            || sourceFileName.StartsWith('<'))
        {
            return libraryName;
        }

        string sourcePath = Path.GetFullPath(sourceFileName);
        string? sourceDirectory = Path.GetDirectoryName(sourcePath);
        return string.IsNullOrEmpty(sourceDirectory)
            ? libraryName
            : Path.GetFullPath(Path.Combine(sourceDirectory, libraryName));
    }

    private static bool LooksLikeRelativePath(string libraryName)
        => libraryName.Contains('/') || libraryName.Contains('\\');
}