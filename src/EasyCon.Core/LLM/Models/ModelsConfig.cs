namespace EasyCon.Core.LLM.Models;

public class ModelsConfig
{
    public ModelsSection Models { get; set; } = new();
}

public class ModelsSection
{
    public Dictionary<string, ProviderConfig> Providers { get; set; } = new();
}
