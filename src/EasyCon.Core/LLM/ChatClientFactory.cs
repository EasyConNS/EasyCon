using EasyCon.Core.LLM.Models;

namespace EasyCon.Core.LLM;

public static class ChatClientFactory
{
    public static IChatClient Create(ProviderConfig provider)
    {
        return provider.Api switch
        {
            "openai-completions" => new OpenAIChatClient(provider.BaseUrl, provider.ApiKey),
            _ => throw new NotSupportedException($"不支持的 API 类型: {provider.Api}")
        };
    }
}
