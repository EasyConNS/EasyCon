using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EasyCon.Core.Config;
using EasyCon.Core.Notifications;
using EasyCon2.Avalonia.QQ;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace EasyCon2.Avalonia.AlertConfig;

public partial class AlertItemViewModel : ObservableObject, IDisposable
{
    public static readonly string[] HttpMethods = ["GET", "POST", "PUT", "HEAD", "OPTIONS"];

    private readonly AlertItem _item;
    private readonly QQNotificationService? _qqService;

    public bool IsQq => _item.IsQq;
    public QQNotificationViewModel? Qq { get; }
    public bool CanEdit => Qq?.CanEdit ?? true;

    public AlertItemViewModel(AlertItem item)
    {
        _item = item;
        Name = item.name;
        Enable = item.enable;
        Method = item.method;
        Url = item.url;
        Token = item.token;
        Body = item.body;

        Headers = new ObservableCollection<KeyValueEntry>(
            item.headers?.Select(kv => new KeyValueEntry(kv.Key, kv.Value)) ?? []);

        Variables = new ObservableCollection<KeyValueEntry>(
            item.variables?.Select(kv => new KeyValueEntry(kv.Key, kv.Value)) ?? []);

        if (IsQq)
        {
            QQNotificationSettings settings = item.qq?.Clone() ?? new();
            settings.enabled = item.enable;
            _qqService = new QQNotificationService(settings: settings, persistSettings: false);
            Qq = new QQNotificationViewModel(_qqService);
            Qq.PropertyChanged += OnQqPropertyChanged;
        }
    }

    public Action<AlertItemViewModel>? RequestDelete { get; set; }

    [ObservableProperty]
    private string name = "";

    [ObservableProperty]
    private bool enable;

    partial void OnEnableChanged(bool value)
    {
        if (Qq != null)
            Qq.Enabled = value;
    }

    private void OnQqPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(QQNotificationViewModel.Enabled))
            Enable = Qq!.Enabled;
        if (e.PropertyName == nameof(QQNotificationViewModel.CanEdit))
            OnPropertyChanged(nameof(CanEdit));
    }

    [ObservableProperty]
    private string method = "GET";

    [ObservableProperty]
    private string url = "";

    [ObservableProperty]
    private string token = "";

    [ObservableProperty]
    private string body = "";

    [ObservableProperty]
    private ObservableCollection<KeyValueEntry> headers = [];

    [ObservableProperty]
    private ObservableCollection<KeyValueEntry> variables = [];

    [ObservableProperty]
    private bool isExpanded;

    [RelayCommand]
    private void ToggleExpand() => IsExpanded = !IsExpanded;

    [RelayCommand]
    private void Delete() => RequestDelete?.Invoke(this);

    [RelayCommand]
    private void AddHeader() => Headers.Add(new KeyValueEntry());

    [RelayCommand]
    private void AddVariable() => Variables.Add(new KeyValueEntry());

    [RelayCommand]
    private void RemoveHeader(KeyValueEntry entry) => Headers.Remove(entry);

    [RelayCommand]
    private void RemoveVariable(KeyValueEntry entry) => Variables.Remove(entry);

    public AlertItem ToAlertItem()
    {
        var headersDict = Headers
            .Where(kv => !string.IsNullOrWhiteSpace(kv.Key) && !string.IsNullOrWhiteSpace(kv.Value))
            .ToDictionary(kv => kv.Key, kv => kv.Value);

        var variablesDict = Variables
            .Where(kv => !string.IsNullOrWhiteSpace(kv.Key) && !string.IsNullOrWhiteSpace(kv.Value))
            .ToDictionary(kv => kv.Key, kv => kv.Value);

        return new AlertItem
        {
            id = _item.id,
            provider = _item.provider,
            name = Name,
            enable = Enable,
            method = Method,
            url = Url,
            token = Token,
            body = Body,
            headers = headersDict.Count > 0 ? headersDict : null,
            variables = variablesDict.Count > 0 ? variablesDict : null,
            qq = Qq?.GetSettings(),
        };
    }

    public void Dispose()
    {
        if (Qq != null)
        {
            Qq.PropertyChanged -= OnQqPropertyChanged;
            Qq.Dispose();
        }
        _qqService?.Dispose();
    }
}