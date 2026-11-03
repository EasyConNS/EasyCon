using EasyCon.Core.Hosting;
using EasyCon.Core.Maa;
using EasyCon.Core.Projects;
using EasyCon.Core.Runner;
using EasyCon.Script;
using EasyCon.Tests.Support;
using EasyScript;
using System.Reflection;

namespace EasyCon.Tests;

/// <summary>
/// 测试 init 脚手架（ProjectScaffold）：布局与运行时识别规则一致
/// （lib/ 自动导入、ImgLabel/ 标签目录、models/ 相对路径约定），
/// 冲突预检无部分写入，--force 只覆盖模板文件；生成项目可编译并运行。
/// </summary>
[TestFixture]
public class ProjectScaffoldTests
{
    private string _root = null!;

    [SetUp]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), $"EasyConInit_{Guid.NewGuid():N}");
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, true);
    }

    [Test]
    public void Init_CreatesExpectedLayout()
    {
        ScaffoldResult result = ProjectScaffold.Create(_root);

        Assert.That(result.Success, Is.True, result.Error);
        Assert.That(File.Exists(Path.Combine(_root, ProjectLayout.MainFile)), Is.True);
        Assert.That(File.Exists(Path.Combine(_root, ProjectLayout.ModuleFile)), Is.True);
        Assert.That(Directory.Exists(Path.Combine(_root, ProjectLayout.LibDir)), Is.True);
        Assert.That(Directory.Exists(Path.Combine(_root, ProjectLayout.ModelsDir)), Is.True);
        Assert.That(Directory.Exists(Path.Combine(_root, ProjectLayout.LabelsDir)), Is.True);
        Assert.That(Directory.Exists(Path.Combine(_root, "obj")), Is.False, "obj/ 应由编译生成，不应由 init 预建");
        Assert.That(result.CreatedFiles, Is.EquivalentTo([ProjectLayout.MainFile, ProjectLayout.ModuleFile]));
        Assert.That(result.CreatedDirectories,
            Is.EquivalentTo([ProjectLayout.LibDir, ProjectLayout.ModelsDir, ProjectLayout.LabelsDir]));
    }

    [Test]
    public void Init_TemplatesMatchProjectConventions()
    {
        ScaffoldResult result = ProjectScaffold.Create(_root);
        Assert.That(result.Success, Is.True, result.Error);

        string main = File.ReadAllText(Path.Combine(_root, ProjectLayout.MainFile));
        string module = File.ReadAllText(Path.Combine(_root, ProjectLayout.ModuleFile));

        Assert.That(main, Does.Contain("ADD(1, 2)"));
        Assert.That(main, Does.Contain("欢迎使用伊机控"));
        Assert.That(main, Does.Contain("ezcon run main.ecs"));
        Assert.That(module, Does.Contain("FUNC ADD($a, $b):INT"));
        Assert.That(module, Does.Contain("RETURN $a + $b"));
        // 生成脚本按 LF + 结尾换行落盘（跨平台稳定）
        Assert.That(main, Does.Not.Contain("\r\n"));
        Assert.That(main, Does.EndWith("\n"));
    }

    [Test]
    public void Init_ExistingTemplateFiles_RefusedWithoutAnyWrite()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, ProjectLayout.MainFile), "old main");

        ScaffoldResult result = ProjectScaffold.Create(_root);

        Assert.That(result.Success, Is.False);
        Assert.That(result.Conflicts, Does.Contain(ProjectLayout.MainFile));
        Assert.That(Directory.Exists(Path.Combine(_root, ProjectLayout.LibDir)), Is.False, "冲突时不应创建目录");
        Assert.That(File.ReadAllText(Path.Combine(_root, ProjectLayout.MainFile)), Is.EqualTo("old main"));
    }

    [Test]
    public void Init_Force_OverwritesOnlyTemplates()
    {
        Directory.CreateDirectory(Path.Combine(_root, ProjectLayout.LibDir));
        File.WriteAllText(Path.Combine(_root, ProjectLayout.MainFile), "old main");
        File.WriteAllText(Path.Combine(_root, ProjectLayout.ModuleFile), "old module");

        ScaffoldResult result = ProjectScaffold.Create(_root, force: true);

        Assert.That(result.Success, Is.True, result.Error);
        Assert.That(File.ReadAllText(Path.Combine(_root, ProjectLayout.MainFile)), Does.Contain("欢迎使用伊机控"));
        Assert.That(File.ReadAllText(Path.Combine(_root, ProjectLayout.ModuleFile)), Does.Contain("FUNC ADD"));
        Assert.That(result.ExistingDirectories, Does.Contain(ProjectLayout.LibDir));
    }

    [Test]
    public void Init_TargetPathIsFile_Fails()
    {
        Directory.CreateDirectory(_root);
        string file = Path.Combine(_root, "not-a-directory.ecs");
        File.WriteAllText(file, "");

        ScaffoldResult result = ProjectScaffold.Create(file);

        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Does.Contain("文件而非目录"));
    }

    [Test]
    public void Init_DirectoryOccupiedByFile_Fails()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, ProjectLayout.LibDir), "occupied");

        ScaffoldResult result = ProjectScaffold.Create(_root);

        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Does.Contain("被文件占据"));
        Assert.That(Directory.Exists(Path.Combine(_root, ProjectLayout.ModelsDir)), Is.False);
    }

    [TestCase(ProjectLayout.MainFile, false)]
    [TestCase(ProjectLayout.MainFile, true)]
    [TestCase(ProjectLayout.ModuleFile, false)]
    [TestCase(ProjectLayout.ModuleFile, true)]
    public void Init_TemplateOccupiedByDirectory_RefusesBeforeWriting(string template, bool force)
    {
        Directory.CreateDirectory(Path.Combine(_root, template));
        if (template == ProjectLayout.ModuleFile)
            File.WriteAllText(Path.Combine(_root, ProjectLayout.MainFile), "old main");

        ScaffoldResult result = ProjectScaffold.Create(_root, force);

        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Does.Contain("被目录占据"));
        Assert.That(result.CreatedFiles, Is.Empty);
        Assert.That(result.CreatedDirectories, Is.Empty);
        Assert.That(Directory.Exists(Path.Combine(_root, ProjectLayout.ModelsDir)), Is.False);
        Assert.That(Directory.Exists(Path.Combine(_root, ProjectLayout.LabelsDir)), Is.False);
        if (template == ProjectLayout.ModuleFile)
            Assert.That(File.ReadAllText(Path.Combine(_root, ProjectLayout.MainFile)), Is.EqualTo("old main"));
        else
            Assert.That(Directory.Exists(Path.Combine(_root, ProjectLayout.LibDir)), Is.False);
    }

    [Test]
    public void WriteTemplate_FileCreatedAfterPreflight_IsNotOverwritten()
    {
        Directory.CreateDirectory(_root);
        string path = Path.Combine(_root, ProjectLayout.MainFile);
        File.WriteAllText(path, "concurrent user script");
        // 直接验证写入边界，避免依赖线程调度重现预检后的短暂竞争窗口。
        Action<string, string, bool> writeTemplate = typeof(ProjectScaffold)
            .GetMethod("WriteTemplate", BindingFlags.NonPublic | BindingFlags.Static)!
            .CreateDelegate<Action<string, string, bool>>();

        Assert.Throws<IOException>(() => writeTemplate(path, ProjectScaffold.MainTemplate, false));
        Assert.That(File.ReadAllText(path), Is.EqualTo("concurrent user script"));
    }

    [Test]
    public void GeneratedProject_CompilesAndPrintsWelcome()
    {
        ScaffoldResult scaffold = ProjectScaffold.Create(_root);
        Assert.That(scaffold.Success, Is.True, scaffold.Error);

        string mainPath = Path.Combine(_root, ProjectLayout.MainFile);
        CompileResult compilation = Compilation.CompileFile(mainPath, ScriptCompileProfiles.Interactive());
        List<string> errors = compilation.Diagnostics.Where(d => d.IsError).Select(d => d.Message).ToList();
        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(compilation.Image, Is.Not.Null);

        MockOutputAdapter output = new();
        EcxVm.Run(compilation.Image!, EcsTestHost.Capabilities(output),
            CancellationToken.None, [], compilation.NativeSymbols);

        Assert.That(output.Printed, Has.Some.EqualTo("欢迎使用伊机控\n"));
        Assert.That(output.Printed, Has.Some.EqualTo("ADD(1, 2) = 3\n"));
    }

    [Test]
    public void GeneratedProject_LabelsDirectoryIsEmpty()
    {
        ScaffoldResult scaffold = ProjectScaffold.Create(_root);
        Assert.That(scaffold.Success, Is.True, scaffold.Error);

        LabelDiscovery discovery = LegacyLabelCatalog.FromParents(_root);
        Assert.That(discovery.Selected, Is.Empty);
        Assert.That(discovery.Diagnostics, Is.Empty);
        Assert.That(discovery.TotalFiles, Is.Zero);
    }
}