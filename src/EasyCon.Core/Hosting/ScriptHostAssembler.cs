#nullable enable
using EasyCon.Capture;
using EasyCon.Core.Capabilities;
using EasyCon.Core.Logging;
using EasyScript;

namespace EasyCon.Core.Hosting;

/// <summary>
/// 一次脚本运行的「能力原料」清单。宿主（GUI / CLI）只负责凑齐原料；
/// 组合规则、默认值与生命周期集中在 <see cref="ScriptHostAssembler"/>。
///
/// <para>
/// 收敛动机：装配原先在 GUI（<c>EasyCon2.Avalonia.Core/Services/ScriptService.cs</c>）与
/// CLI（<c>EasyCon2.CLI/Program.cs</c>）各手写一份对象字面量，已出现静默漂移——
/// CLI 只 Dispose <c>Ocr</c> 不 Dispose <c>Inference</c>、两宿主都不装配
/// <see cref="IHostEnvironment"/>（ARG/APP 走 EcxVm 硬编码回落）、文件能力靠
/// <c>BuiltinCallable</c> 里的静态单例兜底。这些差异集中在装配器后不可能再各自漂移。
/// </para>
/// </summary>
public sealed class ScriptHostContext
{
    /// <summary>PRINT/ALERT/FREAD/BEEP 落点；null = 输出静默丢弃。</summary>
    public IConsoleIo? Console { get; init; }

    /// <summary>L1 键鼠摇杆来源（脚本层 <see cref="ICGamePad"/>）；null 时脚本按键指令得到「不支持」。</summary>
    public ICGamePad? Pad { get; init; }

    /// <summary>截屏帧委托（Base64 PNG）；装配器据此装配 <see cref="ICaptureSource"/>。</summary>
    public FrameDelegate? Frame { get; init; }

    /// <summary>截屏帧端口（优先于 <see cref="Frame"/>）；宿主已持有端口实例时用本项。</summary>
    public ICaptureSource? CaptureSource { get; init; }

    /// <summary>手柄端口（优先于 <see cref="Pad"/>）；嵌套装配（Flow 节点内跑脚本）用本项避免二次适配。</summary>
    public IPadInput? PadInput { get; init; }

    /// <summary>ROI 裁剪委托；与 <see cref="LabelMatch"/> 任一非 null 即装配 <see cref="IVisionService"/>。</summary>
    public RoiDelegate? Roi { get; init; }

    /// <summary>图像标签匹配委托；null 时 <c>@标签</c> 求值抛「图像标签匹配器未初始化」。</summary>
    public LabelMatchDelegate? LabelMatch { get; init; }

    /// <summary>图像处理/标签匹配端口（优先于 <see cref="Roi"/>/<see cref="LabelMatch"/> 委托）。</summary>
    public IVisionService? VisionService { get; init; }

    /// <summary>OCR 后端（所有权移交装配结果）；null 且 <see cref="EnableOcr"/> 时装配默认 Tesseract。</summary>
    public IOcrService? Ocr { get; init; }

    /// <summary>推理后端（所有权移交装配结果）；null 且 <see cref="EnableInference"/> 时装配默认 DNN。</summary>
    public IInference? Inference { get; init; }

    /// <summary>文件族能力；null = 桌面参考实现 <see cref="DesktopFileSystem.Instance"/>。</summary>
    public IFileSystem? Files { get; init; }

    /// <summary>ARG(i) 参数表；null = 空表。装配后优先于 <c>ScriptHostOptions.Args</c>（EcxVm 既有语义）。</summary>
    public string[]? Args { get; init; }

    /// <summary>__APP__ 与 tessdata 的基准目录；null = 进程 BaseDirectory。</summary>
    public string? AppDir { get; init; }

    /// <summary>tessdata 目录；null = &lt;AppDir&gt;/Tessdata。</summary>
    public string? TessdataPath { get; init; }

    /// <summary>未显式提供 <see cref="Ocr"/> 时是否装配默认 Tesseract（默认 true）。</summary>
    public bool EnableOcr { get; init; } = true;

    /// <summary>未显式提供 <see cref="Inference"/> 时是否装配默认 DNN（默认 true）。</summary>
    public bool EnableInference { get; init; } = true;

    /// <summary>
    /// 借用语义（默认 false = 装配结果接管 <see cref="Ocr"/>/<see cref="Inference"/> 所有权并负责释放）。
    /// 置 true 时资源所有权仍属调用方——嵌套装配必须如此，
    /// 否则内层脚本运行结束时释放掉外层运行还在用的 OCR/推理服务。
    /// </summary>
    public bool BorrowResources { get; init; }
}

/// <summary>
/// 装配结果：能力集 + 本次装配拥有的原生资源（OCR 引擎、DNN 会话）。
/// 调用方必须在脚本运行结束后 Dispose（通常在 <c>using</c> 作用域内）；重复 Dispose 安全。
/// 装配器**接管**传入的 <see cref="ScriptHostContext.Ocr"/>/<see cref="ScriptHostContext.Inference"/> 所有权。
/// </summary>
public sealed class CapabilityLease(CapabilitySet capabilities, IOcrService? ocr, IInference? inference,
    bool ownsResources = true) : IDisposable
{
    private int _disposed;

    /// <summary>本次运行的能力集。</summary>
    public CapabilitySet Capabilities { get; } = capabilities;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        // 借用语义：资源所有权在调用方（嵌套装配的外层），内层释放不得拆掉共享服务
        if (!ownsResources)
            return;

        DisposeQuietly(ocr, nameof(IOcrService));
        DisposeQuietly(inference, nameof(IInference));
    }

    /// <summary>释放失败不能反噬调用方（本方法通常在 finally 中执行）。</summary>
    private static void DisposeQuietly(IDisposable? resource, string name)
    {
        if (resource is null)
            return;

        try
        {
            resource.Dispose();
        }
        catch (Exception ex)
        {
            CoreLog.Warn($"{name} 释放失败: {ex.Message}");
        }
    }
}

/// <summary>
/// 宿主能力装配器 —— **全仓唯一构造 <see cref="CapabilitySet"/> 的地方**（组合根的唯一能力组装点）。
///
/// <para>契约（改装配行为只改这里，宿主不得自行拼装）：</para>
/// <list type="number">
/// <item>「null = 不可用」：宿主没给的原料对应能力即为 null，不静默造替身
/// （例外是 <see cref="IFileSystem"/>，其缺省是桌面参考实现，与 VM 文件族语义一致）。</item>
/// <item>默认值只在此定义一处：<see cref="IHostEnvironment"/> 恒装配、OCR/DNN 缺省装配但可关。</item>
/// <item>返回的 <see cref="CapabilityLease"/> 拥有 OCR/推理资源，宿主负责释放。</item>
/// <item>装配器不感知具体宿主：只吃委托与端口接口（<see cref="FrameDelegate"/> 等），
/// 不认识 ICaptureService / FrameProducer，故 Core 不反向依赖宿主。</item>
/// </list>
/// </summary>
public static class ScriptHostAssembler
{
    /// <summary>按清单装配能力集；返回的租约负责原生资源释放。</summary>
    public static CapabilityLease Assemble(ScriptHostContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        string appDir = string.IsNullOrWhiteSpace(context.AppDir)
            ? AppDomain.CurrentDomain.BaseDirectory
            : context.AppDir;

        IVisionService? vision = context.VisionService
            ?? (context.Roi is null && context.LabelMatch is null
                ? null
                : new DelegateVisionService(context.Roi, context.LabelMatch));

        IOcrService? ocr = context.Ocr;
        if (ocr is null && context.EnableOcr)
        {
            // OCR 无条件装配（与历史行为一致）：tessdata 缺失时识别给出明确错误，
            // 而非让脚本看到「能力不存在」这种无从排查的语义。
            ocr = new TesseractOcrService(new OcrEngineCache
            {
                DefaultDataPath = string.IsNullOrWhiteSpace(context.TessdataPath)
                    ? Path.Combine(appDir, "Tessdata")
                    : context.TessdataPath,
            });
        }

        IInference? inference = context.Inference;
        if (inference is null && context.EnableInference)
            inference = new DnnInference();

        var capabilities = new CapabilitySet
        {
            Input = context.PadInput ?? (context.Pad is null ? null : new PadInputAdapter(context.Pad)),
            Console = context.Console,
            Environment = new HostEnvironment(context.Args ?? [], appDir),
            Files = context.Files ?? DesktopFileSystem.Instance,
            Capture = context.CaptureSource ?? (context.Frame is null ? null : new DelegateCaptureSource(context.Frame)),
            Vision = vision,
            Ocr = ocr,
            Inference = inference,
        };

        return new CapabilityLease(capabilities, ocr, inference, ownsResources: !context.BorrowResources);
    }
}