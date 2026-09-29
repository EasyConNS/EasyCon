using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using EasyCon.Capture;
using EasyCon.Core.Config;
using EasyCon.Core.Services;
using EasyCon2.Avalonia.Core.Editor;
using EasyCon2.Avalonia.Core.Services;
using EasyCon2.Avalonia.Core.Threading;
using EasyCon2.Avalonia.Services;
using EasyCon2.Avalonia.ViewModels;
using EasyCon2.Avalonia.Views;
using EasyDevice;

namespace EasyCon2.Avalonia.UiTests;

[TestFixture]
public class MainWindowSaveTests
{
    private static bool _stylesInitialized;
    private string _tempRoot = "";

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        if (Application.Current == null)
            TestAppBuilder.BuildAvaloniaApp().SetupWithoutStarting();

        if (_stylesInitialized)
            return;

        if (!Application.Current!.Styles.OfType<FluentTheme>().Any())
            Application.Current.Styles.Add(new FluentTheme());
        AddStyleIfMissing("avares://EasyCon2.Avalonia/Resources/Styles/EasyConWorkbenchTheme.axaml");
        AddStyleIfMissing("avares://AvaloniaEdit/Themes/Fluent/AvaloniaEdit.xaml");
        _stylesInitialized = true;
    }

    [SetUp]
    public void SetUp()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "easycon-save-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    [TearDown]
    public void TearDown()
    {
        try { Directory.Delete(_tempRoot, recursive: true); } catch { }
    }

    [TestCase("classic", "ClassicScriptEditor")]
    [TestCase("card", "CardScriptEditor")]
    [Explicit("真实主窗口键盘路由用例需单独运行，避免共享 Headless Dispatcher 的测试线程交叉")]
    [Category("Manual")]
    public void CtrlS_SavesLatestEditorTextFromTheFocusedEditor(string themeStyle, string editorName)
    {
        string scriptPath = Path.Combine(_tempRoot, "focused-script.ecs");
        TestServices services = new() { SavePath = scriptPath };
        MainWindowViewModel vm = CreateViewModel(services, new ConfigState { ThemeStyleName = themeStyle });
        MainWindow? window = null;

        try
        {
            window = new MainWindow(initializeEditorServices: false) { DataContext = vm };
            window.Show();
            WaitUntil(() => window.IsVisible && window.FindControl<ScriptEditorControl>(editorName)?.IsEffectivelyVisible == true,
                TimeSpan.FromSeconds(5), "真实主窗口编辑器没有显示");
            window.Activate();
            Dispatcher.UIThread.RunJobs();

            ScriptEditorControl editor = window.FindControl<ScriptEditorControl>(editorName)!;
            editor.TextEditor.CaretOffset = editor.TextEditor.Document.TextLength;
            editor.TextEditor.TextArea.Focus();
            WaitUntil(() => editor.TextEditor.TextArea.IsFocused,
                TimeSpan.FromSeconds(3),
                $"编辑器文本区没有获得焦点（窗口激活={window.IsActive}，文本区可见={editor.TextEditor.TextArea.IsEffectivelyVisible}）");

            window.KeyTextInput("before-latest");
            const string expectedText = "before-latest";
            WaitUntil(() => vm.EditorText == expectedText, TimeSpan.FromSeconds(3), "输入没有同步到当前编辑文档");

            window.KeyPressQwerty(PhysicalKey.ControlLeft, RawInputModifiers.None);
            window.KeyPressQwerty(PhysicalKey.S, RawInputModifiers.Control);
            window.KeyReleaseQwerty(PhysicalKey.S, RawInputModifiers.Control);
            window.KeyReleaseQwerty(PhysicalKey.ControlLeft, RawInputModifiers.None);

            WaitUntil(() => !vm.IsScriptModified && File.ReadAllText(scriptPath) == expectedText,
                TimeSpan.FromSeconds(5), "空白编辑区直接输入后，Ctrl+S 没有保存最新文本");

            Assert.Multiple(() =>
            {
                Assert.That(File.ReadAllText(scriptPath), Is.EqualTo(expectedText));
                Assert.That(vm.IsScriptModified, Is.False);
                Assert.That(services.SaveDialogCalls, Is.EqualTo(1), "无路径文档首次 Ctrl+S 应打开一次保存对话框");
                Assert.That(services.Messages.Count(message => message.StartsWith("已保存脚本:", StringComparison.Ordinal)), Is.EqualTo(1),
                    "一次 Ctrl+S 只能进入一次保存流程");
            });

            string saveAsPath = Path.Combine(_tempRoot, "focused-script-copy.ecs");
            services.SavePath = saveAsPath;
            editor.TextEditor.CaretOffset = editor.TextEditor.Document.TextLength;
            window.KeyTextInput("-copy");
            const string expectedCopyText = "before-latest-copy";
            WaitUntil(() => vm.EditorText == expectedCopyText, TimeSpan.FromSeconds(3), "另存为前的输入没有同步到编辑文档");

            RawInputModifiers controlShift = RawInputModifiers.Control | RawInputModifiers.Shift;
            window.KeyPressQwerty(PhysicalKey.ControlLeft, RawInputModifiers.None);
            window.KeyPressQwerty(PhysicalKey.ShiftLeft, RawInputModifiers.Control);
            window.KeyPressQwerty(PhysicalKey.S, controlShift);
            window.KeyReleaseQwerty(PhysicalKey.S, controlShift);
            window.KeyReleaseQwerty(PhysicalKey.ShiftLeft, RawInputModifiers.Control);
            window.KeyReleaseQwerty(PhysicalKey.ControlLeft, RawInputModifiers.None);

            WaitUntil(() => !vm.IsScriptModified && File.Exists(saveAsPath),
                TimeSpan.FromSeconds(5), "Ctrl+Shift+S 没有保存到另存为目标");
            Assert.Multiple(() =>
            {
                Assert.That(File.ReadAllText(saveAsPath), Is.EqualTo(expectedCopyText));
                Assert.That(File.ReadAllText(scriptPath), Is.EqualTo(expectedText), "另存为不能覆盖原路径");
                Assert.That(vm.CurrentScriptPath, Is.EqualTo(saveAsPath));
                Assert.That(services.Messages.Count(message => message.StartsWith("已保存脚本:", StringComparison.Ordinal)), Is.EqualTo(2));
            });
        }
        finally
        {
            vm.IsScriptModified = false;
            window?.Close();
            vm.OnMainWindowClosing();
        }
    }

    [Test]
    public async Task ModifiedEditorWithoutPath_CanSaveDirectInputAndAfterPathIsCleared()
    {
        string firstPath = Path.Combine(_tempRoot, "direct-input.ecs");
        string secondPath = Path.Combine(_tempRoot, "after-close.ecs");
        TestServices services = new() { SavePath = firstPath };
        MainWindowViewModel vm = CreateViewModel(services);
        try
        {
            Assert.That(vm.SaveScriptCommand.CanExecute(null), Is.False,
                "没有路径且未编辑的空白区不应启动保存");

            vm.EditorText = "typed before creating a document";
            Assert.Multiple(() =>
            {
                Assert.That(vm.IsScriptModified, Is.True);
                Assert.That(vm.SaveScriptCommand.CanExecute(null), Is.True);
                Assert.That(vm.SaveScriptAsCommand.CanExecute(null), Is.True);
            });

            Task firstSave = vm.SaveScriptCommand.ExecuteAsync(null);
            WaitUntil(() => firstSave.IsCompleted, TimeSpan.FromSeconds(5), "直接输入后的首次保存没有结束");
            await firstSave.ConfigureAwait(false);
            WaitUntil(() => vm.FileTreeVM.HasLoadedDirectory, TimeSpan.FromSeconds(5), "首次保存后没有加载目标目录");

            Assert.Multiple(() =>
            {
                Assert.That(File.ReadAllText(firstPath), Is.EqualTo("typed before creating a document"));
                Assert.That(vm.CurrentScriptPath, Is.EqualTo(firstPath));
                Assert.That(vm.GetCurrentProjectDirectory(), Is.EqualTo(_tempRoot));
                Assert.That(services.SaveDialogCalls, Is.EqualTo(1));
            });

            // Simulate the editor returning to a no-path state after its prior document closes.
            vm.CurrentScriptPath = string.Empty;
            vm.EditorText = "typed after clearing the previous document path";
            services.SavePath = secondPath;

            Assert.That(vm.SaveScriptCommand.CanExecute(null), Is.True,
                "关闭文档后直接输入的内容也应可保存");
            Task secondSave = vm.SaveScriptCommand.ExecuteAsync(null);
            WaitUntil(() => secondSave.IsCompleted, TimeSpan.FromSeconds(5), "关闭文档后输入内容的保存没有结束");
            await secondSave.ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(File.ReadAllText(secondPath), Is.EqualTo("typed after clearing the previous document path"));
                Assert.That(vm.CurrentScriptPath, Is.EqualTo(secondPath));
                Assert.That(vm.IsScriptModified, Is.False);
                Assert.That(services.SaveDialogCalls, Is.EqualTo(2));
            });
        }
        finally
        {
            vm.OnMainWindowClosing();
        }
    }

    [Test]
    public async Task SaveAsCancelAndWriteFailure_PreservePathDirtyStateAndProjectRoot()
    {
        string projectPath = Path.Combine(_tempRoot, "project");
        Directory.CreateDirectory(projectPath);
        string scriptPath = Path.Combine(projectPath, "main.ecs");
        File.WriteAllText(scriptPath, "original");
        string invalidTarget = Path.Combine(_tempRoot, "is-a-directory");
        Directory.CreateDirectory(invalidTarget);

        TestServices services = new();
        MainWindowViewModel vm = CreateViewModel(services);
        try
        {
            vm.OpenProjectFromDirectory(projectPath);
            WaitUntil(() => vm.FileTreeVM.HasLoadedDirectory, TimeSpan.FromSeconds(5), "项目树加载超时");
            vm.CurrentScriptPath = scriptPath;
            vm.EditorText = "unsaved edit";

            services.SavePath = null;
            await vm.SaveScriptAsCommand.ExecuteAsync(null).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(vm.CurrentScriptPath, Is.EqualTo(scriptPath));
                Assert.That(vm.IsScriptModified, Is.True);
                Assert.That(vm.GetCurrentProjectDirectory(), Is.EqualTo(projectPath));
                Assert.That(vm.FileTreeVM.RootItems.Single().FullPath, Is.EqualTo(projectPath));
            });

            services.SavePath = invalidTarget;
            await vm.SaveScriptAsCommand.ExecuteAsync(null).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(vm.CurrentScriptPath, Is.EqualTo(scriptPath));
                Assert.That(vm.IsScriptModified, Is.True);
                Assert.That(vm.GetCurrentProjectDirectory(), Is.EqualTo(projectPath));
                Assert.That(vm.FileTreeVM.RootItems.Single().FullPath, Is.EqualTo(projectPath));
                Assert.That(services.Messages.Any(message => message.StartsWith("保存脚本失败(", StringComparison.Ordinal)), Is.True);
                Assert.That(services.Messages.Any(message => message.StartsWith("已保存脚本:", StringComparison.Ordinal)), Is.False);
            });
        }
        finally
        {
            vm.OnMainWindowClosing();
        }
    }

    [Test]
    public async Task SaveAsOutsideProject_UpdatesDocumentPathAndKeepsOriginalProjectRoot()
    {
        string projectPath = Path.Combine(_tempRoot, "project");
        Directory.CreateDirectory(projectPath);
        string scriptPath = Path.Combine(projectPath, "main.ecs");
        File.WriteAllText(scriptPath, "original");
        string copyPath = Path.Combine(_tempRoot, "saved-copy.ecs");

        TestServices services = new() { SavePath = copyPath };
        MainWindowViewModel vm = CreateViewModel(services);
        try
        {
            vm.OpenProjectFromDirectory(projectPath);
            WaitUntil(() => vm.FileTreeVM.HasLoadedDirectory, TimeSpan.FromSeconds(5), "项目树加载超时");
            vm.CurrentScriptPath = scriptPath;
            vm.EditorText = "saved copy";

            await vm.SaveScriptAsCommand.ExecuteAsync(null).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(File.ReadAllText(copyPath), Is.EqualTo("saved copy"));
                Assert.That(vm.CurrentScriptPath, Is.EqualTo(copyPath));
                Assert.That(vm.IsScriptModified, Is.False);
                Assert.That(vm.GetCurrentProjectDirectory(), Is.EqualTo(projectPath));
                Assert.That(vm.FileTreeVM.RootItems.Single().FullPath, Is.EqualTo(projectPath));
            });
        }
        finally
        {
            vm.OnMainWindowClosing();
        }
    }

    [Test]
    public async Task FirstSaveOfUntitledDocument_UsesDialogAndLoadsParentAfterSuccess()
    {
        string savedPath = Path.Combine(_tempRoot, "first-save.ecs");
        TestServices services = new() { SavePath = savedPath };
        MainWindowViewModel vm = CreateViewModel(services);
        try
        {
            vm.FileTreeVM.NewScriptCommand.Execute(null);
            vm.EditorText = "first document";
            Task saveTask = vm.SaveScriptCommand.ExecuteAsync(null);
            WaitUntil(() => saveTask.IsCompleted, TimeSpan.FromSeconds(5), "首次保存没有完成");
            await saveTask.ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(File.ReadAllText(savedPath), Is.EqualTo("first document"));
                Assert.That(vm.CurrentScriptPath, Is.EqualTo(savedPath));
                Assert.That(vm.IsScriptModified, Is.False);
                Assert.That(services.SaveDialogCalls, Is.EqualTo(1));
                Assert.That(vm.GetCurrentProjectDirectory(), Is.EqualTo(_tempRoot));
                Assert.That(vm.FileTreeVM.RootItems.Single().FullPath, Is.EqualTo(_tempRoot));
            });
        }
        finally
        {
            vm.OnMainWindowClosing();
        }
    }

    [Test]
    public async Task SaveAndSaveAsShareOneBusyGateWhileDialogIsOpen()
    {
        string scriptPath = Path.Combine(_tempRoot, "busy.ecs");
        File.WriteAllText(scriptPath, "before");
        TestServices services = new() { SaveDialogCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        MainWindowViewModel vm = CreateViewModel(services);
        try
        {
            vm.CurrentScriptPath = scriptPath;
            vm.EditorText = "unsaved";
            Task saveAsTask = vm.SaveScriptAsCommand.ExecuteAsync(null);

            Assert.Multiple(() =>
            {
                Assert.That(vm.IsSavingScript, Is.True);
                Assert.That(vm.SaveScriptCommand.CanExecute(null), Is.False);
                Assert.That(vm.SaveScriptAsCommand.CanExecute(null), Is.False);
                Assert.That(services.SaveDialogCalls, Is.EqualTo(1));
            });

            services.SaveDialogCompletion!.SetResult(null);
            WaitUntil(() => saveAsTask.IsCompleted, TimeSpan.FromSeconds(5), "对话框关闭后保存流程没有结束");
            await saveAsTask.ConfigureAwait(false);
            Assert.That(vm.IsSavingScript, Is.False);
            Assert.That(vm.IsScriptModified, Is.True);
        }
        finally
        {
            vm.OnMainWindowClosing();
        }
    }

    [Test]
    public void SaveCommands_AreDisabledOutsideTheScriptEditorTab()
    {
        string scriptPath = Path.Combine(_tempRoot, "main.ecs");
        File.WriteAllText(scriptPath, "script");
        TestServices services = new();
        MainWindowViewModel vm = CreateViewModel(services);
        try
        {
            vm.CurrentScriptPath = scriptPath;
            vm.EditorText = "changed";
            vm.SelectedEditorTab = 1;

            Assert.Multiple(() =>
            {
                Assert.That(vm.SaveScriptCommand.CanExecute(null), Is.False);
                Assert.That(vm.SaveScriptAsCommand.CanExecute(null), Is.False);
            });
        }
        finally
        {
            vm.OnMainWindowClosing();
        }
    }

    private static MainWindowViewModel CreateViewModel(TestServices services, ConfigState? config = null)
    {
        return new MainWindowViewModel(
            services,
            services,
            services,
            services,
            services,
            services,
            services,
            SynchronousUiDispatcher.Instance,
            loadUserConfig: () => config ?? new ConfigState(),
            saveUserConfig: _ => { });
    }

    private static void AddStyleIfMissing(string uri)
    {
        if (Application.Current!.Styles.OfType<StyleInclude>().Any(include => include.Source?.AbsoluteUri == uri))
            return;

        Application.Current.Styles.Add(new StyleInclude(new Uri("avares://EasyCon2.Avalonia/"))
        {
            Source = new Uri(uri)
        });
    }

    private static void WaitUntil(Func<bool> condition, TimeSpan timeout, string message)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            if (condition())
                return;
            Thread.Sleep(10);
        }
        Dispatcher.UIThread.RunJobs();
        Assert.That(condition(), Is.True, message);
    }

    private sealed class TestServices : ILogService, IDeviceService, ICaptureService, IScriptService,
        IControllerService, IDialogService, IWindowService
    {
        private readonly List<string> _messages = [];
        public IReadOnlyList<string> Messages => _messages;
        public bool IsConnected => false;
        public bool ShowDebugInfo { get; set; }
        public string CaptureType { get; set; } = "ANY";
        public bool IsRunning => false;
        public bool HasKeyAction => false;
        public bool HighResolutionTiming { get; set; }
        public string? SavePath { get; set; }
        public string? FolderPath { get; set; }
        public int SaveDialogCalls { get; private set; }
        public TaskCompletionSource<string?>? SaveDialogCompletion { get; set; }
        public event Action<string?, string?>? LogAppended;
        public event Action? ConnectionLost;
        public event Action? ConnectionRestored;
        public event Action<bool>? IsRunningChanged;
        public event Action? AvailableSourcesChanged;
        public event Action? Disconnected;

        public void AddLog(string message, string? color = null)
        {
            _messages.Add(message);
            LogAppended?.Invoke(message, color);
        }

        public void Clear()
        {
            _messages.Clear();
            LogAppended?.Invoke(null, null);
        }

        public void Print(string message, bool newline = true) => AddLog(message);
        public void Alert(string message) => AddLog(message);
        public string ReadLine() => "";
        public bool TryReadLine(out string line) { line = ""; return false; }
        public string[] GetAvailablePorts() => [];
        public string[] GetAvailableSources() => [];
        IReadOnlyList<ControlSourceInfo> IControllerService.GetAvailableSources() => [];
        public bool TryConnect(string sourceName) => false;
        public string? AutoConnect() => null;
        public void Disconnect() { }
        public NintendoSwitch GetDevice() => null!;
        public void Reset() { }
        public bool RemoteStart() => false;
        public bool RemoteStop() => false;
        public bool Flash(byte[] asmBytes) => false;
        public int GetVersion() => -1;
        public bool UnPair() => false;
        public Task DisconnectAsync() => Task.CompletedTask;
        public FrameLease? AcquireLatestFrame() => null;
        public void SetCaptureProperties(int width, int height) { }
        public Task<bool> CompileAsync(string scriptText, string? fileName) => Task.FromResult(false);
        public string GetFormattedCode() => "";
        public Task<byte[]> BuildAsync(bool autoRun) => Task.FromResult(Array.Empty<byte>());
        public void Run(string scriptPath, string[]? args = null) { }
        public void RunFromContent(string content, string[]? args = null, string? fileName = null) { }
        public void Stop() { }
        public ScriptRequirements GetRequirements() => default!;
        public void SetOwnerWindow(Window owner) { }
        public void Dispose() { }
        public Task<IReadOnlyList<string>> OpenFilesAsync(string title, IReadOnlyList<FileDialogFilter>? filters = null, string? suggestedStartPath = null)
            => Task.FromResult<IReadOnlyList<string>>([]);
        public Task<string?> SaveFileAsync(string title, string defaultExtension, IReadOnlyList<FileDialogFilter>? filters = null, string? suggestedFileName = null)
        {
            SaveDialogCalls++;
            return SaveDialogCompletion?.Task ?? Task.FromResult(SavePath);
        }
        public Task<string?> OpenFolderAsync(string title, string? suggestedStartPath = null)
            => Task.FromResult(FolderPath);
        public Task<DialogColor?> PickColorAsync(DialogColor current) => Task.FromResult<DialogColor?>(null);
        public Task<string[]?> ShowScriptArgsDialogAsync() => Task.FromResult<string[]?>(null);
        public Task<bool> ConfirmAsync(string title, string message) => Task.FromResult(false);
        public void ShowESPConfigWindow() { }
        public void ShowAlertConfigWindow() { }
        public void ShowModelsConfigWindow() { }
        public void ShowMcpConfigWindow() { }
        public void ShowKeyMappingWindow() { }
        public void ShowScriptSyntaxWindow() { }
    }
}