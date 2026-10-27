using EasyCon2.Avalonia.DrawingBoard;
using EasyCon2.Avalonia.Localization;
using EasyCon2.Avalonia.Models;
using System.Text.RegularExpressions;
using static EasyCon2.Avalonia.Core.Tests.DrawingBoardTestKit;

namespace EasyCon2.Avalonia.Core.Tests;

/// <summary>画板 ViewModel 的参数部分：输入校验、自动估算与本地化文本。</summary>
[TestFixture]
[NonParallelizable]
public class DrawingBoardSettingsTests : DrawingBoardTestBase
{
    [Test]
    public void LocaleKeys_UsedByDrawingBoard_ExistInBothLanguages()
    {
        string drawingBoard = Path.Combine(RepoRoot(), "src", "EasyCon2.Avalonia", "DrawingBoard");
        string sources = string.Concat(Directory.GetFiles(drawingBoard).Select(File.ReadAllText))
            + File.ReadAllText(Path.Combine(RepoRoot(), "src", "EasyCon2.Avalonia", "Shell", "FeatureCenterPage.axaml"));
        string[] used = Regex.Matches(sources, @"Text\.(?:DrawingBoard\.\w+|Feature\.(?:Splatoon|FreeDraw))")
            .Select(m => m.Value).Distinct().ToArray();
        Dictionary<string, string> zh = Locale("zh_CN");
        Dictionary<string, string> en = Locale("en_US");

        Assert.That(used, Has.Length.GreaterThan(20));
        Assert.That(used.Where(key => !zh.ContainsKey(key)), Is.Empty, "zh_CN 缺少键");
        Assert.That(used.Where(key => !en.ContainsKey(key)), Is.Empty, "en_US 缺少键");
        Assert.That(en.Keys.Where(k => k.StartsWith("Text.DrawingBoard.")).Order(),
            Is.EqualTo(zh.Keys.Where(k => k.StartsWith("Text.DrawingBoard.")).Order()));
    }

    [Test]
    public async Task StatusTexts_FollowLanguageSwitch()
    {
        Fixture f = new(Canvas((0, 1)));
        await f.Vm.LoadPictureFileAsync("x.png");
        List<string> changed = [];
        f.Vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName!);

        L10nBridge.Resolver = Locale("en_US").GetValueOrDefault;
        f.Vm.RefreshLocalizedTexts();

        Assert.That(changed, Is.SupersetOf(new[] { "RegionText", "EstimateText", "ProgressText" }));
        Assert.That(f.Vm.RegionText, Is.EqualTo("Drawing the whole canvas 320×120"));
        Assert.That(f.Vm.EstimateText, Does.StartWith("Estimated time: ").And.EndWith(" min"));
    }

    [Test]
    public async Task Estimate_AppearsAutomaticallyOnceAPictureIsLoaded()
    {
        Fixture f = new(Canvas((0, 1)));
        Assert.That(f.Vm.EstimateText, Is.EqualTo("预计耗时：—"));

        await f.Vm.LoadPictureFileAsync("x.png");

        // 1 次点按 × (50 + 45) + 落笔前后两次移动各 2 次延迟 + 落笔、抬笔各 1 次延迟 = 95 + 6 × 45 = 365 ms
        Assert.That(f.Vm.EstimateText, Is.EqualTo("预计耗时：" + (365 / 60000.0).ToString("0.00") + " 分钟"));
        Assert.That(f.Dialogs.Messages, Is.Empty);
    }

    [Test]
    public async Task Estimate_FollowsPictureAndParameterChanges()
    {
        // 点放远一些，让估算长到"X.XX 分钟"能分辨：黑点在 (60,1)，灰点在 (100,300)
        byte[] rgba = White(320, 120);
        SetPixel(rgba, 60, 1, 0, 0, 0, 255);
        SetPixel(rgba, 100, 300, 100, 100, 100, 255);
        Fixture f = new(new PixelImage(320, 120, rgba));
        await f.Vm.LoadPictureFileAsync("x.png");
        List<string> seen = [f.Vm.EstimateText];
        f.Vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(DrawingBoardViewModel.EstimateText))
                seen.Add(f.Vm.EstimateText);
        };

        f.Vm.ThresholdPercent = 30;   // 第 300 列的灰点不再画：少走一大段
        string afterThreshold = f.Vm.EstimateText;
        f.Vm.DelayText = "100";       // 等待变长
        string afterDelay = f.Vm.EstimateText;
        f.Vm.SelectRegion = true;
        f.Vm.SetRegion(0, 0, 1, 120); // 只画第 0 列：没有墨点
        string afterRegion = f.Vm.EstimateText;

        Assert.That(Minutes(afterThreshold), Is.LessThan(Minutes(seen[0])));
        Assert.That(Minutes(afterDelay), Is.GreaterThan(Minutes(afterThreshold)));
        Assert.That(Minutes(afterRegion), Is.Zero);
        Assert.That(seen, Has.Count.GreaterThanOrEqualTo(4), "每次变化都通知界面");
    }

    [Test]
    public async Task Estimate_InvalidParameter_ShowsHintWithoutDialog()
    {
        Fixture f = new(Canvas((0, 1)));
        await f.Vm.LoadPictureFileAsync("x.png");

        f.Vm.DelayText = "";

        Assert.That(f.Vm.EstimateText, Is.EqualTo("预计耗时：参数有误"));
        Assert.That(f.Dialogs.Messages, Is.Empty, "输入过程中不弹框");

        f.Vm.DelayText = "45";
        Assert.That(f.Vm.EstimateText, Does.EndWith("分钟"));
    }

    [TestCase("abc", "50", "延迟应为 0～10000 的整数")]
    [TestCase("-1", "50", "延迟应为 0～10000 的整数")]
    [TestCase("45", "", "按键持续应为 1～10000 的整数")]
    [TestCase("45", "10001", "按键持续应为 1～10000 的整数")]
    public async Task InvalidSettings_AreRejectedWithFieldName(string delay, string duration, string expected)
    {
        Fixture f = new(Canvas((0, 1)));
        await f.Vm.LoadPictureFileAsync("x.png");
        f.Vm.DelayText = delay;
        f.Vm.DurationText = duration;

        await f.Vm.StartCommand.ExecuteAsync(null);

        Assert.That(f.Dialogs.Messages, Is.EqualTo(new[] { expected }));
        Assert.That(f.Vm.DrawTask, Is.Null);
        Assert.That(f.Vm.EstimateText, Is.EqualTo("预计耗时：参数有误"));
    }

    [Test]
    public async Task DurationZero_IsRejected()
    {
        Fixture f = new(Canvas((0, 1)));
        await f.Vm.LoadPictureFileAsync("x.png");
        f.Vm.DurationText = "0";

        await f.Vm.StartCommand.ExecuteAsync(null);

        Assert.That(f.Dialogs.Messages, Is.EqualTo(new[] { "按键持续应为 1～10000 的整数" }));
    }

    [Test]
    public async Task Region_DefaultsToTheWholeCanvas()
    {
        Fixture f = new(Canvas((0, 1), (100, 300)));
        await f.Vm.LoadPictureFileAsync("x.png");

        Assert.That(f.Vm.SelectRegion, Is.False);
        Assert.That(f.Vm.RegionText, Is.EqualTo("绘制整张画布 320×120"));
        Assert.That(f.Vm.DisplayImage, Is.SameAs(f.Vm.Preview), "不框选时不叠加区域框");
    }

    [Test]
    public async Task Region_DragOnThePreviewSelectsAndFramesIt()
    {
        Fixture f = new(Canvas((0, 1), (100, 300)));
        await f.Vm.LoadPictureFileAsync("x.png");
        double full = Minutes(f.Vm.EstimateText);
        f.Vm.SelectRegion = true;

        // 预览 822×282：按下 (0,0)，拖到画布 (列 20, 行 4) 附近，再往回拖到 (列 10, 行 2)
        f.Vm.OnPicturePressed(1, 1, 822, 282);
        f.Vm.OnPictureDragged(20 * 822 / 320.0 + 1, 4 * 282 / 120.0 + 1, 822, 282);
        f.Vm.OnPictureDragged(10 * 822 / 320.0 + 1, 2 * 282 / 120.0 + 1, 822, 282);
        f.Vm.OnPictureReleased();
        f.Vm.OnPictureDragged(300, 100, 822, 282);

        Assert.That(f.Vm.RegionText, Is.EqualTo("绘制区域：第 0–10 列，第 0–2 行"), "松开后再移动不改区域");
        Assert.That(f.Vm.DisplayImage, Is.Not.SameAs(f.Vm.Preview), "叠加了区域框");
        Assert.That(f.Vm.Preview!.GetPixel(10, 0).ToArgb(), Is.EqualTo(System.Drawing.Color.White.ToArgb()), "预览本身不带框");
        Assert.That(Minutes(f.Vm.EstimateText), Is.LessThan(full), "第 300 列的点不在区域内");

        f.Vm.ResetRegionCommand.Execute(null);
        Assert.That(f.Vm.RegionText, Is.EqualTo("绘制区域：第 0–319 列，第 0–119 行"));
        f.Vm.SetRegion(5, 5, 1, 1);
        f.Vm.SelectRegion = false;
        Assert.That(Minutes(f.Vm.EstimateText), Is.EqualTo(full), "关闭框选即恢复整张画布");
    }

    [Test]
    public async Task Region_CannotChangeWhileDrawing()
    {
        Fixture f = new(Canvas((0, 100)));
        await f.Vm.LoadPictureFileAsync("x.png");
        f.Vm.SelectRegion = true;
        f.Vm.DelayText = "0";
        f.Vm.DurationText = "10000";
        TaskCompletionSource firstKey = new(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Pad.WaitForDuration = true;
        f.Pad.OnKey = () => firstKey.TrySetResult();
        string before = f.Vm.RegionText;

        await f.Vm.StartCommand.ExecuteAsync(null);
        await firstKey.Task.WaitAsync(TimeSpan.FromSeconds(5));
        f.Vm.OnPicturePressed(100, 100, 822, 282);
        f.Vm.SetRegion(1, 1, 2, 2);

        Assert.That(f.Vm.RegionText, Is.EqualTo(before));
        Assert.That(f.Vm.ResetAllCommand.CanExecute(null), Is.False, "绘制中不能全部重置");
        await f.Vm.StopCommand.ExecuteAsync(null).WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Test]
    public async Task ResetAll_RestoresEveryOptionAndKeepsThePicture()
    {
        Fixture f = new(Canvas((0, 1), (100, 300)));
        await f.Vm.LoadPictureFileAsync("x.png");
        string defaultEstimate = f.Vm.EstimateText;
        PixelImage defaultPreview = f.Vm.Preview!;
        f.Vm.FitModeIndex = 1;
        f.Vm.CoverAngle = 30;
        f.Vm.CoverZoomPercent = 200;
        f.Vm.CoverPanX = 50;
        f.Vm.CoverPanY = -50;
        f.Vm.RotationIndex = 2;
        f.Vm.FlipHorizontal = true;
        f.Vm.FlipVertical = true;
        f.Vm.PixelArt = true;
        f.Vm.ToneBrightness = 20;
        f.Vm.ToneContrast = -20;
        f.Vm.ToneGamma = 2;
        f.Vm.ThresholdPercent = 80;
        f.Vm.DitherModeIndex = 3;
        f.Vm.SpeckSize = 5;
        f.Vm.OutlineMode = true;
        f.Vm.Reverse = true;
        f.Vm.ShowOriginal = true;
        f.Vm.DurationText = "80";
        f.Vm.DelayText = "60";
        f.Vm.EraserMode = true;
        f.Vm.SelectRegion = true;
        f.Vm.SetRegion(10, 10, 20, 20);
        int fits = f.Images.Fits.Count;

        f.Vm.ResetAllCommand.Execute(null);

        Assert.Multiple(() =>
        {
            Assert.That((f.Vm.FitModeIndex, f.Vm.CoverAngle, f.Vm.CoverZoomPercent, f.Vm.CoverPanX, f.Vm.CoverPanY),
                Is.EqualTo((0, 0.0, 100.0, 0.0, 0.0)));
            Assert.That((f.Vm.RotationIndex, f.Vm.FlipHorizontal, f.Vm.FlipVertical, f.Vm.PixelArt), Is.EqualTo((0, false, false, false)));
            Assert.That((f.Vm.ToneBrightness, f.Vm.ToneContrast, f.Vm.ToneGamma), Is.EqualTo((0, 0, 1.0)));
            Assert.That((f.Vm.ThresholdPercent, f.Vm.DitherModeIndex, f.Vm.SpeckSize, f.Vm.OutlineMode, f.Vm.Reverse, f.Vm.ShowOriginal),
                Is.EqualTo((50, 0, 0, false, false, false)));
            Assert.That((f.Vm.DurationText, f.Vm.DelayText, f.Vm.EraserMode, f.Vm.SelectRegion), Is.EqualTo(("50", "45", false, false)));
            Assert.That(f.Vm.RegionText, Is.EqualTo("绘制整张画布 320×120"));
            Assert.That(f.Vm.EstimateText, Is.EqualTo(defaultEstimate));
            Assert.That(f.Vm.Preview!.Rgba.ToArray(), Is.EqualTo(defaultPreview.Rgba.ToArray()), "图片仍在，按默认参数重新处理");
            Assert.That(f.Images.Fits, Has.Count.EqualTo(fits + 1), "各项改完后只重新适配一次");
        });

        f.Vm.SelectRegion = true;
        Assert.That(f.Vm.RegionText, Is.EqualTo("绘制区域：第 0–319 列，第 0–119 行"), "框选区域也恢复为整张画布");
    }
}