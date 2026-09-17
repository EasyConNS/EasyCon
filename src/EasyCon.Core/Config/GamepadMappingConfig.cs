namespace EasyCon.Core.Config;

public record GamepadMappingConfig
{
    public bool IsSwitchXY { get; init; } = true;
    public float TriggerThreshold { get; init; } = 0.1f;
}