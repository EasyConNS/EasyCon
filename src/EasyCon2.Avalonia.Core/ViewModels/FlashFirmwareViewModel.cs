using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EasyCon.Core.Services;
using EasyCon2.Avalonia.Core.Localization;
using EasyCon2.Avalonia.Core.Services;
using System;
using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using System.Windows.Input;

namespace EasyCon2.Avalonia.Core.ViewModels;

/// <summary>
/// 固件与烧录编排：编译烧录/清除烧录/生成固件。所有串口 I/O 均在后台线程执行。
/// </summary>
public partial class FlashFirmwareViewModel : ObservableObject
{
    /// <summary>支持脚本写入的固件协议版本。</summary>
    private const byte FirmwareProtocolVersion = 0x45;

    /// <summary>HexWriter 写入脚本的偏移。</summary>
    private const int FirmwareScriptOffset = 924;

    private readonly IDeviceService _deviceService;
    private readonly IScriptService _scriptService;
    private readonly ILogService _logService;
    private readonly Func<string?> _getEditorText;
    private readonly Func<string?> _getScriptPath;

    /// <summary>正在执行烧录/清除烧录等长耗时串口操作，期间禁止其它设备命令。</summary>
    [ObservableProperty]
    private bool _isFlashing = false;

    // 固件类型列表
    [ObservableProperty]
    private ObservableCollection<string> _firmwareOptions = new() { "leonardo" };

    [ObservableProperty]
    private string _selectedFirmware = "leonardo";

    public ICommand CompileFlashCommand { get; }
    public ICommand ClearFlashCommand { get; }
    public ICommand GenerateFirmwareCommand { get; }

    public FlashFirmwareViewModel(IDeviceService deviceService, IScriptService scriptService, ILogService logService, Func<string?> getEditorText, Func<string?> getScriptPath)
    {
        _deviceService = deviceService;
        _scriptService = scriptService;
        _logService = logService;
        _getEditorText = getEditorText;
        _getScriptPath = getScriptPath;

        CompileFlashCommand = new AsyncRelayCommand(CompileFlashAsync);
        ClearFlashCommand = new AsyncRelayCommand(ClearFlashAsync);
        GenerateFirmwareCommand = new AsyncRelayCommand(GenerateFirmwareAsync);
    }

    private async Task CompileFlashAsync()
    {
        if (!_deviceService.IsConnected)
        {
            _logService.AddLog(L10nBridge.T("Text.Msg.ConnectFirst"));
            return;
        }

        var editorText = _getEditorText();
        if (string.IsNullOrWhiteSpace(editorText))
        {
            _logService.AddLog(L10nBridge.T("Text.Msg.NoScriptToFlash"));
            return;
        }

        if (IsFlashing)
        {
            return;
        }

        IsFlashing = true;
        try
        {
            _logService.AddLog(L10nBridge.T("Text.Msg.CompileStart"));

            // 编译脚本
            if (!await _scriptService.CompileAsync(editorText, _getScriptPath()))
            {
                _logService.AddLog(L10nBridge.T("Text.Msg.CompileFailFlash"));
                return;
            }

            // 组装为字节码
            var bytes = await _scriptService.BuildAsync(true);
            if (bytes == null || bytes.Length == 0)
            {
                _logService.AddLog(L10nBridge.T("Text.Msg.CompileEmptyFlash"));
                return;
            }

            // 版本查询与烧录是同步串口 I/O（分包重试，可能阻塞数分钟），放后台执行避免冻结 UI
            var flashed = await Task.Run(() =>
            {
                var version = _deviceService.GetVersion();
                if (version != FirmwareProtocolVersion)
                {
                    _logService.AddLog(string.Format(L10nBridge.T("Text.Msg.VersionMismatch"), version, FirmwareProtocolVersion));
                    return false;
                }

                _logService.AddLog(string.Format(L10nBridge.T("Text.Msg.Flashing"), bytes.Length));
                return _deviceService.Flash(bytes);
            });

            _logService.AddLog(flashed ? L10nBridge.T("Text.Msg.FlashOk") : L10nBridge.T("Text.Msg.FlashFail"));
        }
        catch (Exception ex)
        {
            _logService.AddLog(string.Format(L10nBridge.T("Text.Msg.FlashError"), ex.Message));
        }
        finally
        {
            IsFlashing = false;
        }
    }

    private async Task ClearFlashAsync()
    {
        if (!_deviceService.IsConnected)
        {
            _logService.AddLog(L10nBridge.T("Text.Msg.ConnectFirst"));
            return;
        }

        if (IsFlashing)
        {
            return;
        }

        IsFlashing = true;
        try
        {
            _logService.AddLog(L10nBridge.T("Text.Msg.ClearFlashing"));
            // 使用空字节数组清除烧录；同步串口 I/O 放后台执行
            var cleared = await Task.Run(() => _deviceService.Flash(Array.Empty<byte>()));
            _logService.AddLog(cleared ? L10nBridge.T("Text.Msg.ClearFlashOk") : L10nBridge.T("Text.Msg.ClearFlashFail"));
        }
        catch (Exception ex)
        {
            _logService.AddLog(string.Format(L10nBridge.T("Text.Msg.ClearFlashError"), ex.Message));
        }
        finally
        {
            IsFlashing = false;
        }
    }

    private async Task GenerateFirmwareAsync()
    {
        var editorText = _getEditorText();
        if (string.IsNullOrWhiteSpace(editorText))
        {
            _logService.AddLog(L10nBridge.T("Text.Msg.NoScriptForFirmware"));
            return;
        }

        _logService.AddLog(L10nBridge.T("Text.Msg.CompileStart"));

        // 编译脚本
        if (!await _scriptService.CompileAsync(editorText, _getScriptPath()))
        {
            _logService.AddLog(L10nBridge.T("Text.Msg.CompileFailFirmware"));
            return;
        }

        // 组装为字节码
        var bytes = await _scriptService.BuildAsync(false);
        if (bytes == null || bytes.Length == 0)
        {
            _logService.AddLog(L10nBridge.T("Text.Msg.CompileEmptyFirmware"));
            return;
        }

        _logService.AddLog(string.Format(L10nBridge.T("Text.Msg.FirmwareGenerateStart"), SelectedFirmware));

        try
        {
            // 检查固件目录
            var firmwarePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Firmware");
            if (!Directory.Exists(firmwarePath))
            {
                _logService.AddLog(L10nBridge.T("Text.Msg.FirmwareDirMissing"));
                return;
            }

            // 查找对应的固件文件
            var firmwareFile = GetFirmwareFile(firmwarePath, SelectedFirmware);
            if (firmwareFile == null)
            {
                _logService.AddLog(string.Format(L10nBridge.T("Text.Msg.FirmwareFileMissing"), SelectedFirmware));
                return;
            }

            // 读取固件模板并写入脚本。hex 读写与逐行改写在 UI 续体上直接做会冻结
            // 界面数百毫秒，与同文件烧录/清除一样移到线程池执行
            var scriptBytes = bytes;
            var scriptOffset = FirmwareScriptOffset;
            var protocolVersion = FirmwareProtocolVersion;
            var outputPath = await Task.Run(() =>
            {
                var hexContent = File.ReadAllText(firmwareFile);
                var outputFileName = Path.GetFileNameWithoutExtension(firmwareFile) + "+Script" + Path.GetExtension(firmwareFile);
                var output = Path.Combine(Environment.CurrentDirectory, outputFileName);

                // 使用HexWriter写入脚本到固件
                var resultHex = EasyCon.Script.Asm.HexWriter.WriteHex(hexContent, scriptBytes, scriptOffset, protocolVersion);
                File.WriteAllText(output, resultHex);
                return output;
            });

            _logService.AddLog(string.Format(L10nBridge.T("Text.Msg.FirmwareGenerated"), outputPath));
        }
        catch (Exception ex)
        {
            _logService.AddLog(string.Format(L10nBridge.T("Text.Msg.FirmwareError"), ex.Message));
        }
    }

    private static string? GetFirmwareFile(string firmwarePath, string coreName)
    {
        var dir = new DirectoryInfo(firmwarePath);
        if (!dir.Exists) return null;

        var max = 0;
        string? filename = null;
        foreach (var fi in dir.GetFiles("*.hex"))
        {
            var m = Regex.Match(
                fi.Name,
                $@"^{coreName} v(\d+)\.hex$",
                RegexOptions.IgnoreCase);

            if (m.Success)
            {
                var ver = int.Parse(m.Groups[1].Value);
                if (ver > max)
                {
                    max = ver;
                    filename = fi.FullName;
                }
            }
        }

        return filename;
    }
}