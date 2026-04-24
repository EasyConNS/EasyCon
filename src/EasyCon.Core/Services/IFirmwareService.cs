namespace EasyCon.Core.Services;

public interface IFirmwareService
{
    BoardInfo[] GetSupportedBoards();
    Task<bool> GenerateFirmware(int boardIndex, string scriptText, string? fileName);
    Task<bool> Flash(int boardIndex, bool autoRun, string scriptText, string? fileName);
    Task<bool> FlashClear();
}

public record BoardInfo(string DisplayName, string CoreName, int DataSize);