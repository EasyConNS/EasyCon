using Avalonia;
using Avalonia.Controls;
using Avalonia.Themes.Fluent;
using EasyCon.Core.Services;
using EasyCon2.Avalonia.Services;

namespace EasyCon2.Avalonia.UiTests;

/// <summary>
/// WindowService 注册表化后的行为回归：
/// ① 同键不同工厂重复注册 → 启动期快速失败（InvalidOperationException）；
/// ② 未注册键 → fail-soft（返回 false 并记日志）；
/// ③ 六个标准 WindowKeys 的单例展示语义（首次创建返回 true / 已可见走激活路径返回 false）；
/// ④ 窗口 Closed 后再次 Show 可重新创建（返回 true）。
/// </summary>
[TestFixture]
public class WindowServiceRegistryTests
{
    private static bool _stylesInitialized;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        // 其他 fixture（如 MainWindowSaveTests）可能已初始化过 App；
        // SetupWithoutStarting 重复调用会抛 InvalidOperationException
        if (Application.Current == null)
            TestAppBuilder.BuildAvaloniaApp().SetupWithoutStarting();

        if (_stylesInitialized)
            return;

        if (!Application.Current!.Styles.OfType<FluentTheme>().Any())
            Application.Current.Styles.Add(new FluentTheme());
        _stylesInitialized = true;
    }

    /// <summary>最小窗口桩：纯 Avalonia Window，无任何 feature 依赖。</summary>
    private sealed class TestWindow : Window { }

    /// <summary>记录型日志服务：把 AddLog 的消息收集下来供断言。</summary>
    private sealed class RecordingLogService : ILogService
    {
        public List<string> Messages { get; } = new();
        public event Action<string?, string?>? LogAppended;
        public void Clear() => Messages.Clear();
        public void AddLog(string message, string? color = null) => Messages.Add(message);
        public void Print(string message, bool newline = true) { }
        public void Alert(string message) { }
        public string ReadLine() => "";
        public bool TryReadLine(out string line) { line = ""; return false; }
    }

    [Test]
    public void RegisterDuplicateKeyWithDifferentFactory_ThrowsInvalidOperationException()
    {
        var service = new WindowService(new RecordingLogService());
        service.Register(WindowKeys.EspConfig, () => new TestWindow());

        Assert.That(() => service.Register(WindowKeys.EspConfig, () => new TestWindow()),
            Throws.InvalidOperationException.With.Message.Contains("窗口键重复注册"));
    }

    [Test]
    public void RegisterSameFactoryTwice_DoesNotThrow()
    {
        var service = new WindowService(new RecordingLogService());
        Func<Window> factory = () => new TestWindow();

        Assert.DoesNotThrow(() =>
        {
            service.Register(WindowKeys.AlertConfig, factory);
            service.Register(WindowKeys.AlertConfig, factory);
        });
    }

    [Test]
    public void ShowUnregisteredKey_ReturnsFalseAndLogs()
    {
        var log = new RecordingLogService();
        var service = new WindowService(log);

        Assert.That(service.Show("no-such-key"), Is.False, "未注册键应 fail-soft 返回 false");
        Assert.That(log.Messages, Has.Count.EqualTo(1), "未注册键应记一条日志");
        Assert.That(log.Messages[0], Does.Contain("no-such-key"));
    }

    [Test]
    public void ShowAllWindowKeys_FirstShowCreates_SecondShowActivatesAndReturnsFalse()
    {
        var service = new WindowService(new RecordingLogService());
        var created = new List<Window>();
        string[] keys =
        [
            WindowKeys.EspConfig,
            WindowKeys.AlertConfig,
            WindowKeys.ModelsConfig,
            WindowKeys.McpConfig,
            WindowKeys.KeyMapping,
            WindowKeys.ScriptSyntax,
        ];
        foreach (var key in keys)
            service.Register(key, () => { var w = new TestWindow(); created.Add(w); return w; });

        foreach (var key in keys)
        {
            Assert.That(service.Show(key), Is.True, $"首次 Show({key}) 应创建窗口并返回 true");
            Assert.That(service.Show(key), Is.False, $"二次 Show({key}) 应走单例激活路径返回 false");
        }

        Assert.That(created, Has.Count.EqualTo(keys.Length), "每个键只应创建一个窗口实例");
    }

    [Test]
    public void ShowAfterWindowClosed_CreatesNewInstanceAndReturnsTrue()
    {
        var service = new WindowService(new RecordingLogService());
        var created = new List<Window>();
        service.Register(WindowKeys.KeyMapping, () => { var w = new TestWindow(); created.Add(w); return w; });

        Assert.That(service.Show(WindowKeys.KeyMapping), Is.True);
        created[0].Close();

        Assert.That(service.Show(WindowKeys.KeyMapping), Is.True, "窗口关闭后再次 Show 应重新创建并返回 true");
        Assert.That(created, Has.Count.EqualTo(2), "关闭后应通过工厂创建新实例");
    }
}