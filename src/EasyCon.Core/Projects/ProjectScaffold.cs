#nullable enable
using System.Text;

namespace EasyCon.Core.Projects;

/// <summary>脚手架布局路径（相对项目根；与运行时识别规则对齐：docs/Script.md §11、
/// ModuleGraphBuilder R3 自动导入、ECCore.ImgDir 标签目录、models/ 为脚本相对路径约定）。</summary>
public static class ProjectLayout
{
    /// <summary>主脚本文件名（同目录可并列多个主脚本，各自独立编译）。</summary>
    public const string MainFile = "main.ecs";

    /// <summary>库目录：主脚本同目录 lib/*.ecs 自动导入（R3），无需 IMPORT。</summary>
    public const string LibDir = "lib";

    /// <summary>示例库模块相对路径（lib/ 内模块与 main 之间同目录互见/自动导入）。</summary>
    public const string ModuleFile = "lib/mod1.ecs";

    /// <summary>模型资产目录（运行约定：脚本按项目相对路径引用，如 NET_LOAD("models/...")）。</summary>
    public const string ModelsDir = "models";

    /// <summary>识图标签目录（唯一识别名，见 <see cref="ECCore.ImgDir"/>；init 生成空目录）。</summary>
    public const string LabelsDir = ECCore.ImgDir;
}

/// <summary>init 命令的执行结果。</summary>
public sealed class ScaffoldResult
{
    public required string RootPath { get; init; }

    /// <summary>已创建的目录（相对项目根）。</summary>
    public List<string> CreatedDirectories { get; } = [];

    /// <summary>已存在并保留的目录（相对项目根）。</summary>
    public List<string> ExistingDirectories { get; } = [];

    /// <summary>已创建的文件（相对项目根）。</summary>
    public List<string> CreatedFiles { get; } = [];

    /// <summary>冲突项：已存在且未被覆盖的路径（相对项目根）。</summary>
    public List<string> Conflicts { get; } = [];

    /// <summary>IO/权限失败信息；非 null 时 <see cref="Success"/> 为 false。</summary>
    public string? Error { get; internal set; }

    public bool Success => Conflicts.Count == 0 && Error == null;
}

/// <summary>
/// 脚本项目脚手架：初始化基础项目框架（main.ecs + lib/mod1.ecs + models/ + ImgLabel/）。
/// 目录即包（类 Go）：main 同目录 lib/*.ecs 自动可见；ImgLabel/ 只识别顶层 *.IL；
/// models/ 只是脚本相对路径约定，无运行时识别。预检发现冲突时不写入；默认不覆盖并发新建的文件。
/// </summary>
public static class ProjectScaffold
{
    static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public const string MainTemplate =
        """
        # main.ecs — 伊机控脚本项目入口
        # 目录约定：
        #   lib/*.ecs      自动导入，可直接调用其中的 FUNC（本例 ADD 在 lib/mod1.ecs）
        #   ImgLabel/*.IL  识图标签（可留空）
        #   models/        本地模型/字典资产（可留空；脚本按项目相对路径引用）
        #
        # 运行：在项目根目录执行
        #   ezcon run main.ecs --port mock
        # 编译：ezcon compile main.ecs

        $sum = ADD(1, 2)
        PRINT "欢迎使用伊机控"
        PRINT "ADD(1, 2) = " & $sum
        """;

    public const string ModuleTemplate =
        """
        # lib/mod1.ecs — 项目库模块（main 自动可见，无需 IMPORT）
        FUNC ADD($a, $b):INT
            RETURN $a + $b
        ENDFUNC
        """;

    /// <summary>
    /// 初始化项目框架。rootPath 不存在时自动创建；已存在时只补缺失项。
    /// 预检发现模板文件已存在（force=false），或文件/目录位置被另一种类型占据时，不做任何写入。
    /// 默认不覆盖预检后并发新建的文件；IO 失败可能保留已完成项，记录于结果中。
    /// force 仅覆盖两个模板文件，不删除/清空任何目录。
    /// </summary>
    public static ScaffoldResult Create(string rootPath, bool force = false)
    {
        string root = Path.GetFullPath(rootPath);

        // 路径目标是已存在文件（如 init a.txt）→ 直接拒绝
        if (File.Exists(root))
            return new ScaffoldResult { RootPath = root, Error = $"目标路径是文件而非目录: {root}" };

        string[] dirs = [ProjectLayout.LibDir, ProjectLayout.ModelsDir, ProjectLayout.LabelsDir];
        string[] files = [ProjectLayout.MainFile, ProjectLayout.ModuleFile];

        var result = new ScaffoldResult { RootPath = root };

        // 目录占用预检：目录位置被同名文件占据属硬错误（--force 也不删除文件）
        foreach (string dir in dirs)
        {
            string full = Path.Combine(root, dir);
            if (File.Exists(full))
                return new ScaffoldResult { RootPath = root, Error = $"目录位置被文件占据: {full}" };
        }

        // 模板位置被目录占据时始终拒绝；force 只允许覆盖文件。
        foreach (string file in files)
        {
            string full = Path.Combine(root, file);
            if (Directory.Exists(full))
                return new ScaffoldResult { RootPath = root, Error = $"文件位置被目录占据: {full}" };
            if (!force && File.Exists(full))
                result.Conflicts.Add(file);
        }
        if (result.Conflicts.Count > 0)
            return result;

        try
        {
            Directory.CreateDirectory(root);
            foreach (string dir in dirs)
            {
                string full = Path.Combine(root, dir);
                if (Directory.Exists(full))
                    result.ExistingDirectories.Add(dir);
                else
                {
                    Directory.CreateDirectory(full);
                    result.CreatedDirectories.Add(dir);
                }
            }
            WriteTemplate(Path.Combine(root, ProjectLayout.MainFile), MainTemplate, force);
            result.CreatedFiles.Add(ProjectLayout.MainFile);
            WriteTemplate(Path.Combine(root, ProjectLayout.ModuleFile), ModuleTemplate, force);
            result.CreatedFiles.Add(ProjectLayout.ModuleFile);
            return result;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            result.Error = ex.Message;
            return result;
        }
    }

    /// <summary>统一 UTF-8 无 BOM + LF + 结尾换行，避免检出平台 EOL 影响生成结果。</summary>
    static void WriteTemplate(string path, string content, bool force)
    {
        // CreateNew 将不覆盖保证放在写入时，避免预检后并发创建的文件被截断。
        using FileStream stream = new(path, force ? FileMode.Create : FileMode.CreateNew, FileAccess.Write);
        using StreamWriter writer = new(stream, Utf8NoBom);
        writer.Write(content.Replace("\r\n", "\n") + "\n");
    }
}