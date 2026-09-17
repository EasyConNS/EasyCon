using EasyCon.Core.LLM.Models;

namespace EasyCon.Core.LLM;

public static class ChatClientFactory
{
    private static readonly Dictionary<string, IChatClient> _cache = new(StringComparer.Ordinal);
    private static readonly object _lock = new();

    public static IChatClient Create(ProviderConfig provider)
    {
        var key = $"{provider.Api}|{provider.BaseUrl}";
        lock (_lock)
        {
            if (_cache.TryGetValue(key, out var cached))
                return cached;

            var client = provider.Api switch
            {
                "openai-completions" => new OpenAIChatClient(provider.BaseUrl, provider.ApiKey),
                _ => throw new NotSupportedException($"不支持的 API 类型: {provider.Api}")
            };
            _cache[key] = client;
            return client;
        }
    }

    /// <summary>
    /// 清除所有缓存的客户端实例（配置变更时调用）。
    /// </summary>
    public static void ClearCache()
    {
        lock (_lock)
        {
            foreach (var client in _cache.Values)
                client.Dispose();
            _cache.Clear();
        }
    }
}