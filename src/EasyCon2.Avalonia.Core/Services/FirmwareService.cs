using EasyCon.Core.Services;
using EasyCon.Script.Asm;
using System.Diagnostics;
using System.IO;

namespace EasyCon2.Avalonia.Core.Services;

public class FirmwareService : IFirmwareService
{
    private readonly ILogService _logService;
    private readonly IDeviceService _deviceService;
    private readonly IScriptService _scriptService;
    private readonly BoardInfo[] _boards;

    public FirmwareService(ILogService logService, IDeviceService deviceService, IScriptService scriptService)
    {
        _logService = logService;
        _deviceService = deviceService;
        _scriptService = scriptService;
        _boards = GetDefaultBoards();
    }

    public BoardInfo[] GetSupportedBoards() => _boards;

    private static BoardInfo[] GetDefaultBoards() =>
    [
        new("Leonardo", "Leonardo", 924),
        new("Teensy 2.0", "Teensy2", 924),
        new("Teensy 2.0++", "Teensy2pp", 3996),
        new("Beetle", "Beetle", 924),
        new("Arduino UNO R3", "UNO", 412),
    ];

    public async Task<bool> GenerateFirmware(int boardIndex, string scriptText, string? fileName)
    {
        if (!await _scriptService.Compile(scriptText, fileName))
            return false;

        try
        {
            _logService.AddLog("开始生成固件...");
            var bytes = await _scriptService.Build(false);
            var board = _boards[boardIndex];
            var firmwarePath = @"Firmware\";
            File.WriteAllBytes("temp.bin", bytes);

            var fwname = GetFirmwareName(board.CoreName);
            if (fwname == null)
                throw new Exception("未找到固件！请确认程序Firmware目录下是否有对应固件文件！");

            var hexStr = File.ReadAllText(firmwarePath + fwname);
            hexStr = HexWriter.WriteHex(hexStr, bytes, board.DataSize, 0x45);
            fwname = fwname.Replace(".", "+Script.");
            File.WriteAllText(fwname, hexStr);

            _logService.AddLog("固件生成完毕");
            Debug.WriteLine("[Beep] Firmware generated");
            return true;
        }
        catch (AssembleException)
        {
            _logService.AddLog("固件生成失败");
            Debug.WriteLine("[Hand] Firmware generation failed");
        }
        catch (Exception ex)
        {
            _logService.AddLog("固件生成失败！" + ex.Message);
            Debug.WriteLine("[Hand] Firmware generation failed");
        }
        return false;
    }

    public async Task<bool> Flash(int boardIndex, bool autoRun, string scriptText, string? fileName)
    {
        var board = _boards[boardIndex];
        try
        {
            _logService.AddLog("开始烧录...");
            var bytes = await _scriptService.Build(autoRun);
            if (bytes.Length > board.DataSize)
                throw new Exception("长度超出限制");

            if (_deviceService.Flash(bytes))
            {
                _logService.AddLog("烧录完毕");
                Debug.WriteLine("[Beep] Flash success");
                return true;
            }
            throw new Exception("请检查设备连接后重试");
        }
        catch (AssembleException)
        {
            _logService.AddLog("烧录失败");
            Debug.WriteLine("[Hand] Flash failed");
        }
        catch (Exception ex)
        {
            _logService.AddLog("烧录失败！" + ex.Message);
            Debug.WriteLine("[Hand] Flash failed");
        }
        return false;
    }

    public async Task<bool> FlashClear()
    {
        if (!_deviceService.Flash(HexWriter.EmptyAsm))
        {
            _logService.AddLog("烧录失败");
            Debug.WriteLine("[Hand] Flash clear failed");
            return false;
        }
        _logService.AddLog("清除完毕");
        Debug.WriteLine("[Beep] Flash clear success");
        return true;
    }

    private static string? GetFirmwareName(string corename, string path = @"Firmware\")
    {
        var dir = new DirectoryInfo(path);
        if (!dir.Exists) return null;
        var max = 0;
        string? filename = null;
        foreach (var fi in dir.GetFiles())
        {
            var m = System.Text.RegularExpressions.Regex.Match(fi.Name, $@"^{corename} v(\d+)\.hex$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (m.Success)
            {
                var ver = int.Parse(m.Groups[1].Value);
                if (ver > max)
                {
                    max = ver;
                    filename = fi.Name;
                }
            }
        }
        return filename;
    }
}