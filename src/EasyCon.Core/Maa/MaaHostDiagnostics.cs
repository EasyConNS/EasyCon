#nullable enable
using EasyCon.Core.Capabilities;
using MaaFramework.Binding;
using MaaFramework.Binding.Buffers;
using OpenCvSharp;
using System.Diagnostics;

namespace EasyCon.Core.Maa;

public sealed record MaaRuntimeCheck(string Runtime, string? Binding, bool ResourceLoaded,
    bool TaskerInitialized, string RecognitionStatus, bool PixelRoundTrip, IReadOnlyDictionary<string, string> NativeLibraries);
public sealed record MaaOcrRuntimeCheck(string ModelGeneration, string Expected, string Actual, double RawConfidence,
    int LegacyConfidence, string Compatibility);

/// <summary>无硬件的发行目录自检。加载真实原生资源、显式图像识别与两套 OpenCV 的 PNG 桥接。</summary>
public static class MaaHostDiagnostics
{
    public static MaaOcrRuntimeCheck CheckOcr(string profilePath)
    {
        MaaOcrProfile profile = MaaOcrProfile.Load(profilePath);
        using MaaOcrService ocr = new(profile);
        if (!ocr.Init(new OcrConfig { Language = "eng" })) throw new InvalidOperationException("中英试点 OCR 模型初始化失败");
        using Mat image = new(64, 256, MatType.CV_8UC3, Scalar.All(255));
        Cv2.PutText(image, "12345", new Point(12, 46), HersheyFonts.HersheySimplex, 1.3, Scalar.Black, 2, LineTypes.AntiAlias);
        string actual = ocr.Recognize(ImageRef.FromBase64(Convert.ToBase64String(image.ToBytes(".png"))), new OcrQuery { Language = "eng" });
        if (actual != "12345" || ocr.LastConfidence <= 0) throw new InvalidOperationException($"OCR 显式图片自检失败: [{actual}], confidence={ocr.LastConfidence}");
        return new(profile.Digest, "12345", actual, ocr.LastRawConfidence, ocr.LastConfidence, profile.Compatibility);
    }

    public static MaaRuntimeCheck CheckRuntime()
    {
        string version = MaaEnvironment.RuntimeVersion;
        using MaaResource resource = MaaEnvironment.LoadBootstrap();
        // binding 的 required 属性通过官方 Null 哨兵表达“不绑定控制器”，不创建假硬件句柄。
        using MaaTasker tasker = new() { Resource = resource, Controller = MaaController.Null, DisposeOptions = DisposeOptions.None };
        if (!tasker.IsInitialized) throw new InvalidOperationException("无控制器的 Maa Tasker 未初始化");
        using Mat source = new(18, 32, MatType.CV_8UC3, new Scalar(7, 51, 209));
        using MaaImageBuffer buffer = new();
        if (!buffer.TrySetEncodedData(source.ToBytes(".png")) || !buffer.TryGetEncodedData(out byte[]? png) || png == null)
            throw new InvalidDataException("Maa PNG 图像桥接失败");
        using Mat roundTrip = Cv2.ImDecode(png, ImreadModes.Color);
        bool equal = source.Size() == roundTrip.Size() && Cv2.Norm(source, roundTrip) == 0;
        if (!equal) throw new InvalidDataException("Maa/OpenCvSharp PNG 往返像素不一致");
        MaaJobStatus status = tasker.AppendRecognition("DirectHit", "{}", buffer).Wait();
        if (status != MaaJobStatus.Succeeded) throw new InvalidOperationException($"Maa 显式图片 DirectHit 自检失败: {status}");
        tasker.ClearCache();
        Dictionary<string, string> libraries = new();
        if (OperatingSystem.IsWindows())
        {
            using Process process = Process.GetCurrentProcess();
            foreach (ProcessModule module in process.Modules)
                if (module.ModuleName.StartsWith("Maa", StringComparison.OrdinalIgnoreCase) ||
                    module.ModuleName.Contains("OpenCvSharpExtern", StringComparison.OrdinalIgnoreCase))
                    libraries[module.ModuleName] = module.FileName;
        }
        return new(version, typeof(MaaCustomController).Assembly.GetName().Version?.ToString(),
            resource.IsLoaded, tasker.IsInitialized, status.ToString(), equal, libraries);
    }
}