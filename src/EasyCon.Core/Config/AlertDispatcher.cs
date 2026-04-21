using System.Text;
using System.Text.Json;
using System.Web;

namespace EasyCon.Core.Config;

public class AlertDispatcher
{
    private readonly List<AlertItem> _items;
    private readonly HttpClient _http;

    public event EventHandler<string> OnResult;

    public AlertDispatcher(AlertConfig config)
    {
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(config.timeout) };
        _items = config.alerts;
    }

    public async Task DispatchAsync(string content, string title = "伊机控消息")
    {
        var enabled = _items.Where(a => a.enable).ToList();
        if (enabled.Count == 0) return;

        var tasks = enabled.Select(item => SendAsync(item, content, title));
        var results = await Task.WhenAll(tasks);

        foreach (var result in results)
        {
            OnResult?.Invoke(this, result);
        }
    }

    private async Task<string> SendAsync(AlertItem item, string content, string title)
    {
        try
        {
            var url = ReplaceVariables(item.url, item.token, item.variables, content, title);
            var method = item.method.Equals("POST", StringComparison.OrdinalIgnoreCase) ? HttpMethod.Post : HttpMethod.Get;

            var request = new HttpRequestMessage(method, url);

            if (item.headers != null)
            {
                foreach (var (key, value) in item.headers)
                {
                    request.Headers.TryAddWithoutValidation(key, ReplaceVariables(value, item.token, item.variables, content, title));
                }
            }

            if (method == HttpMethod.Post && !string.IsNullOrEmpty(item.body))
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

    private static string ReplaceVariables(string template, string token, Dictionary<string, string> variables, string content, string title)
    {
        var result = template;

        result = result.Replace("{{token}}", token ?? "");
        result = result.Replace("{{content}}", HttpUtility.UrlEncode(content));
        result = result.Replace("{{title}}", HttpUtility.UrlEncode(title));

        if (variables != null)
        {
            foreach (var (key, value) in variables)
            {
                result = result.Replace($"{{{{{key}}}}}", value);
            }
        }

        return result;
    }
}