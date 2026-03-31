namespace EasyCon.Core;

public record ConfigState
{
    public bool ShowControllerHelp { get; set; } = true;
    public string CaptureType { get; set; } = "ANY";
    public string AlertToken { get; set; } = string.Empty;
    public string ChannelToken { get; set; } = string.Empty;
    public bool EnableAlertToken { get; set; } = false;
    public bool EnableChannelToken { get; set; } = false;
    public bool EnableAutoCompletion { get; set; } = false;

    public bool ChannelControl { get; set; } = false;

    public bool CheckPPToken() => AlertToken.Length == 32;
}