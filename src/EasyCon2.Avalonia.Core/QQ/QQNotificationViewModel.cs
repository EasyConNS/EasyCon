using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EasyCon.Core.Config;
using EasyCon.Core.Notifications;

namespace EasyCon2.Avalonia.Core.QQ;

public partial class QQNotificationViewModel : ObservableObject, IDisposable
{
    private readonly QQNotificationService _service;
    private bool _suppressChanges;
    private bool _disposed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanVerify))]
    [NotifyCanExecuteChangedFor(nameof(VerifyCommand))]
    private string _appId = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanVerify))]
    [NotifyCanExecuteChangedFor(nameof(VerifyCommand))]
    private string _secret = "";

    [ObservableProperty] private bool _rememberSecret;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UserOpenIdDisplay))]
    private string _userOpenId = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GroupOpenIdDisplay))]
    private string _groupOpenId = "";

    [ObservableProperty] private bool _userEnabled = true;
    [ObservableProperty] private bool _groupEnabled;
    [ObservableProperty] private bool _enabled;
    [ObservableProperty] private bool _attachImage = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanBind), nameof(VerificationDisplay))]
    [NotifyCanExecuteChangedFor(nameof(BindUserCommand), nameof(BindGroupCommand), nameof(SendTestCommand))]
    private bool _verified;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEdit), nameof(CanVerify), nameof(CanBind), nameof(CanRememberSecret))]
    [NotifyCanExecuteChangedFor(nameof(VerifyCommand), nameof(BindUserCommand), nameof(BindGroupCommand),
        nameof(SendTestCommand), nameof(SaveCommand), nameof(CancelCommand))]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBindingCode))]
    private string _bindingCode = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BindingCountdownDisplay))]
    private int _bindingSeconds;

    [ObservableProperty] private string _feedback = "";
    [ObservableProperty] private bool _feedbackIsError;

    public QQNotificationViewModel(QQNotificationService service)
    {
        _service = service;
        Load(service.Settings);
        if (!string.IsNullOrWhiteSpace(service.LastError))
            ShowError(service.LastError);
        _service.StatusChanged += OnStatusChanged;
        _service.BindingCodeChanged += OnBindingCodeChanged;
        _service.Error += OnError;
    }

    public string BindingCountdownDisplay => BindingSeconds > 0 ? $"剩余 {BindingSeconds} 秒" : "正在更新绑定码…";
    public string VerificationDisplay => Verified ? "凭据已验证" : "尚未验证凭据";
    public string UserOpenIdDisplay => UserOpenId.Length > 0 ? "OpenID：" + UserOpenId : "尚未绑定私聊";
    public string GroupOpenIdDisplay => GroupOpenId.Length > 0 ? "OpenID：" + GroupOpenId : "尚未绑定群聊";
    public bool HasBindingCode => BindingCode.Length > 0;
    public bool CanEdit => !IsBusy;
    public bool CanVerify => CanEdit && !string.IsNullOrWhiteSpace(AppId) && !string.IsNullOrWhiteSpace(Secret);
    public bool CanBind => Verified && CanEdit;
    public bool CanRememberSecret => OperatingSystem.IsWindows() && CanEdit;

    partial void OnAppIdChanged(string value)
    {
        if (_suppressChanges)
            return;
        Verified = false;
        Enabled = false;
        UserOpenId = "";
        GroupOpenId = "";
    }

    partial void OnSecretChanged(string value)
    {
        if (_suppressChanges)
            return;
        Verified = false;
        Enabled = false;
    }

    [RelayCommand(CanExecute = nameof(CanVerify))]
    private async Task VerifyAsync()
    {
        try
        {
            SaveCore();
            IsBusy = true;
            await _service.VerifyAsync();
            Load(_service.Settings);
        }
        catch (OperationCanceledException)
        {
            ShowMessage("操作已取消。");
        }
        catch (Exception ex)
        {
            Verified = false;
            ShowError(ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanBind))]
    private async Task BindUserAsync() => await BindAsync(QQNotificationTargetKind.User);

    [RelayCommand(CanExecute = nameof(CanBind))]
    private async Task BindGroupAsync() => await BindAsync(QQNotificationTargetKind.Group);

    [RelayCommand(CanExecute = nameof(CanBind))]
    private async Task SendTestAsync()
    {
        try
        {
            SaveCore();
            IsBusy = true;
            await _service.SendTestAsync();
            ShowMessage("图文测试发送成功，请在 QQ 中确认文字和图片均已收到。");
        }
        catch (OperationCanceledException)
        {
            ShowMessage("操作已取消。已提交的消息请在 QQ 中核对。");
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void Save()
    {
        try
        {
            SaveCore();
            ShowMessage("QQ 通知配置已保存。");
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
    }

    [RelayCommand(CanExecute = nameof(IsBusy))]
    private void Cancel() => _service.CancelCurrent();

    private async Task BindAsync(QQNotificationTargetKind kind)
    {
        try
        {
            SaveCore();
            IsBusy = true;
            await _service.BindAsync(kind);
            Load(_service.Settings);
            ShowMessage(kind == QQNotificationTargetKind.User ? "QQ 私聊绑定成功。" : "QQ 群聊绑定成功。");
        }
        catch (OperationCanceledException)
        {
            ShowMessage("绑定已取消。");
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
        finally
        {
            IsBusy = false;
            BindingCode = "";
            BindingSeconds = 0;
        }
    }

    private void SaveCore()
    {
        _service.Update(settings =>
        {
            settings.app_id = AppId;
            settings.secret = Secret;
            settings.remember_secret = RememberSecret;
            settings.user_openid = UserOpenId;
            settings.group_openid = GroupOpenId;
            settings.user_enabled = UserEnabled;
            settings.group_enabled = GroupEnabled;
            settings.enabled = Enabled;
            settings.attach_image = AttachImage;
        });
        Load(_service.Settings);
    }

    private void Load(QQNotificationSettings settings)
    {
        if (_disposed)
            return;
        _suppressChanges = true;
        try
        {
            AppId = settings.app_id;
            Secret = settings.secret;
            RememberSecret = settings.remember_secret;
            UserOpenId = settings.user_openid;
            GroupOpenId = settings.group_openid;
            UserEnabled = settings.user_enabled;
            GroupEnabled = settings.group_enabled;
            Enabled = settings.enabled;
            AttachImage = settings.attach_image;
            Verified = settings.verified;
        }
        finally
        {
            _suppressChanges = false;
        }
    }

    private void OnStatusChanged(string message) => Post(() => ShowMessage(message));
    private void OnError(string message) => Post(() => ShowError(message));

    private void OnBindingCodeChanged(QQBindingCode code) => Post(() =>
    {
        if (IsBusy)
        {
            BindingCode = code.Code;
            BindingSeconds = code.Seconds;
        }
    });

    private void ShowMessage(string message)
    {
        Feedback = message;
        FeedbackIsError = false;
    }

    private void ShowError(string message)
    {
        Feedback = message;
        FeedbackIsError = true;
    }

    private void Post(Action action)
    {
        void Apply()
        {
            if (!_disposed)
                action();
        }
        if (Dispatcher.UIThread.CheckAccess())
            Apply();
        else
            Dispatcher.UIThread.Post(Apply);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        if (IsBusy)
            _service.CancelCurrent();
        _service.StatusChanged -= OnStatusChanged;
        _service.BindingCodeChanged -= OnBindingCodeChanged;
        _service.Error -= OnError;
    }
}