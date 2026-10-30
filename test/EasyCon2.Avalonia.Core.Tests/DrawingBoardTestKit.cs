using EasyCon.Core.Capabilities;
using EasyCon.Core.Services;
using EasyCon2.Avalonia.DrawingBoard;
using EasyCon2.Avalonia.Localization;
using EasyCon2.Avalonia.Models;
using EasyCon2.Avalonia.Services;
using EasyScript;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace EasyCon2.Avalonia.Core.Tests;

/// <summary>画板测试共用的图片构造、断言辅助与假对象（以 using static 引入）。</summary>
internal static class DrawingBoardTestKit
{
    internal const string LeaseRelease = "lease-release";

    /// <summary>开画前归位发出的一次输入：左摇杆以最大角度推向左上（推一下、推住各一次，见 <see cref="HomeKeys"/>）。</summary>
    internal const string Home = "stick LS 0,0";

    /// <summary>开画前的归位：左摇杆先向左上推一下，再推住把光标推到画布左上角。</summary>
    internal static readonly string[] HomeKeys = [Home, Home];

    /// <summary>归位的计划耗时：摇杆推一下 + 推住，各跟一次延迟。</summary>
    internal static int HomeMs(int wait) => SplatoonDrawing.HomeTapMs + SplatoonDrawing.HomeHoldMs + 2 * wait;

    internal static readonly DrawingBoardSettings FullCanvas = new(0, 0, 0, 0, 320, 120);

    internal static Dictionary<string, string> Locale(string name)
    {
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        XDocument doc = XDocument.Load(Path.Combine(RepoRoot(), "src", "EasyCon2.Avalonia", "Resources", "Locales", name + ".axaml"));
        return doc.Root!.Elements(x + "String").ToDictionary(e => (string)e.Attribute(x + "Key")!, e => e.Value);
    }

    internal static string RepoRoot()
    {
        DirectoryInfo? dir = new(TestContext.CurrentContext.TestDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "EasyCon2.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("找不到仓库根目录（EasyCon2.slnx）");
    }

    /// <summary>从"预计耗时：X.XX 分钟"中取出分钟数。</summary>
    internal static double Minutes(string estimateText)
        => double.Parse(Regex.Match(estimateText, @"[\d.]+").Value, System.Globalization.CultureInfo.CurrentCulture);

    internal static PixelImage Filled(byte gray)
    {
        byte[] rgba = White(320, 120);
        for (int row = 0; row < 120; row++)
            for (int col = 0; col < 320; col++)
                SetPixel(rgba, row, col, gray, gray, gray, 255);
        return new PixelImage(320, 120, rgba);
    }

    internal static IEnumerable<bool> Cells(InkMask mask)
        => Enumerable.Range(0, mask.Height).SelectMany(row => Enumerable.Range(0, mask.Width).Select(col => mask[col, row]));

    internal static double InkFraction(InkMask mask, int fromCol = 0, int toCol = 320)
    {
        int ink = 0;
        for (int row = 0; row < mask.Height; row++)
            for (int col = fromCol; col < toCol; col++)
                if (mask[col, row])
                    ink++;
        return (double)ink / (mask.Height * (toCol - fromCol));
    }

    internal static List<List<DrawingLine>> Scan(PixelImage image, DrawingBoardSettings settings, bool reverse = false)
        => SplatoonDrawing.SearchLines(InkMask.Binarize(image, new BinarizeOptions(50, reverse)), settings);

    internal static string Describe(DrawingLine line) => $"{line.Row}:{line.StartCol}-{line.EndCol}";

    internal static IEnumerable<string> Clicks(GamePadKey key, int count)
        => Enumerable.Repeat("click " + key, count);

    internal static byte[] White(int width, int height) => Enumerable.Repeat((byte)255, width * height * 4).ToArray();

    internal static void SetPixel(byte[] rgba, int row, int col, byte r, byte g, byte b, byte a)
    {
        int i = (row * 320 + col) * 4;
        rgba[i] = r;
        rgba[i + 1] = g;
        rgba[i + 2] = b;
        rgba[i + 3] = a;
    }

    /// <summary>
    /// 同步执行投递的操作，并标记"正在 UI 线程上"。配合 <see cref="UiOnlyResolver"/>，
    /// 可以抓出在绘制线程上直接取本地化文本的代码（真实程序里 L10n 只能在 UI 线程上调用，否则抛异常）。
    /// </summary>
    internal sealed class UiMarkingDispatcher : IUiDispatcher
    {
        [ThreadStatic]
        private static bool _inPost;

        public static bool InPost => _inPost;

        public bool IsOnUiThread => _inPost;

        public void Post(Action action)
        {
            bool outer = _inPost;
            _inPost = true;
            try
            {
                action();
            }
            finally
            {
                _inPost = outer;
            }
        }

        public Task InvokeAsync(Action action)
        {
            Post(action);
            return Task.CompletedTask;
        }

        public Task InvokeAsync(Func<Task> action)
        {
            Task task = Task.CompletedTask;
            Post(() => task = action());
            return task;
        }
    }

    /// <summary>线程池线程（绘制线程）上、且不在投递的 UI 操作内取本地化文本时抛异常，与真实 L10n 一致。</summary>
    internal static Func<string, string?> UiOnlyResolver(Func<string, string?> inner) => key =>
    {
        if (Thread.CurrentThread.IsThreadPoolThread && !UiMarkingDispatcher.InPost)
            throw new InvalidOperationException("本地化文本只能在 UI 线程上取：" + key);
        return inner(key);
    };

    /// <summary>320×120 白底，给定 (行, 列) 处为黑点。</summary>
    internal static PixelImage Canvas(params (int Row, int Col)[] dark)
    {
        byte[] rgba = White(320, 120);
        foreach ((int row, int col) in dark)
        {
            int i = (row * 320 + col) * 4;
            rgba[i] = rgba[i + 1] = rgba[i + 2] = 0;
        }
        return new PixelImage(320, 120, rgba);
    }

    internal sealed class Fixture
    {
        public Fixture(PixelImage image, IUiDispatcher? ui = null)
        {
            Runner = new FakeRunner(Pad);
            Images = new FakeImages(image);
            Vm = new DrawingBoardViewModel(Runner, Dialogs, Images, ui ?? SynchronousUiDispatcher.Instance, Log);
        }

        public RecordingLog Log { get; } = new();

        public FakeImages Images { get; }
        public RecordingPad Pad { get; } = new();
        public FakeRunner Runner { get; }
        public FakeDialogs Dialogs { get; } = new();
        public DrawingBoardViewModel Vm { get; }
    }

    /// <summary>模拟运行租约：回调结束（含取消与异常）后记录一次中性释放。</summary>
    internal sealed class FakeRunner(RecordingPad pad) : IDrawingRunner
    {
        public bool Connected { get; set; } = true;
        public Exception? Failure { get; set; }
        public bool IsConnected => Connected;

        /// <summary>每次运行收到的保持唤醒选项。</summary>
        public List<bool> KeepAwakeRequests { get; } = [];

        public void Run(Action<IPadInput, CancellationToken> draw, bool keepAwake, CancellationToken token)
        {
            KeepAwakeRequests.Add(keepAwake);
            if (Failure != null)
                throw Failure;
            try
            {
                draw(pad, token);
            }
            finally
            {
                pad.Add(LeaseRelease);
            }
        }
    }

    internal sealed class RecordingPad : IPadInput
    {
        private readonly List<string> _events = [];
        private long _clickDurations;

        /// <summary>点按时真实等待按键持续（可被取消），模拟设备侧 pulse。</summary>
        public bool WaitForDuration { get; set; }
        public Action? OnKey { get; set; }
        public long ClickDurations => Interlocked.Read(ref _clickDurations);

        public List<string> Events
        {
            get
            {
                lock (_events)
                    return [.. _events];
            }
        }

        public void ClickButtons(GamePadKey key, int duration, CancellationToken token)
        {
            Add("click " + key);
            Interlocked.Add(ref _clickDurations, duration);
            OnKey?.Invoke();
            if (WaitForDuration && token.WaitHandle.WaitOne(duration))
                throw new OperationCanceledException(token);
        }

        public void PressButtons(GamePadKey key)
        {
            Add("press " + key);
            OnKey?.Invoke();
        }

        public void ReleaseButtons(GamePadKey key) => Add("release " + key);
        public void ClickStick(GamePadKey key, byte x, byte y, int duration, CancellationToken token)
        {
            Add($"stick {key} {x},{y}");
            Interlocked.Add(ref _clickDurations, duration);
            OnKey?.Invoke();
            if (WaitForDuration && token.WaitHandle.WaitOne(duration))
                throw new OperationCanceledException(token);
        }
        public void SetStick(GamePadKey key, byte x, byte y) => Add("stick " + key);
        public void ChangeAmiibo(uint index) { }

        public void Add(string e)
        {
            lock (_events)
                _events.Add(e);
        }
    }

    internal sealed class FakeImages(PixelImage image) : IImageProcessor
    {
        public int Decodes { get; private set; }
        public byte[]? Crop(byte[] sourceBytes, int x, int y, int width, int height) => null;

        public byte[]? LoadAsPng(string filePath)
        {
            Decodes++;
            return [1];
        }

        public PixelImage? LoadPixels(string filePath)
        {
            Decodes++;
            return image;
        }

        public List<ImageFit> Fits { get; } = [];

        /// <summary>记录适配方式；尺寸已一致时原样返回（与真实实现相同），否则给一张同尺寸白图。</summary>
        public List<(int Width, int Height)> FittedSizes { get; } = [];
        public List<CoverTransform?> Covers { get; } = [];
        public List<bool> Nearest { get; } = [];

        public PixelImage FitTo(PixelImage source, int width, int height, ImageFit fit, CoverTransform? cover = null, bool nearest = false)
        {
            Nearest.Add(nearest);
            Fits.Add(fit);
            Covers.Add(cover);
            FittedSizes.Add((source.Width, source.Height));
            return source.Width == width && source.Height == height ? source : new PixelImage(width, height, White(width, height));
        }

        public List<(PixelImage Image, string Path)> Saved { get; } = [];
        public bool SaveSucceeds { get; set; } = true;

        public bool SavePng(PixelImage image, string filePath)
        {
            Saved.Add((image, filePath));
            return SaveSucceeds;
        }
    }

    internal sealed class FakeDialogs : IDialogService
    {
        public List<string> Messages { get; } = [];
        public IReadOnlyList<FileDialogFilter>? OpenFilters { get; private set; }

        public Task<IReadOnlyList<string>> OpenFilesAsync(string title, IReadOnlyList<FileDialogFilter>? filters = null, string? suggestedStartPath = null)
        {
            OpenFilters = filters;
            return Task.FromResult<IReadOnlyList<string>>([]);
        }
        /// <summary>保存对话框返回的路径；null 表示用户取消。</summary>
        public string? SavePath { get; set; }
        public string? SuggestedFileName { get; private set; }

        public Task<string?> SaveFileAsync(string title, string defaultExtension, IReadOnlyList<FileDialogFilter>? filters = null, string? suggestedFileName = null)
        {
            SuggestedFileName = suggestedFileName;
            return Task.FromResult(SavePath);
        }
        public Task<string?> OpenFolderAsync(string title, string? suggestedStartPath = null) => Task.FromResult<string?>(null);
        public Task<DialogColor?> PickColorAsync(DialogColor current) => Task.FromResult<DialogColor?>(null);
        public Task<string[]?> ShowScriptArgsDialogAsync() => Task.FromResult<string[]?>(null);
        public Task<bool> ConfirmAsync(string title, string message) => Task.FromResult(false);
        public Task ShowMessageAsync(string title, string message)
        {
            Messages.Add(message);
            return Task.CompletedTask;
        }
    }

    internal sealed class RecordingLog : ILogService
    {
        private readonly List<string> _messages = [];

        public List<string> Messages
        {
            get
            {
                lock (_messages)
                    return [.. _messages];
            }
        }

        public event Action<string?, string?>? LogAppended
        {
            add { }
            remove { }
        }
        public void AddLog(string message, string? color = null)
        {
            lock (_messages)
                _messages.Add(message);
        }
        public void Clear() { }
        public void Print(string message, bool newline = true) { }
        public void Alert(string message) { }
        public string ReadLine() => "";
        public bool TryReadLine(out string line)
        {
            line = "";
            return false;
        }
    }
}

/// <summary>画板 ViewModel 测试基类：文案断言按真实的 zh_CN 资源进行（L10nBridge 未接宿主时只回显键名）。</summary>
public abstract class DrawingBoardTestBase
{
    [SetUp]
    public void UseChineseLocale() => L10nBridge.Resolver = DrawingBoardTestKit.Locale("zh_CN").GetValueOrDefault;

    /// <summary>学到的额外耗时是进程级静态值，各用例从 0 开始，避免前一个用例的真实计时影响估算断言。</summary>
    [SetUp]
    public void ForgetLearnedOverhead() => DrawingBoardViewModel.LearnedOverhead = default;

    [TearDown]
    public void ResetLocale() => L10nBridge.Resolver = null;
}