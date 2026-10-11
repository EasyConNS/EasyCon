using EasyCon.Core.Notifications;
using System.Text;
using System.Text.Json;
using System.Web;

namespace EasyCon.Core.Config;

public class AlertDispatcher : IDisposable
{
    private readonly object _sync = new();
    private List<AlertItem> _items = [];
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
    private readonly Func<QQNotificationSettings, QQNotificationService> _qqFactory;
    private readonly Dictionary<string, QqChannel> _qqChannels = [];
    private bool _disposed;

    public Func<byte[]?>? ImageProvider { get; set; }

    public event EventHandler<string> OnResult;

    public AlertDispatcher(AlertConfig config, HttpClient? http = null,
        Func<QQNotificationSettings, QQNotificationService>? qqFactory = null)
    {
        _ownsHttp = http == null;
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(config.timeout) };
        _qqFactory = qqFactory ?? (settings => new QQNotificationService(settings: settings, persistSettings: false));
        UpdateConfiguration(config);
    }

    public void UpdateConfiguration(AlertConfig config)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _items = [.. config.alerts.Select(item => item.Clone())];
            HashSet<string> activeIds = [];
            foreach (AlertItem item in _items.Where(item => item.IsQq))
            {
                if (item.id.Length == 0 || !activeIds.Add(item.id))
                    throw new InvalidOperationException("QQ 推送配置缺少唯一标识。");
                QQNotificationSettings settings = item.qq ?? new();
                settings.enabled = item.enable;
                if (_qqChannels.TryGetValue(item.id, out QqChannel? existing))
                {
                    QQNotificationSettings previous = existing.Service.Settings;
                    if (previous.app_id == settings.app_id && previous.secret == settings.secret)
                    {
                        existing.Name = item.name;
                        existing.Service.Update(next =>
                        {
                            next.enabled = settings.enabled;
                            next.attach_image = settings.attach_image;
                            next.user_enabled = settings.user_enabled;
                            next.group_enabled = settings.group_enabled;
                            next.user_openid = settings.user_openid;
                            next.group_openid = settings.group_openid;
                        }, save: false);
                        continue;
                    }
                    existing.Service.Dispose();
                }
                QqChannel channel = new(item.name, _qqFactory(settings));
                channel.Service.Result += result => OnResult?.Invoke(this, $"[{channel.Name}] {result}");
                channel.Service.Error += result => OnResult?.Invoke(this, $"[{channel.Name}] {result}");
                _qqChannels[item.id] = channel;
            }
            foreach (string id in _qqChannels.Keys.Where(id => !activeIds.Contains(id)).ToArray())
            {
                _qqChannels[id].Service.Dispose();
                _qqChannels.Remove(id);
            }
        }
    }

    public async Task DispatchAsync(string content, string title = "伊机控消息", CancellationToken cancellationToken = default,
        byte[]? image = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        List<Task<string>> tasks = [];
        lock (_sync)
        {
            if (_disposed)
                return;
            foreach (AlertItem item in _items.Where(item => item.enable))
            {
                if (item.IsQq)
                {
                    QQNotificationService service = _qqChannels[item.id].Service;
                    service.ImageProvider = ImageProvider;
                    tasks.Add(WaitForQqAsync(service.DispatchAsync(content, title, image, cancellationToken)));
                }
                else
                {
                    tasks.Add(SendAsync(item, content, title, cancellationToken));
                }
            }
        }
        string[] results = await Task.WhenAll(tasks).ConfigureAwait(false);
        foreach (string result in results.Where(result => result.Length > 0))
            OnResult?.Invoke(this, result);
    }

    private static async Task<string> WaitForQqAsync(Task dispatch)
    {
        await dispatch.ConfigureAwait(false);
        return "";
    }

    private async Task<string> SendAsync(AlertItem item, string content, string title, CancellationToken cancellationToken)
    {
        try
        {
            var url = ReplaceVariables(item.url, item.token, item.variables, content, title);
            var method = item.method.Equals("POST", StringComparison.OrdinalIgnoreCase) ? HttpMethod.Post : HttpMethod.Get;

            using var request = new HttpRequestMessage(method, url);

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

            using var response = await _http.SendAsync(request, cancellationToken);
            var respBody = await response.Content.ReadAsStringAsync(cancellationToken);

            if (response.IsSuccessStatusCode)
                return $"[{item.name}] 推送成功";
            else
                return $"[{item.name}] 推送失败: HTTP {(int)response.StatusCode} - {respBody}";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
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

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
                return;
            _disposed = true;
            foreach (QqChannel channel in _qqChannels.Values)
                channel.Service.Dispose();
            _qqChannels.Clear();
            if (_ownsHttp)
                _http.Dispose();
        }
    }

    private sealed class QqChannel(string name, QQNotificationService service)
    {
        public string Name { get; set; } = name;
        public QQNotificationService Service { get; } = service;
    }
}