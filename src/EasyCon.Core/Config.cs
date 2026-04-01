namespace EasyCon.Core;

public record ConfigState
{
    public string CaptureType { get; set; } = "ANY";
    public string AlertToken { get; set; } = string.Empty;
    public string ChannelToken { get; set; } = string.Empty;
    public bool EnableAlertToken { get; set; } = false;
    public bool EnableChannelToken { get; set; } = false;

    public bool CheckPPToken() => AlertToken.Length == 32;
}