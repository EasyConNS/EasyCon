namespace EasyCon.Core.Services;

public interface ICaptureService
{
    bool IsOpened { get; }
    IEnumerable<ImgLabelInfo> LoadedLabels { get; }
    event Action? CaptureStatusChanged;
    void LoadImgLabels(string path);
    string[] GetCaptureSources();
    bool TryConnect(string sourceName);
    void Disconnect();
    (string name, int value)[] GetCaptureTypes();
    void SetOpened(bool opened);
}

public record ImgLabelInfo(string name);