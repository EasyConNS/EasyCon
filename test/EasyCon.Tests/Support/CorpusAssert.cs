namespace EasyCon.Tests.Support;

/// <summary>
/// 语料与例程路径解析（docs/Pipeline.md）：仓库根目录向上探测，
/// 供 corpus/ 数据驱动用例与 examples 全链路用例共享。
/// </summary>
internal static class CorpusAssert
{
    /// <summary>仓库根目录（含 src/ 与 test/ 的祖先目录）。</summary>
    public static string RepoRoot()
    {
        var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        for (int i = 0; i < 8 && dir != null; i++, dir = dir.Parent)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "src", "EasyCon.Vm", "native")))
                return dir.FullName;
        }
        throw new InvalidOperationException("仓库根目录未找到");
    }

    public static string CorpusDir()
        => Path.Combine(RepoRoot(), "test", "EasyCon.Tests", "Bytecode", "corpus");

    public static IEnumerable<string> CorpusCases()
        => Directory.GetFiles(CorpusDir(), "*.ecs").OrderBy(f => f, StringComparer.Ordinal);

    /// <summary>examples/ 下例程的绝对路径；不存在返回空串（调用方 Ignore）。</summary>
    public static string ExamplePath(string fileName)
    {
        var path = Path.Combine(RepoRoot(), "examples", fileName);
        return File.Exists(path) ? path : "";
    }
}