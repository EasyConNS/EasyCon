#nullable enable

using EasyCon.Core.Config;
using EasyCon.Core.Services;
using System.Threading.Channels;

namespace EasyCon.Core.Notifications;

/// <summary>QQ 通知配置、绑定和后台发送队列。网络发送不会阻塞脚本执行。</summary>
public sealed class QQNotificationService : IAlertService, IDisposable, IAsyncDisposable
{
    private readonly object _settingsLock = new();
    private readonly ILogService? _log;
    private readonly QQNotificationClient _client;
    private readonly QQNotificationClient _sender;
    private readonly Channel<PendingNotification> _queue = Channel.CreateBounded<PendingNotification>(
        new BoundedChannelOptions(20) { FullMode = BoundedChannelFullMode.Wait });
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Task _worker;
    private CancellationTokenSource? _operation;
    private CancellationTokenSource? _sending;
    private QQNotificationSettings _settings;
    private TaskCompletionSource<bool> _idle = CompletedSource();
    private int _pendingCount;
    private int _generation;
    private bool _disposed;

    public event Action<string>? Result;
    public event Action<string>? StatusChanged;
    public event Action<QQBindingCode>? BindingCodeChanged;
    public event Action<string>? Error;

    /// <summary>在 ALERT 触发时读取当前帧，返回 JPEG 字节。无画面时使用内嵌 Logo。</summary>
    public Func<byte[]?>? ImageProvider { get; set; }

    public QQNotificationService(ILogService? log = null, QQNotificationClient? client = null,
        QQNotificationClient? sender = null, QQNotificationSettings? settings = null)
    {
        _log = log;
        _client = client ?? new QQNotificationClient();
        _sender = sender ?? new QQNotificationClient();
        if (ReferenceEquals(_client, _sender))
            throw new ArgumentException("绑定和自动通知必须使用独立的 QQ 客户端。", nameof(sender));
        _settings = settings?.Clone() ?? LoadSettings();
        _worker = Task.Run(ProcessQueueAsync);
    }

    public QQNotificationSettings Settings
    {
        get
        {
            lock (_settingsLock)
                return _settings.Clone();
        }
    }

    public string LastError { get; private set; } = "";

    public bool IsBusy
    {
        get
        {
            lock (_settingsLock)
                return _operation != null;
        }
    }

    public void Update(Action<QQNotificationSettings> update, bool save = true)
    {
        lock (_settingsLock)
        {
            ThrowIfDisposed();
            QQNotificationSettings next = _settings.Clone();
            update(next);
            next.app_id = next.app_id?.Trim() ?? "";
            next.secret = next.secret?.Trim() ?? "";
            next.user_openid = next.user_openid?.Trim() ?? "";
            next.group_openid = next.group_openid?.Trim() ?? "";
            bool credentialsChanged = next.app_id != _settings.app_id || next.secret != _settings.secret;
            if (credentialsChanged && _operation != null)
                throw new InvalidOperationException("请先取消当前操作，再修改 QQ 凭据。");
            if (next.app_id != _settings.app_id)
            {
                next.user_openid = "";
                next.group_openid = "";
            }
            if (credentialsChanged)
            {
                next.enabled = false;
                next.verified = false;
            }
            if (next.enabled && !next.IsReady())
                throw new InvalidOperationException("启用 QQ 通知前，请填写凭据并绑定所有勾选的接收方。");

            if (save)
            {
                next.protected_secret = next.remember_secret && next.secret.Length > 0
                    ? QQNotificationSecretProtector.Protect(next.secret) : "";
                // 先保存成功，再更新内存。写入失败时保持原来的绑定与配置。
                ConfigManager.SaveQqNotification(next);
            }
            if (credentialsChanged || (_settings.enabled && !next.enabled))
            {
                _generation++;
                _sending?.Cancel();
                while (_queue.Reader.TryRead(out _))
                    CompletePending();
            }
            _settings = next;
        }
    }

    public async Task VerifyAsync(CancellationToken cancellationToken = default)
    {
        await RunOperationAsync(async (settings, token) =>
        {
            lock (_settingsLock)
                _settings.verified = false;
            StatusChanged?.Invoke("正在验证 QQ 机器人凭据…");
            await _client.VerifyCredentialsAsync(token).ConfigureAwait(false);
            lock (_settingsLock)
                _settings.verified = true;
            LastError = "";
            StatusChanged?.Invoke("凭据验证成功，可以继续绑定接收方。");
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<string> BindAsync(QQNotificationTargetKind kind, CancellationToken cancellationToken = default)
    {
        string openId = "";
        await RunOperationAsync(async (settings, token) =>
        {
            if (!settings.verified)
                throw new InvalidOperationException("请先验证 QQ 凭据。");
            StatusChanged?.Invoke("正在连接 QQ 网关…");
            int generation = 0;
            openId = await _client.BindAsync(kind, code =>
            {
                BindingCodeChanged?.Invoke(code);
                if (code.Generation != generation)
                {
                    generation = code.Generation;
                    string instruction = kind == QQNotificationTargetKind.User ? "私聊机器人" : "在目标群 @机器人";
                    StatusChanged?.Invoke($"请{instruction}发送当前绑定码：{code.Code}（60 秒后自动更新）");
                }
            }, token).ConfigureAwait(false);
            Update(next =>
            {
                if (kind == QQNotificationTargetKind.User)
                {
                    next.user_openid = openId;
                    next.user_enabled = true;
                }
                else
                {
                    next.group_openid = openId;
                    next.group_enabled = true;
                }
            });
            LastError = "";
            StatusChanged?.Invoke(kind == QQNotificationTargetKind.User ? "QQ 私聊绑定成功。" : "QQ 群聊绑定成功。");
        }, cancellationToken).ConfigureAwait(false);
        return openId;
    }

    public async Task SendTestAsync(CancellationToken cancellationToken = default)
    {
        await RunOperationAsync(async (settings, token) =>
        {
            IReadOnlyList<QQNotificationTarget> targets = settings.Targets();
            byte[] image = GetImage(null);
            StatusChanged?.Invoke("正在发送 QQ 图文测试…");
            await _client.SendAsync(targets,
                "QQ 通知测试：请确认同时收到这条文字和测试图片。",
                image, token).ConfigureAwait(false);
            LastError = "";
            StatusChanged?.Invoke("图文测试发送成功，请在 QQ 中确认文字和图片均已收到。");
        }, cancellationToken).ConfigureAwait(false);
    }

    public void CancelCurrent()
    {
        lock (_settingsLock)
            _operation?.Cancel();
    }

    public void Dispatch(string content, string title = "伊机控消息", byte[]? image = null)
    {
        QQNotificationSettings settings;
        int generation;
        lock (_settingsLock)
        {
            if (_disposed || !_settings.enabled)
                return;
            settings = _settings.Clone();
            generation = _generation;
        }
        try
        {
            IReadOnlyList<QQNotificationTarget> targets = settings.Targets();
            if (!settings.IsReady())
                throw new InvalidOperationException("QQ 通知凭据不完整，请重新填写。");
            image = settings.attach_image ? GetImage(image).ToArray() : null;
            string text = string.IsNullOrWhiteSpace(title) ? content : title + "\n" + content;
            lock (_settingsLock)
            {
                if (_disposed || generation != _generation || !_settings.enabled)
                    return;
                PendingNotification pending = new(settings.app_id, settings.secret, targets, text, image, generation);
                if (!_queue.Writer.TryWrite(pending))
                    throw new InvalidOperationException("待发送通知过多，当前通知未加入队列。");
                if (_pendingCount++ == 0)
                    _idle = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            }
        }
        catch (Exception ex)
        {
            ReportError("QQ 通知未发送：" + ex.Message);
        }
    }

    /// <summary>CLI 在脚本退出后等待已排队的通知；脚本本身不等待网络。</summary>
    public Task FlushAsync(CancellationToken cancellationToken = default)
    {
        lock (_settingsLock)
            return _idle.Task.WaitAsync(cancellationToken);
    }

    private byte[] GetImage(byte[]? supplied)
    {
        if (supplied is { Length: > 0 })
            return supplied;
        byte[]? image = ImageProvider?.Invoke();
        return image is { Length: > 0 } ? image : NotificationImage.FromFrame(null);
    }

    private async Task ProcessQueueAsync()
    {
        try
        {
            await foreach (PendingNotification pending in _queue.Reader.ReadAllAsync(_shutdown.Token).ConfigureAwait(false))
            {
                CancellationTokenSource? sending = null;
                try
                {
                    lock (_settingsLock)
                    {
                        if (_disposed || pending.Generation != _generation)
                            continue;
                        sending = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token);
                        _sending = sending;
                    }
                    _sender.Configure(pending.AppId, pending.Secret);
                    await _sender.SendAsync(pending.Targets, pending.Content, pending.Image, sending.Token).ConfigureAwait(false);
                    LastError = "";
                    ReportResult("QQ 通知发送成功。");
                }
                catch (OperationCanceledException) when (sending?.IsCancellationRequested == true)
                {
                    if (!_disposed)
                        ReportResult("QQ 待发送通知已取消。已提交的消息请在 QQ 中核对。");
                }
                catch (Exception ex)
                {
                    ReportError("QQ 通知发送失败：" + ex.Message);
                }
                finally
                {
                    lock (_settingsLock)
                    {
                        if (ReferenceEquals(_sending, sending))
                            _sending = null;
                        CompletePending();
                    }
                    sending?.Dispose();
                }
            }
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
        }
        finally
        {
            lock (_settingsLock)
            {
                while (_queue.Reader.TryRead(out _))
                    CompletePending();
            }
        }
    }

    private async Task RunOperationAsync(Func<QQNotificationSettings, CancellationToken, Task> operation,
        CancellationToken cancellationToken)
    {
        CancellationTokenSource operationCts;
        QQNotificationSettings settings;
        lock (_settingsLock)
        {
            ThrowIfDisposed();
            if (_operation != null)
                throw new InvalidOperationException("已有 QQ 操作正在进行，请先等待或取消。");
            operationCts = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token, cancellationToken);
            _operation = operationCts;
            settings = _settings.Clone();
        }
        try
        {
            _client.Configure(settings.app_id, settings.secret);
            await operation(settings, operationCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            StatusChanged?.Invoke("QQ 通知操作已取消。已提交的消息请在 QQ 中核对。");
            throw;
        }
        catch (Exception ex)
        {
            ReportError(ex.Message);
            throw;
        }
        finally
        {
            lock (_settingsLock)
                _operation = null;
            operationCts.Dispose();
        }
    }

    private QQNotificationSettings LoadSettings()
    {
        QQNotificationSettings settings = ConfigManager.LoadQqNotification();
        if (settings.remember_secret && !string.IsNullOrWhiteSpace(settings.protected_secret))
        {
            try
            {
                settings.secret = QQNotificationSecretProtector.Unprotect(settings.protected_secret);
            }
            catch
            {
                settings.secret = "";
                settings.enabled = false;
                LastError = "保存的 QQ AppSecret 无法解密，请重新填写。";
            }
        }
        if (!settings.IsReady())
            settings.enabled = false;
        return settings;
    }

    private void CompletePending()
    {
        if (--_pendingCount == 0)
            _idle.TrySetResult(true);
    }

    private static TaskCompletionSource<bool> CompletedSource()
    {
        TaskCompletionSource<bool> source = new(TaskCreationOptions.RunContinuationsAsynchronously);
        source.SetResult(true);
        return source;
    }

    private void ReportResult(string message)
    {
        if (_disposed)
            return;
        Result?.Invoke(message);
        _log?.AddLog(message);
    }

    private void ReportError(string message)
    {
        if (_disposed)
            return;
        LastError = message;
        Error?.Invoke(message);
        _log?.AddLog(message);
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    public void Dispose()
    {
        lock (_settingsLock)
        {
            if (_disposed)
                return;
            _disposed = true;
            _shutdown.Cancel();
            _operation?.Cancel();
            _queue.Writer.TryComplete();
        }
        _client.Dispose();
        _ = _worker.ContinueWith(_ =>
        {
            _sender.Dispose();
            _shutdown.Dispose();
        }, TaskScheduler.Default);
    }

    public async ValueTask DisposeAsync()
    {
        Dispose();
        await _worker.ConfigureAwait(false);
    }

    private sealed record PendingNotification(
        string AppId, string Secret, IReadOnlyList<QQNotificationTarget> Targets,
        string Content, byte[]? Image, int Generation);
}