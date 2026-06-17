using EzTesseract.Enums;
using EzTesseract.Interop;

namespace EzTesseract;

/// <summary>
/// Tesseract OCR 引擎。对应原 TesseractOCR.Engine 的 capture 用到的子集。
/// </summary>
public sealed class Engine : IDisposable
{
    /// <summary>原生 TessBaseAPI 句柄。</summary>
    internal IntPtr Handle { get; private set; }

    /// <summary>
    /// 初始化引擎。
    /// </summary>
    /// <param name="dataPath">tessdata 的父目录（不含末尾分隔符，为空时用 TESSDATA_PREFIX）。</param>
    /// <param name="language">语言，如 "eng"、"chi_sim+eng"。</param>
    /// <param name="engineMode">引擎模式（OEM）。</param>
    /// <exception cref="TesseractException">引擎创建或初始化失败。</exception>
    public Engine(string dataPath, string language, EngineMode engineMode = EngineMode.Default)
    {
        ArgumentNullException.ThrowIfNull(language);

        var handle = TesseractDll.BaseApiCreate();
        if (handle == IntPtr.Zero)
            throw new TesseractException("Failed to create Tesseract engine (TessBaseAPICreate returned NULL)");

        // 与原 TesseractOCR.Engine.Initialize 一致：规整 datapath
        dataPath = (dataPath ?? string.Empty).Trim().TrimEnd('/').TrimEnd('\\');

        // OEM 通过 Init4 的 mode 参数生效；configs/vars 未使用，传 NULL
        var result = TesseractDll.Init4(
            handle,
            dataPath,
            language,
            (int)engineMode,
            configs: IntPtr.Zero, configs_size: 0,
            vars_vec: IntPtr.Zero, vars_values: IntPtr.Zero,
            vars_vec_size: UIntPtr.Zero,
            set_only_non_debug_params: false);

        if (result == -1)
        {
            // 失败时 Init 内部已释放 handle，但仍调用 Delete 清理并防止双重释放
            TesseractDll.BaseApiDelete(handle);
            throw new TesseractException($"Failed to initialize Tesseract engine (datapath='{dataPath}', language='{language}')");
        }

        Handle = handle;
    }

    /// <summary>
    /// 设置图像与页面分割模式，返回 <see cref="Page"/>（不立即识别）。
    /// 首次访问 <see cref="Page.Text"/>/<see cref="Page.MeanConfidence"/> 时才触发识别。
    /// 调用方负责 Dispose 返回的 Page（using）。
    /// </summary>
    public Page Process(Pix.Image image, PageSegMode pageSegMode)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (image.Handle == IntPtr.Zero)
            throw new ObjectDisposedException(nameof(Pix.Image));
        if (Handle == IntPtr.Zero)
            throw new ObjectDisposedException(nameof(Engine));

        TesseractDll.SetPageSegMode(Handle, (int)pageSegMode);
        TesseractDll.SetImage2(Handle, image.Handle);
        return new Page(this);
    }

    public void Dispose()
    {
        var h = Handle;
        if (h == IntPtr.Zero) return;
        Handle = IntPtr.Zero;
        TesseractDll.BaseApiDelete(h);
        GC.SuppressFinalize(this);
    }

    ~Engine() => Dispose();
}
