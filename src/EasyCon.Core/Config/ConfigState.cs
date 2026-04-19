namespace EasyCon.Core.Config;

public record ConfigState
{
    public string CaptureType { get; set; } = "ANY";
    public bool ShowControllerHelp { get; set; } = true;
    public bool EnableAutoCompletion { get; set; } = false;
}
