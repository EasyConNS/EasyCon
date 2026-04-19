using System.Text;
using System.Text.Json;
using System.Web;

namespace EasyCon.Core.Alert;

public class AlertDispatcher
{
    private readonly List<AlertItem> _items;
    private readonly HttpClient _http;

    public event EventHandler<string>? OnResult;

    public AlertDispatcher(string configPath)
    {
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        _items = Load(configPath);
    }

    private static List<AlertItem> Load(string path)
    {
        if (!File.Exists(path)) return [];

        try
        {
            var json = File.ReadAllText(path);
            var config = JsonSerializer.Deserialize<List<AlertItem>>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            return config ?? [];
        }
        catch
        {
            return [];
        }
    }

    public async Task DispatchAsync(string content, string title = "伊机控消息")
    {
        var enabled = _items.Where(a => a.enable).ToList();
        if (enabled.Count == 0) return;

        var tasks = enabled.Select(item => SendAsync(item, content, title));
        var results = await Task.WhenAll(tasks);

        foreach (var result in results)
        {
            if (result != null)
                OnResult?.Invoke(this, result);
        }
    }

    private async Task<string?> SendAsync(AlertItem item, string content, string title)
    {
        try
        {
            var url = ReplaceVariables(item.url, item.token, item.variables, content, title);
            var method = item.method.ToUpperInvariant() == "POST" ? HttpMethod.Post : HttpMethod.Get;

            var request = new HttpRequestMessage(method, url);

            if (item.headers != null)
            {
                foreach (var (key, value) in item.headers)
                {
                    request.Headers.TryAddWithoutValidation(key, ReplaceVariables(value, item.token, item.variables, content, title));
                }
            }

            if (method == HttpMethod.Post && item.body != null)
            {
                var body = ReplaceVariables(item.body, item.token, item.variables, content, title);
                var contentType = "application/json";

                if (item.headers != null)
                {
                    foreach (var (key, value) in item.headers)
                    {
                        if (key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
                        {
                            contentType = ReplaceVariables(value, item.token, item.variables, content, title);
                            break;
                        }
                    }
                }

                request.Content = new StringContent(body, Encoding.UTF8, contentType);
            }

            var response = await _http.SendAsync(request);
            var respBody = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
                return $"[{item.name}] 推送成功";
            else
                return $"[{item.name}] 推送失败: HTTP {(int)response.StatusCode} - {respBody}";
        }
        catch (Exception ex)
        {
            return $"[{item.name}] 推送失败: {ex.Message}";
        }
    }

    private static string ReplaceVariables(string template, string? token, Dictionary<string, string>? variables, string content, string title)
    {
        var result = template;

        // 替换内置变量
        result = result.Replace("{{token}}", token ?? "");
        result = result.Replace("{{content}}", HttpUtility.UrlEncode(content));
        result = result.Replace("{{title}}", HttpUtility.UrlEncode(title));

        // 替换自定义变量
        if (variables != null)
        {
            foreach (var (key, value) in variables)
            {
                result = result.Replace($"{{{{{key}}}}}", value);
            }
        }

        return result;
    }

    public static void GenerateDefault(string path)
    {
        if (File.Exists(path)) return;

        var json = """
[
  {
    "name": "PushPlus",
    "enable": false,
    "url": "https://www.pushplus.plus/send/{{token}}?content={{content}}&title={{title}}",
    "token": ""
  },
  {
    "name": "Bark",
    "enable": false,
    "url": "https://api.day.app/{{token}}/{{title}}/{{content}}",
    "token": ""
  },
  {
    "name": "自定义Webhook",
    "enable": false,
    "method": "POST",
    "url": "https://example.com/webhook",
    "token": "",
    "headers": {
      "Authorization": "Bearer {{token}}",
      "Content-Type": "application/json"
    },
    "body": "{\"msg\":\"{{content}}\"}"
  }
]
""";
        File.WriteAllText(path, json);
    }
}
