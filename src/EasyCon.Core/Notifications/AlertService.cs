#nullable enable

using EasyCon.Core.Config;
using EasyCon.Core.Services;

namespace EasyCon.Core.Notifications;

/// <summary>统一桌面提醒入口：通用 Webhook 与 QQ 图片通知均在后台发送。</summary>
public sealed class AlertService : IAlertService, IDisposable
{
    private readonly object _sync = new();
    private readonly Action<string>? _resultLogger;
    private readonly AlertDispatcher _dispatcher;
    private readonly HashSet<Task> _pending = [];
    private readonly CancellationTokenSource _shutdown = new();
    private readonly CancellationToken _shutdownToken;
    private bool _disposed;

    public AlertService(Action<string>? resultLogger = null, Func<byte[]?>? imageProvider = null,
        AlertDispatcher? dispatcher = null)
    {
        _resultLogger = resultLogger;
        _shutdownToken = _shutdown.Token;
        _dispatcher = dispatcher ?? new AlertDispatcher(ConfigManager.LoadAlert());
        _dispatcher.ImageProvider = imageProvider;
        _dispatcher.OnResult += OnResult;
        ConfigManager.AlertConfigChanged += OnConfigurationChanged;
    }

    public void Dispatch(string content, string title = "伊机控消息", byte[]? image = null)
    {
        lock (_sync)
        {
            if (_disposed)
                return;
            Task task = DispatchAsync(content, title, image);
            _pending.Add(task);
            _ = task.ContinueWith(completed =>
            {
                lock (_sync)
                    _pending.Remove(completed);
            }, TaskScheduler.Default);
        }
    }

    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        Task[] pending;
        lock (_sync)
            pending = [.. _pending];
        await Task.WhenAll(pending).WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task DispatchAsync(string content, string title, byte[]? image)
    {
        try
        {
            await _dispatcher.DispatchAsync(content, title, _shutdownToken, image).ConfigureAwait(false);
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

    private void OnResult(object? sender, string result) => _resultLogger?.Invoke(result);

    private void OnConfigurationChanged(AlertConfig config)
    {
        lock (_sync)
        {
            if (_disposed)
                return;
            try
            {
                _dispatcher.UpdateConfiguration(config);
            }
            catch (Exception ex)
            {
                _resultLogger?.Invoke("推送配置更新失败：" + ex.Message);
            }
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
                return;
            _disposed = true;
            _shutdown.Cancel();
            ConfigManager.AlertConfigChanged -= OnConfigurationChanged;
            _dispatcher.OnResult -= OnResult;
            _dispatcher.Dispose();
            _ = Task.WhenAll(_pending).ContinueWith(_ => _shutdown.Dispose(), TaskScheduler.Default);
        }
    }
}