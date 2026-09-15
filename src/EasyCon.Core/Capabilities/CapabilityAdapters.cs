using EasyCon.Script;
using EasyScript;

namespace EasyCon.Core.Capabilities;

/// <summary><see cref="ICGamePad"/> → <see cref="IPadInput"/> 适配。</summary>
public sealed class PadInputAdapter(ICGamePad pad) : IPadInput
{
    public void ClickButtons(GamePadKey key, int duration, CancellationToken token)
        => pad.ClickButtons(key, duration, token);

    public void PressButtons(GamePadKey key) => pad.PressButtons(key);

    public void ReleaseButtons(GamePadKey key) => pad.ReleaseButtons(key);

    public void ClickStick(GamePadKey key, byte x, byte y, int duration, CancellationToken token)
        => pad.ClickStick(key, x, y, duration, token);

    public void SetStick(GamePadKey key, byte x, byte y) => pad.SetStick(key, x, y);

    public void ChangeAmiibo(uint index) => pad.ChangeAmiibo(index);
}

/// <summary><see cref="IIoAdapter"/> → <see cref="IConsoleIo"/> 适配。</summary>
public sealed class ConsoleIoAdapter(IIoAdapter io) : IConsoleIo
{
    public void Print(string message, bool newline = true) => io.Print(message, newline);

    public void Alert(string message) => io.Alert(message);
}

/// <summary>ARG/APP 宿主环境缺省实现。</summary>
public sealed class HostEnvironment(string[] args, string appDir) : IHostEnvironment
{
    public string[] Args { get; } = args;

    public string AppDir { get; } = appDir;
}

/// <summary><see cref="FrameDelegate"/> → <see cref="ICaptureSource"/> 适配（P1 过渡件）。</summary>
public sealed class DelegateCaptureSource(FrameDelegate frame) : ICaptureSource
{
    public string? CaptureFrame(int x, int y, int width, int height) => frame(x, y, width, height);
}

/// <summary>
/// <see cref="RoiDelegate"/>/<see cref="LabelMatchDelegate"/> → <see cref="IVisionService"/> 适配（P1 过渡件）。
/// labelMatch 未装配时 MatchLabel 抛错，与旧 EcxVm.ImgLabel 语义一致。
/// </summary>
public sealed class DelegateVisionService(RoiDelegate? roi, LabelMatchDelegate? labelMatch) : IVisionService
{
    public int MatchLabel(string labelName)
        => labelMatch != null ? labelMatch(labelName) : throw new Exception("图像标签匹配器未初始化");

    public string? Crop(string imageBase64, int x, int y, int width, int height)
        => roi?.Invoke(imageBase64, x, y, width, height);
}

/// <summary>
/// 旧 OCR 委托（OcrDelegate/OcrInitDelegate/ocrConf）→ <see cref="IOcrService"/> 桥（P1 过渡件）。
/// Recognize 忽略 image（旧 OcrDelegate 自带采集，坐标即全图区域）；P3 OCR 洞改走 IOcrService 后退役。
/// </summary>
public sealed class DelegateOcrService(OcrDelegate? ocr, OcrInitDelegate? ocrInit, Func<int>? ocrConf) : IOcrService
{
    /// <summary>底层旧识别委托（CapabilityEvalContext 直通用，避免双重采帧）。</summary>
    public OcrDelegate? Ocr => ocr;

    /// <summary>底层旧初始化委托。</summary>
    public OcrInitDelegate? OcrInit => ocrInit;

    public string Backend => "delegate";

    public int LastConfidence => ocrConf?.Invoke() ?? 0;

    public bool Init(OcrConfig cfg)
        => ocrInit?.Invoke(
               cfg.Language,
               cfg.ModelPath ?? "",
               cfg.Options.GetValueOrDefault("tess:engineMode", "DEFAULT"),
               cfg.Options.GetValueOrDefault("tess:psmode", "SINGLE_LINE")) ?? false;

    public string Recognize(ImageRef image, OcrQuery query)
        => ocr != null
            ? ocr(query.X, query.Y, query.Width, query.Height, query.Language ?? "")
            : "ERR!!OCR NOT SUPPORT";

    public void Dispose()
    {
    }
}