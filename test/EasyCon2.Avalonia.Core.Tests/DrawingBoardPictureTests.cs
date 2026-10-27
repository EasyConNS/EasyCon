using EasyCon2.Avalonia.DrawingBoard;
using EasyCon2.Avalonia.Models;
using static EasyCon2.Avalonia.Core.Tests.DrawingBoardTestKit;

namespace EasyCon2.Avalonia.Core.Tests;

/// <summary>画板 ViewModel 的图片部分：载入、拖放、保存、适配与取景、方向、阈值与抖动、预览坐标。</summary>
[TestFixture]
[NonParallelizable]
public class DrawingBoardPictureTests : DrawingBoardTestBase
{
    [Test]
    public async Task DitherOption_UpdatesMaskImmediately()
    {
        Fixture f = new(Filled(128));
        await f.Vm.LoadPictureFileAsync("x.png");
        Assert.That(InkFraction(f.Vm.Mask!), Is.Zero);

        f.Vm.DitherModeIndex = (int)DitherMode.Atkinson;
        Assert.That(InkFraction(f.Vm.Mask!), Is.InRange(0.4, 0.6));

        f.Vm.DitherModeIndex = 99;
        Assert.That(InkFraction(f.Vm.Mask!), Is.Zero, "越界的下标按不抖动处理");
    }

    [Test]
    public async Task RotateAndFlip_MoveWhatGetsDrawn()
    {
        Fixture f = new(Canvas((0, 0)));
        await f.Vm.LoadPictureFileAsync("x.png");
        Assert.That(f.Vm.Mask![0, 0], Is.True);

        f.Vm.FlipHorizontal = true;
        Assert.That(f.Vm.Mask![319, 0], Is.True);
        Assert.That(f.Vm.Mask![0, 0], Is.False);

        f.Vm.FlipVertical = true;
        Assert.That(f.Vm.Mask![319, 119], Is.True);

        f.Vm.FlipHorizontal = false;
        f.Vm.FlipVertical = false;
        f.Vm.RotationIndex = 2;
        Assert.That(f.Vm.Mask![319, 119], Is.True, "旋转 180° 与同时水平、垂直翻转等效");

        Assert.That(f.Images.Decodes, Is.EqualTo(1), "旋转/翻转不重新读文件");
        Assert.That(f.Vm.SourceSizeText, Is.EqualTo("原图 320×120"), "显示的是原图尺寸");
    }

    [Test]
    public async Task Rotate90_FitsThePortraitPicture()
    {
        Fixture f = new(new PixelImage(640, 480, White(640, 480)));
        await f.Vm.LoadPictureFileAsync("x.png");

        f.Vm.RotationIndex = 1;

        Assert.That(f.Images.FittedSizes, Is.EqualTo(new[] { (640, 480), (480, 640) }));
    }

    [Test]
    public async Task SavePicture_IsAvailableOnlyWithAPicture()
    {
        Fixture f = new(Canvas((0, 1)));
        Assert.That(f.Vm.SavePictureCommand.CanExecute(null), Is.False);

        await f.Vm.LoadPictureFileAsync("/tmp/cat.png");

        Assert.That(f.Vm.SavePictureCommand.CanExecute(null), Is.True);
    }

    [Test]
    public async Task SavePicture_SavesWhatThePreviewShows()
    {
        PixelImage image = Canvas((0, 1));
        Fixture f = new(image);
        await f.Vm.LoadPictureFileAsync("/tmp/cat.png");
        f.Dialogs.SavePath = "/tmp/out.png";

        await f.Vm.SavePictureCommand.ExecuteAsync(null);
        f.Vm.ShowOriginal = true;
        await f.Vm.SavePictureCommand.ExecuteAsync(null);

        Assert.That(f.Dialogs.SuggestedFileName, Is.EqualTo("cat-320x120.png"));
        Assert.That(f.Images.Saved, Has.Count.EqualTo(2));
        Assert.That(f.Images.Saved[0].Path, Is.EqualTo("/tmp/out.png"));
        Assert.That(f.Images.Saved[0].Image.GetPixel(1, 0).R, Is.EqualTo(0), "默认保存二值化结果：墨点为黑");
        Assert.That(f.Images.Saved[0].Image, Is.Not.SameAs(image));
        Assert.That(f.Images.Saved[1].Image, Is.SameAs(image), "勾选显示原图时保存原图");
        Assert.That(f.Dialogs.Messages, Is.Empty);
    }

    [Test]
    public async Task SavePicture_CancelledDialog_SavesNothing()
    {
        Fixture f = new(Canvas((0, 1)));
        await f.Vm.LoadPictureFileAsync("x.png");

        await f.Vm.SavePictureCommand.ExecuteAsync(null);

        Assert.That(f.Images.Saved, Is.Empty);
    }

    [Test]
    public async Task SavePicture_Failure_IsReported()
    {
        Fixture f = new(Canvas((0, 1)));
        await f.Vm.LoadPictureFileAsync("x.png");
        f.Dialogs.SavePath = "/readonly/out.png";
        f.Images.SaveSucceeds = false;

        await f.Vm.SavePictureCommand.ExecuteAsync(null);

        Assert.That(f.Dialogs.Messages, Is.EqualTo(new[] { "图片保存失败" }));
    }

    [Test]
    public async Task CoverControls_OnlyApplyInCoverMode()
    {
        Fixture f = new(new PixelImage(640, 480, White(640, 480)));
        await f.Vm.LoadPictureFileAsync("x.png");
        List<string> changed = [];
        f.Vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName!);
        Assert.That(f.Vm.IsCoverMode, Is.False);

        f.Vm.CoverAngle = 30;
        Assert.That(f.Images.Fits, Has.Count.EqualTo(1), "非等比裁切时调取景不重算");

        f.Vm.FitModeIndex = 1;
        Assert.That(f.Vm.IsCoverMode, Is.True);
        Assert.That(changed, Does.Contain(nameof(DrawingBoardViewModel.IsCoverMode)));
        f.Vm.CoverZoomPercent = 150;
        f.Vm.CoverPanX = -100;
        f.Vm.CoverPanY = 40;

        Assert.That(f.Images.Covers.Last(), Is.EqualTo(new CoverTransform(30, 1.5, -1, 0.4)));
    }

    [Test]
    public async Task ResetCover_RestoresDefaultsWithASingleRefit()
    {
        Fixture f = new(new PixelImage(640, 480, White(640, 480)));
        await f.Vm.LoadPictureFileAsync("x.png");
        f.Vm.FitModeIndex = 1;
        f.Vm.CoverAngle = 30;
        f.Vm.CoverZoomPercent = 200;
        f.Vm.CoverPanX = 50;
        f.Vm.CoverPanY = -50;
        int before = f.Images.Fits.Count;

        f.Vm.ResetCoverCommand.Execute(null);

        Assert.That(f.Images.Fits.Count - before, Is.EqualTo(1));
        Assert.That(f.Images.Covers.Last(), Is.EqualTo(CoverTransform.Identity));
        Assert.That((f.Vm.CoverAngle, f.Vm.CoverZoomPercent, f.Vm.CoverPanX, f.Vm.CoverPanY), Is.EqualTo((0.0, 100.0, 0.0, 0.0)));
    }

    [Test]
    public async Task DroppedFile_IsLoaded()
    {
        Fixture f = new(Canvas((0, 1)));

        await f.Vm.DropPictureCommand.ExecuteAsync("/tmp/dropped.webp");

        Assert.That(f.Images.Decodes, Is.EqualTo(1));
        Assert.That(f.Vm.Mask, Is.Not.Null);
    }

    [Test]
    public async Task OpenDialog_ListsEverySupportedFormat()
    {
        Fixture f = new(Canvas());

        await f.Vm.LoadPictureCommand.ExecuteAsync(null);

        Assert.That(f.Dialogs.OpenFilters![0].Patterns,
            Is.EquivalentTo(new[] { "*.png", "*.jpg", "*.jpeg", "*.bmp", "*.gif", "*.webp", "*.ico", "*.wbmp" }));
    }

    [Test]
    public async Task Preview_FollowsThresholdReverseAndShowOriginal()
    {
        byte[] rgba = White(320, 120);
        SetPixel(rgba, 0, 0, 100, 100, 100, 255);
        PixelImage image = new(320, 120, rgba);
        Fixture f = new(image);
        await f.Vm.LoadPictureFileAsync("x.png");
        Assert.That(f.Vm.Preview!.GetPixel(0, 0).R, Is.EqualTo(0), "默认阈值下灰度 100 是墨点");

        f.Vm.ThresholdPercent = 30;
        Assert.That(f.Vm.Mask![0, 0], Is.False);
        Assert.That(f.Vm.Preview!.GetPixel(0, 0).R, Is.EqualTo(255));

        f.Vm.Reverse = true;
        Assert.That(f.Vm.Mask![0, 0], Is.True);

        f.Vm.ShowOriginal = true;
        Assert.That(f.Vm.Preview, Is.SameAs(image));
        f.Vm.ShowOriginal = false;
        Assert.That(f.Vm.Preview, Is.Not.SameAs(image));
    }

    [Test]
    public async Task Threshold_ChangesWhatGetsDrawn()
    {
        byte[] rgba = White(320, 120);
        SetPixel(rgba, 0, 3, 100, 100, 100, 255);
        Fixture f = new(new PixelImage(320, 120, rgba));
        await f.Vm.LoadPictureFileAsync("x.png");
        f.Vm.DelayText = "0";
        f.Vm.DurationText = "1";
        f.Vm.ThresholdPercent = 30;

        await f.Vm.StartCommand.ExecuteAsync(null);
        await f.Vm.DrawTask!;

        Assert.That(f.Pad.Events, Does.Not.Contain("press A"), "阈值 30% 时灰度 100 不画");
    }

    [Test]
    public async Task Load_DecodesOnceAndPreviewsTheSamePixels()
    {
        PixelImage image = Canvas((0, 1));
        Fixture f = new(image);

        await f.Vm.LoadPictureFileAsync("x.png");

        Assert.That(f.Images.Decodes, Is.EqualTo(1));
        Assert.That(f.Vm.Mask, Is.Not.Null);
        f.Vm.ShowOriginal = true;
        Assert.That(f.Vm.Preview, Is.SameAs(image));
    }

    [TestCase(100, 50)]
    [TestCase(320, 100)]
    [TestCase(640, 480)]
    public async Task Load_AnySize_IsFittedToCanvas(int width, int height)
    {
        Fixture f = new(new PixelImage(width, height, White(width, height)));

        await f.Vm.LoadPictureFileAsync("x.png");

        Assert.That(f.Dialogs.Messages, Is.Empty);
        Assert.That(f.Images.Fits, Is.EqualTo(new[] { ImageFit.Contain }), "默认等比留白");
        Assert.That(f.Vm.Mask, Has.Property("Width").EqualTo(320).And.Property("Height").EqualTo(120));
        Assert.That(f.Vm.SourceSizeText, Is.EqualTo($"原图 {width}×{height}"));
    }

    [Test]
    public async Task FitMode_RefitsFromTheDecodedSourceWithoutReloading()
    {
        Fixture f = new(new PixelImage(640, 480, White(640, 480)));
        await f.Vm.LoadPictureFileAsync("x.png");

        f.Vm.FitModeIndex = 1;
        f.Vm.FitModeIndex = 2;

        Assert.That(f.Images.Fits, Is.EqualTo(new[] { ImageFit.Contain, ImageFit.Cover, ImageFit.Stretch }));
        Assert.That(f.Images.Decodes, Is.EqualTo(1));
    }

    [TestCase(0, 0, "当前位置:0,0")]
    [TestCase(411, 141, "当前位置:160,60")]
    [TestCase(821.9, 281.9, "当前位置:319,119")]
    [TestCase(822, 282, "当前位置:319,119")]
    public void PicturePressed_ScalesToCanvasCoordinates(double x, double y, string expected)
    {
        Fixture f = new(Canvas());

        f.Vm.OnPicturePressed(x, y, 822, 282);

        Assert.That(f.Vm.CurPosText, Is.EqualTo(expected));
    }

    [Test]
    public async Task PixelArt_RefitsWithNearestNeighbour()
    {
        Fixture f = new(new PixelImage(640, 480, White(640, 480)));
        await f.Vm.LoadPictureFileAsync("x.png");

        f.Vm.PixelArt = true;

        Assert.That(f.Images.Nearest, Is.EqualTo(new[] { false, true }));
    }

    [Test]
    public async Task Adjustments_ChangeTheMaskAndTheEstimate()
    {
        // 灰点 (0,0)、孤立黑点 (60,300)、5×5 实心方块
        List<(int, int)> dots = [(60, 300)];
        for (int r = 10; r < 15; r++)
            for (int c = 10; c < 15; c++)
                dots.Add((r, c));
        byte[] rgba = White(320, 120);
        foreach ((int r, int c) in dots)
            SetPixel(rgba, r, c, 0, 0, 0, 255);
        SetPixel(rgba, 0, 0, 100, 100, 100, 255);
        Fixture f = new(new PixelImage(320, 120, rgba));
        await f.Vm.LoadPictureFileAsync("x.png");
        double full = Minutes(f.Vm.EstimateText);

        f.Vm.ToneBrightness = 20;
        Assert.That(f.Vm.Mask![0, 0], Is.False, "提亮后灰点不画");

        Assert.That(f.Vm.SpeckText, Is.EqualTo("关闭"));
        f.Vm.SpeckSize = 1;
        Assert.That(f.Vm.SpeckText, Is.EqualTo("≤ 1 点"));
        Assert.That(f.Vm.Mask![300, 60], Is.False, "孤立黑点被去掉");
        Assert.That(Minutes(f.Vm.EstimateText), Is.LessThan(full));

        f.Vm.OutlineMode = true;
        Assert.That(f.Vm.Mask![12, 12], Is.False, "方块内部挖空");
        Assert.That(f.Vm.Mask![10, 10], Is.True);
        Assert.That(f.Vm.Preview!.GetPixel(12, 12).ToArgb(), Is.EqualTo(System.Drawing.Color.White.ToArgb()), "预览跟着变");
    }

    [Test]
    public async Task ResetTone_RestoresDefaultsWithASingleUpdate()
    {
        byte[] rgba = White(320, 120);
        SetPixel(rgba, 0, 0, 100, 100, 100, 255);
        Fixture f = new(new PixelImage(320, 120, rgba));
        await f.Vm.LoadPictureFileAsync("x.png");
        f.Vm.ToneBrightness = 40;
        f.Vm.ToneContrast = -30;
        f.Vm.ToneGamma = 1.5;
        int previews = 0;
        f.Vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(DrawingBoardViewModel.Preview))
                previews++;
        };

        f.Vm.ResetToneCommand.Execute(null);

        Assert.That((f.Vm.ToneBrightness, f.Vm.ToneContrast, f.Vm.ToneGamma), Is.EqualTo((0, 0, 1.0)));
        Assert.That(previews, Is.EqualTo(1));
        Assert.That(f.Vm.Mask![0, 0], Is.True);
    }

    /// <summary>白底黑圆（半径 30）加一个孤立黑点 (5,5)。</summary>
    private static PixelImage DiscWithSpeck()
    {
        List<(int, int)> dots = [(5, 5)];
        for (int r = 0; r < 120; r++)
            for (int c = 0; c < 320; c++)
                if ((c - 160) * (c - 160) + (r - 60) * (r - 60) < 30 * 30)
                    dots.Add((r, c));
        return Canvas([.. dots]);
    }

    [Test]
    public async Task Reverse_IsAppliedLast_AfterOutline()
    {
        Fixture f = new(DiscWithSpeck());
        await f.Vm.LoadPictureFileAsync("x.png");
        f.Vm.OutlineMode = true;
        bool[] outline = Cells(f.Vm.Mask!).ToArray();

        f.Vm.Reverse = true;

        Assert.That(Cells(f.Vm.Mask!), Is.EqualTo(outline.Select(ink => !ink)), "黑色轮廓线反转成黑底白线");
        Assert.That(f.Vm.Mask![160, 60], Is.True, "圆内（轮廓挖空的部分）反转后要画");
        Assert.That(f.Vm.Mask![160, 31], Is.False, "圆周（轮廓线，圆的最上一行是第 31 行）反转后留白");
    }

    [Test]
    public async Task Reverse_IsAppliedLast_AfterSpeckRemoval()
    {
        Fixture f = new(DiscWithSpeck());
        await f.Vm.LoadPictureFileAsync("x.png");
        f.Vm.SpeckSize = 1;
        f.Vm.Reverse = true;

        Assert.That(f.Vm.Mask![5, 5], Is.True, "孤立黑点先被去掉、再反转：与周围背景一样要画");
    }

    [Test]
    public async Task Reverse_Alone_StillSwapsInkAndBlank()
    {
        PixelImage image = DiscWithSpeck();
        Fixture f = new(image);
        await f.Vm.LoadPictureFileAsync("x.png");

        f.Vm.Reverse = true;

        Assert.That(Cells(f.Vm.Mask!), Is.EqualTo(Cells(InkMask.Binarize(image, new BinarizeOptions(50, true)))));
    }
}