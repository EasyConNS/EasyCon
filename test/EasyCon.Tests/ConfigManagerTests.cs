using EasyCon.Core.Config;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace EasyCon.Tests;

/// <summary>
/// ConfigManager 的写入原子性与损坏恢复行为。
/// 通过 internal 测试入口注入临时目录路径，不触碰真实用户配置。
/// </summary>
[TestFixture]
public partial class ConfigManagerTests
{
    private string _dir = "";
    private string _path = "";

    private sealed class SampleConfig
    {
        public string Name { get; set; } = "";
        public int Level { get; set; }
    }

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "easycon-config-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _path = Path.Combine(_dir, "sample.json");
    }

    [TearDown]
    public void TearDown()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* 临时目录清理失败可忽略 */ }
    }

    private List<string> SnapshotDir() =>
        Directory.Exists(_dir) ? Directory.GetFiles(_dir).Select(Path.GetFileName).OrderBy(x => x).ToList() : [];

    [Test]
    public void SaveTo_RoundTripsData_AndLeavesNoTempFile()
    {
        ConfigManager.SaveTo(_path, new SampleConfig { Name = "abc", Level = 3 });

        var loaded = ConfigManager.LoadFrom<SampleConfig>(_path);
        var files = SnapshotDir();

        Assert.Multiple(() =>
        {
            Assert.That(loaded.Name, Is.EqualTo("abc"));
            Assert.That(loaded.Level, Is.EqualTo(3));
            Assert.That(files, Is.EqualTo(new[] { "sample.json" }), "原子写不应残留 .tmp 文件");
        });
    }

    [Test]
    public void SaveTo_OverwritesExistingContent()
    {
        ConfigManager.SaveTo(_path, new SampleConfig { Name = "old" });
        ConfigManager.SaveTo(_path, new SampleConfig { Name = "new" });

        var loaded = ConfigManager.LoadFrom<SampleConfig>(_path);
        Assert.That(loaded.Name, Is.EqualTo("new"));
    }

    [Test]
    public void LoadFrom_CorruptFile_BackupsReportsAndReturnsDefaults()
    {
        ConfigManager.SaveTo(_path, new SampleConfig { Name = "keep-me", Level = 7 });
        var raised = new List<(string Path, string Message)>();
        ConfigManager.ConfigErrorReported += Handler;
        try
        {
            File.WriteAllText(_path, "{ this is not json !!!");

            var loaded = ConfigManager.LoadFrom<SampleConfig>(_path);
            var files = SnapshotDir();

            Assert.Multiple(() =>
            {
                Assert.That(loaded.Name, Is.EqualTo(string.Empty), "损坏配置应回退到默认值");
                Assert.That(loaded.Level, Is.EqualTo(0));
                Assert.That(files.Any(f => f.StartsWith("sample.json.corrupt-")), Is.True,
                    $"损坏文件应先备份，实际目录: {string.Join(", ", files)}");
                Assert.That(raised, Has.Count.EqualTo(1), "应通过 ConfigErrorReported 上报");
                Assert.That(raised[0].Path, Is.EqualTo(_path));
            });
        }
        finally
        {
            ConfigManager.ConfigErrorReported -= Handler;
        }

        void Handler(string path, string message) => raised.Add((path, message));
    }

    [Test]
    public void LoadFrom_MissingFile_ReturnsDefaultsWithoutEvent()
    {
        var raised = 0;
        ConfigManager.ConfigErrorReported += Handler;
        try
        {
            var loaded = ConfigManager.LoadFrom<SampleConfig>(_path);
            Assert.That(loaded.Name, Is.EqualTo(string.Empty));
        }
        finally
        {
            ConfigManager.ConfigErrorReported -= Handler;
        }

        Assert.That(raised, Is.Zero, "文件不存在属正常路径，不应上报错误");

        void Handler(string path, string message) => raised++;
    }

    [Test]
    public void WriteDefaultTo_CreatesFileWithGivenContent()
    {
        var json = """{"name":"default"}""";

        ConfigManager.WriteDefaultTo(_path, json);

        Assert.Multiple(() =>
        {
            Assert.That(File.ReadAllText(_path), Is.EqualTo(json));
            Assert.That(SnapshotDir(), Is.EqualTo(new[] { "sample.json" }), "默认文件写入也不应残留 .tmp");
        });
    }

    [Test]
    public void WriteDefaultTo_UnderMissingDirectory_DoesNotThrow()
    {
        var nested = Path.Combine(_dir, "no", "such", "dir.json");

        Assert.DoesNotThrow(() => ConfigManager.WriteDefaultTo(nested, "{}"));
    }

    [Test]
    public void DefaultModelsJson_ContainsOnlyAnObviousPlaceholderKey()
    {
        // 防回归：默认 models.json 不得内置任何"看起来真实"的密钥
        var json = ConfigManager.DefaultModelsJson;

        Assert.Multiple(() =>
        {
            Assert.That(json, Does.Contain("sk-REPLACE_WITH_YOUR_KEY"));
            Assert.That(JsonRegex().IsMatch(json), Is.False,
                "默认模板中不应出现 40 位以上的真实样式 API Key");
        });
    }

    [GeneratedRegex(@"sk-[A-Za-z0-9]{40,}")]
    private static partial Regex JsonRegex();
}