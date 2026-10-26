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
        f.Vm.OnPicturePressed(411, 141, 822, 282);
        List<string> changed = [];
        f.Vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName!);

        L10nBridge.Resolver = Locale("en_US").GetValueOrDefault;
        f.Vm.RefreshLocalizedTexts();

        Assert.That(changed, Is.SupersetOf(new[] { "CurPosText", "EstimateText", "ProgressText" }));
        Assert.That(f.Vm.CurPosText, Is.EqualTo("Position: 160,60"));
        Assert.That(f.Vm.EstimateText, Does.StartWith("Time: ").And.EndWith(" min"));
    }

    [Test]
    public async Task Estimate_AppearsAutomaticallyOnceAPictureIsLoaded()
    {
        Fixture f = new(Canvas((0, 1)));
        Assert.That(f.Vm.EstimateText, Is.EqualTo("耗时："));

        await f.Vm.LoadPictureFileAsync("x.png");

        // 1 次点按 × (50 + 45) + 落笔前后两次移动各 2 次延迟 + 落笔、抬笔各 1 次延迟 = 95 + 6 × 45 = 365 ms
        Assert.That(f.Vm.EstimateText, Is.EqualTo("耗时：" + (365 / 60000.0).ToString("0.00") + "mins"));
        Assert.That(f.Dialogs.Messages, Is.Empty);
    }

    [Test]
    public async Task Estimate_FollowsPictureAndParameterChanges()
    {
        // 点放远一些，让估算长到"X.XXmins"能分辨：黑点在 (60,1)，灰点在 (100,300)
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
        f.Vm.WidthText = "1";         // 只画第 0 列：没有墨点
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

        Assert.That(f.Vm.EstimateText, Is.EqualTo("耗时：参数有误"));
        Assert.That(f.Dialogs.Messages, Is.Empty, "输入过程中不弹框");

        f.Vm.DelayText = "45";
        Assert.That(f.Vm.EstimateText, Does.EndWith("mins"));
    }

    [TestCase("abc", "0", "0", "320", "120", "延迟应为 0～10000 的整数")]
    [TestCase("45", "-1", "0", "320", "120", "起始列应为 0～319 的整数")]
    [TestCase("45", "0", "120", "320", "120", "起始行应为 0～119 的整数")]
    [TestCase("45", "0", "0", "0", "120", "列数应为 1～320 的整数")]
    [TestCase("45", "0", "0", "320", "", "行数应为 1～120 的整数")]
    public async Task InvalidSettings_AreRejectedWithFieldName(string delay, string startCol, string startRow, string width, string height, string expected)
    {
        Fixture f = new(Canvas((0, 1)));
        await f.Vm.LoadPictureFileAsync("x.png");
        f.Vm.DelayText = delay;
        f.Vm.StartPosXText = startCol;
        f.Vm.StartPosYText = startRow;
        f.Vm.WidthText = width;
        f.Vm.HeightText = height;

        await f.Vm.StartCommand.ExecuteAsync(null);

        Assert.That(f.Dialogs.Messages, Is.EqualTo(new[] { expected }));
        Assert.That(f.Vm.DrawTask, Is.Null);
        Assert.That(f.Vm.EstimateText, Is.EqualTo("耗时：参数有误"));
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
}