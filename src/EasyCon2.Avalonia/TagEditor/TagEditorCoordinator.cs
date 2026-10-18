using Avalonia.Platform.Storage;
using EasyCon.Capture;
using EasyCon.Core;
using EasyCon.Core.Config;
using EasyCon.Core.Maa;
using EasyCon2.Avalonia.Markup;
using EasyCon2.Avalonia.Models;
using EasyCon2.Avalonia.Services;
using OpenCvSharp;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using ILogService = EasyCon.Core.Services.ILogService;

namespace EasyCon2.Avalonia.TagEditor;

/// <summary>
/// 标签编辑器的跨模块协调：截图采集、模板匹配测试、图片文件打开。
/// 持有唯一的 TagEditorViewModel 实例（MainWindowViewModel 只做代理暴露）。
/// 不持有调用方 VM 引用。
/// </summary>
public sealed class TagEditorCoordinator : IDisposable
{
    private readonly ICaptureService _captureService;
    private readonly IDialogService _dialogService;
    private readonly ILogService _logService;

    private TagEditorViewModel? _boundVm;

    public TagEditorCoordinator(
        ICaptureService captureService,
        IDialogService dialogService,
        ILogService logService,
        IImageProcessor? imageProcessor = null)
    {
        _captureService = captureService;
        _dialogService = dialogService;
        _logService = logService;
        ViewModel = new TagEditorViewModel(imageProcessor);
    }

    /// <summary>协调器恒定持有的标签编辑器 ViewModel（构造即创建，终生不变）。</summary>
    public TagEditorViewModel ViewModel { get; }

    /// <summary>当前视频源是否已连接（供外部同步 TagEditorViewModel.IsCaptureConnected）。</summary>
    public bool IsCaptureConnected => _captureService.IsConnected;

    /// <summary>
    /// 把 TagEditorViewModel 的四个事件（OpenFileRequested / CaptureScreenshotRequested /
    /// LabelTestRequested / LogMessage）接到协调器方法上。幂等（重复 Bind 同一实例不重复订阅）。
    /// </summary>
    public void Bind(TagEditorViewModel vm)
    {
        if (_boundVm == vm)
            return;

        if (_boundVm != null)
            Unbind(_boundVm);

        _boundVm = vm;
        vm.OpenFileRequested += OnOpenFileRequested;
        vm.CaptureScreenshotRequested += OnCaptureScreenshotRequested;
        vm.LabelTestRequested += OnLabelTestRequested;
        vm.LogMessage += OnLogMessage;
    }

    /// <summary>
    /// 从 .il 文件加载标签到 ViewModel（原 OnFileTreeFileActivated 的 .il 分支；
    /// 原"实例为空则二次 new TagEditorViewModel"的路径为死代码，已删除——实例由本类恒定持有）。
    /// 切换到卡片标签页属界面编排，留在调用方。
    /// </summary>
    public void LoadLabelFile(string filePath)
    {
        try
        {
            var label = ECCore.LoadIL(filePath);
            ViewModel.LoadFromLabel(label);
        }
        catch (Exception ex)
        {
            _logService.AddLog($"加载标签文件失败: {ex.Message}");
        }
    }

    public void Dispose()
    {
        if (_boundVm != null)
        {
            Unbind(_boundVm);
            _boundVm = null;
        }
    }

    private void Unbind(TagEditorViewModel vm)
    {
        vm.OpenFileRequested -= OnOpenFileRequested;
        vm.CaptureScreenshotRequested -= OnCaptureScreenshotRequested;
        vm.LabelTestRequested -= OnLabelTestRequested;
        vm.LogMessage -= OnLogMessage;
    }

    private void OnOpenFileRequested()
    {
        _ = OpenTagEditorImageFileAsync();
    }

    private void OnCaptureScreenshotRequested()
    {
        _ = CaptureScreenshotForTagEditorAsync();
    }

    private void OnLabelTestRequested()
    {
        _ = ExecuteLabelTestAsync();
    }

    private void OnLogMessage(string message)
    {
        _logService.AddLog(message);
    }

    private async Task ExecuteLabelTestAsync()
    {
        if (!_captureService.IsConnected)
        {
            _logService.AddLog("请先连接视频源");
            return;
        }

        try
        {
            byte[]? resultPng = null;
            double matchDegree = 0;
            string labelName = "";
            string content = JsonSerializer.Serialize(ViewModel.Label);
            ImgLabel label = JsonSerializer.Deserialize<ImgLabel>(content)!;
            label.name = ViewModel.Label.name;

            // 帧租约与 OpenCV 模板匹配都在线程池完成，避免大图匹配冻结 UI
            await Task.Run(() =>
            {
                using FrameLease? lease = _captureService.AcquireLatestFrame();
                if (lease == null || lease.Mat.Empty())
                    throw new InvalidOperationException(L10n.T("Text.Msg.TagTestNoFrame"));

                Mat mat = lease.Mat;
                LegacyLabelSource source = new(label.name, Path.Combine(AppPaths.DataDir, "preview.IL"), content,
                    Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(content))), 0);
                string? ocrPath = Environment.GetEnvironmentVariable("EASYCON_MAA_OCR_PROFILE");
                LabelGeneration generation = MaaLabelConverter.Build(new([source], [], 1),
                    MaaLabelConverter.CacheRoot(Path.Combine(AppPaths.DataDir, "preview")), LegacyCompatibilityProfile.LegacyGui,
                    ocrProfile: string.IsNullOrWhiteSpace(ocrPath) ? null : MaaOcrProfile.Load(ocrPath)).Generation!;
                generation.Preflight([label.name]);
                using MaaVisionService vision = new(generation);
                LegacyRecognitionTrace match = vision.MatchImage(label.name, lease.EncodedPng, lease.Metadata.FrameId, lease.Metadata.SourceEpoch);
                matchDegree = match.LegacyFloat;

                byte[]? png = null;
                if (match.Box.Length == 4)
                {
                    int roiX = Math.Clamp(match.Box[0], 0, mat.Width);
                    int roiY = Math.Clamp(match.Box[1], 0, mat.Height);
                    int roiW = Math.Clamp(label.TargetWidth, 0, mat.Width - roiX);
                    int roiH = Math.Clamp(label.TargetHeight, 0, mat.Height - roiY);

                    if (roiW > 0 && roiH > 0)
                    {
                        using Mat roi = new(mat, new Rect(roiX, roiY, roiW, roiH));
                        png = roi.ToBytes(".png");
                    }
                }

                resultPng = png;
                labelName = label.name;
            });

            // 更新匹配度显示和结果图
            ViewModel.SetTestResult(matchDegree, resultPng);

            _logService.AddLog(string.Format(L10n.T("Text.Msg.TagTestResult"), labelName, matchDegree));
        }
        catch (InvalidOperationException ex)
        {
            _logService.AddLog(ex.Message);
        }
        catch (Exception ex)
        {
            _logService.AddLog(string.Format(L10n.T("Text.Msg.TagTestFail"), ex.Message));
        }
    }

    private async Task OpenTagEditorImageFileAsync()
    {
        try
        {
            var cacheDir = EasyCon.Core.Config.AppPaths.CaptureCacheDir;
            var files = await _dialogService.OpenFilesAsync("选择图片文件",
            [
                new FileDialogFilter("图片文件", ["*.png", "*.jpg", "*.jpeg", "*.bmp", "*.gif"]),
                new FileDialogFilter("所有文件", ["*"])
            ], cacheDir);

            if (files.Count > 0)
                ViewModel.LoadImageFromFile(files[0]);
        }
        catch (Exception ex)
        {
            _logService.AddLog($"打开文件对话框失败: {ex.Message}");
        }
    }

    private async Task CaptureScreenshotForTagEditorAsync()
    {
        if (!_captureService.IsConnected)
        {
            _logService.AddLog("请先连接视频源");
            return;
        }

        try
        {
            using var lease = _captureService.AcquireLatestFrame();
            if (lease == null || lease.Mat.Empty())
            {
                _logService.AddLog("截图失败：无法获取视频帧");
                return;
            }
            var mat = lease.Mat;

            // Mat 编码为 PNG 字节交给 VM（VM 内部解码显示并保留字节供裁剪）；
            // 1080p PNG 编码可达百毫秒级，放后台执行避免冻结 UI（租约保活至编码完成）
            var imageBytes = await Task.Run(() => mat.ToBytes(".png"));
            ViewModel.SetScreenshot(imageBytes);
            _logService.AddLog("截图成功");
        }
        catch (Exception ex)
        {
            _logService.AddLog($"截图失败：{ex.Message}");
        }
    }
}