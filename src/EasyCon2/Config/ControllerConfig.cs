using EasyVPad;

namespace EasyCon2.Config;

public record ConfigState
{
    public KeyMapping KeyMapping { get; set; }
    public bool ShowControllerHelp { get; set; } = true;
    public string CaptureType { get; set; } = "ANY";
    public bool EnableAutoCompletion { get; set; } = false;

    public void SetDefault()
    {
        KeyMapping = new KeyMapping();
    }
}
