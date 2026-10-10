#nullable enable

using EasyCon.Core.Config;
using EasyCon.Core.Services;

namespace EasyCon.Core.Notifications;

/// <summary>统一桌面提醒入口：通用 Webhook 与 QQ 图片通知均在后台发送。</summary>
public sealed class AlertService : IAlertService, IDisposable
{
    private readonly object _sync = new();
    private readonly Action<string>? _resultLogger;
    private readonly QQNotificationService _qq;
    private readonly HashSet<Task> _webhooks = [];
    private readonly CancellationTokenSource _shutdown = new();
    private readonly CancellationToken _shutdownToken;
    private bool _disposed;

    public AlertService(Action<string>? resultLogger = null, Func<byte[]?>? imageProvider = null,
        QQNotificationService? qq = null)
    {
        _resultLogger = resultLogger;
        _shutdownToken = _shutdown.Token;
        _qq = qq ?? new QQNotificationService();
        _qq.ImageProvider = imageProvider;
        _qq.Result += OnQqResult;
        _qq.Error += OnQqResult;
    }

    public void Dispatch(string content, string title = "伊机控消息", byte[]? image = null)
    {
        lock (_sync)
        {
            if (_disposed)
                return;
            _qq.Dispatch(content, title, image);
            Task task = Task.Run(() => DispatchWebhooksAsync(content, title));
            _webhooks.Add(task);
            _ = task.ContinueWith(completed =>
            {
                lock (_sync)
                    _webhooks.Remove(completed);
            }, TaskScheduler.Default);
        }
    }

    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        Task[] webhooks;
        lock (_sync)
            webhooks = [.. _webhooks];
        await Task.WhenAll(_qq.FlushAsync(cancellationToken),
            Task.WhenAll(webhooks).WaitAsync(cancellationToken)).ConfigureAwait(false);
    }

    private async Task DispatchWebhooksAsync(string content, string title)
    {
        try
        {
            using AlertDispatcher dispatcher = new(ConfigManager.LoadAlert());
            dispatcher.OnResult += (_, result) => _resultLogger?.Invoke(result);
            await dispatcher.DispatchAsync(content, title, _shutdownToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_shutdownToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            if (!_disposed)
                _resultLogger?.Invoke("推送失败：" + ex.Message);
        }
    }

    private void OnQqResult(string result) => _resultLogger?.Invoke(result);

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
                return;
            _disposed = true;
            _shutdown.Cancel();
            _qq.Result -= OnQqResult;
            _qq.Error -= OnQqResult;
            _qq.Dispose();
            _ = Task.WhenAll(_webhooks).ContinueWith(_ => _shutdown.Dispose(), TaskScheduler.Default);
        }
    }
}