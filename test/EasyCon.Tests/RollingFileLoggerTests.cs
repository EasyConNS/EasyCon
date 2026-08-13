using EasyCon.Core.Logging;

namespace EasyCon.Tests;

/// <summary>
/// RollingFileLogger 滚动日志文件回归：验证写行落盘、日期文件命名与尺寸切分。
/// </summary>
[TestFixture]
public class RollingFileLoggerTests
{
    private string? _tempDir;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "easycon_logs_" + Guid.NewGuid().ToString("N"));
    }

    [TearDown]
    public void TearDown()
    {
        if (_tempDir != null && Directory.Exists(_tempDir))
        {
            try
            {
                Directory.Delete(_tempDir, true);
            }
            catch
            {
                // 清理失败（如文件仍被占用）不阻断测试结果
            }
        }
    }

    [Test]
    public void WriteLines_ThenDispose_CreatesLogFile_ContainingAllLines()
    {
        using var logger = new RollingFileLogger(_tempDir);
        for (int i = 0; i < 100; i++)
            logger.WriteLine($"line-{i}");
        logger.Dispose();

        var files = Directory.GetFiles(_tempDir!, "easycon_*.log");
        Assert.That(files, Is.Not.Empty);

        var content = string.Concat(files.OrderBy(f => f, StringComparer.Ordinal).Select(File.ReadAllText));
        for (int i = 0; i < 100; i++)
            Assert.That(content, Does.Contain($"line-{i}"));
    }

    [Test]
    public void LargeLines_TriggerSizeBasedRolling()
    {
        // 单行远大于 1KB 上限，迫使第二个文件（_001）出现
        using var logger = new RollingFileLogger(_tempDir, maxFileSizeBytes: 1024);
        logger.WriteLine(new string('A', 4096));
        logger.WriteLine(new string('B', 4096));
        logger.Dispose();

        var files = Directory.GetFiles(_tempDir!, "easycon_*.log");
        Assert.That(files.Length, Is.GreaterThanOrEqualTo(2));
        Assert.That(files.Any(f => Path.GetFileName(f).EndsWith("_001.log")), Is.True);
    }
}
