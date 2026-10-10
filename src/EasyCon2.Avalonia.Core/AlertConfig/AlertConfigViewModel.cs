using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EasyCon.Core.Config;
using System.Collections.ObjectModel;
using System.ComponentModel;
using AlertConfigType = EasyCon.Core.Config.AlertConfig;

namespace EasyCon2.Avalonia.Core.AlertConfig;

public partial class AlertConfigViewModel : ObservableObject, IDisposable
{
    private const int MaxVisibleItems = 5;
    private List<AlertItemViewModel> _allViewModels = [];
    private readonly Action<AlertConfigType> _saveConfiguration;
    private int _timeout = 10;

    public AlertConfigViewModel(Action<AlertConfigType>? saveConfiguration = null)
    {
        _saveConfiguration = saveConfiguration ?? (config => ConfigManager.SaveAlert(config));
    }

    public bool CanSave => _allViewModels.All(item => item.CanEdit);

    [ObservableProperty]
    private ObservableCollection<AlertItemViewModel> visibleItems = [];

    [ObservableProperty]
    private int hiddenCount;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ShowAllItemsCommand))]
    private bool hasHiddenItems;

    [ObservableProperty]
    private bool canAdd;

    [ObservableProperty]
    private string _errorMessage = "";

    [ObservableProperty]
    private bool _hasError;

    public Action? OnSaveCallback { get; set; }

    public void Load(AlertConfigType? config = null)
    {
        Dispose();
        config ??= ConfigManager.LoadAlert();
        _timeout = config.timeout;
        _allViewModels = [.. config.alerts.Select(CreateViewModel)];
        RefreshVisibleItems();
        ErrorMessage = config.load_error;
        HasError = ErrorMessage.Length > 0;
    }

    public bool Save()
    {
        if (!Validate(out var error))
        {
            ErrorMessage = error;
            HasError = true;
            return false;
        }

        try
        {
            var config = new AlertConfigType
            {
                schema_version = 1,
                timeout = _timeout,
                alerts = [.. _allViewModels.Select(vm => vm.ToAlertItem())]
            };
            _saveConfiguration(config);
            OnSaveCallback?.Invoke();
            ClearError();
            return true;
        }
        catch (Exception ex)
        {
            ErrorMessage = "推送配置保存失败：" + ex.Message;
            HasError = true;
            return false;
        }
    }

    private bool Validate(out string error)
    {
        for (var i = 0; i < _allViewModels.Count; i++)
        {
            var vm = _allViewModels[i];
            var label = _allViewModels.Count > 1 ? $"第 {i + 1} 个推送" : "推送";

            if (string.IsNullOrWhiteSpace(vm.Name))
            {
                error = $"{label}：名称不能为空";
                return false;
            }

            if (!vm.CanEdit)
            {
                error = $"{label}：请先等待 QQ 操作完成，或取消操作。";
                return false;
            }

            if (vm.IsQq)
            {
                if (vm.Enable && !vm.Qq!.GetSettings().IsReady())
                {
                    error = $"{label}：请填写 QQ 凭据并绑定所有勾选的接收方。";
                    return false;
                }
                continue;
            }

            if (string.IsNullOrWhiteSpace(vm.Url))
            {
                error = $"{label}：URL 不能为空";
                return false;
            }
        }

        error = "";
        return true;
    }

    [RelayCommand]
    private void AddItem(string? provider)
    {
        if (VisibleItems.Count >= MaxVisibleItems) return;

        AlertItem item = provider switch
        {
            AlertItem.QqProvider => AlertItem.CreateQq(),
            "pushplus" => ConfigManager.CreateDefaultAlert().alerts[0],
            "bark" => ConfigManager.CreateDefaultAlert().alerts[1],
            _ => new AlertItem { name = "" },
        };
        var vm = CreateViewModel(item);
        vm.IsExpanded = true;
        _allViewModels.Add(vm);
        VisibleItems.Add(vm);
        UpdateHiddenState();
        ClearError();
    }

    [RelayCommand(CanExecute = nameof(HasHiddenItems))]
    private void ShowAllItems()
    {
        VisibleItems = new ObservableCollection<AlertItemViewModel>(_allViewModels);
        UpdateHiddenState();
    }

    private void OnItemDeleteRequested(AlertItemViewModel item)
    {
        var visIdx = VisibleItems.IndexOf(item);
        if (visIdx < 0) return;

        _allViewModels.Remove(item);
        VisibleItems.RemoveAt(visIdx);
        item.PropertyChanged -= OnItemPropertyChanged;
        item.Dispose();

        if (_allViewModels.Count > VisibleItems.Count)
        {
            var next = _allViewModels[VisibleItems.Count];
            if (!VisibleItems.Contains(next))
                VisibleItems.Add(next);
        }

        UpdateHiddenState();
        ClearError();
    }

    private void ClearError()
    {
        ErrorMessage = "";
        HasError = false;
    }

    private AlertItemViewModel CreateViewModel(AlertItem item)
    {
        var vm = new AlertItemViewModel(item);
        vm.RequestDelete = OnItemDeleteRequested;
        vm.PropertyChanged += OnItemPropertyChanged;
        return vm;
    }

    private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AlertItemViewModel.CanEdit))
            OnPropertyChanged(nameof(CanSave));
    }

    private void RefreshVisibleItems()
    {
        VisibleItems = new ObservableCollection<AlertItemViewModel>(
            _allViewModels.Take(MaxVisibleItems));
        UpdateHiddenState();
    }

    private void UpdateHiddenState()
    {
        HiddenCount = Math.Max(0, _allViewModels.Count - VisibleItems.Count);
        HasHiddenItems = HiddenCount > 0;
        CanAdd = VisibleItems.Count < MaxVisibleItems;
        OnPropertyChanged(nameof(CanSave));
    }

    public void Dispose()
    {
        foreach (AlertItemViewModel item in _allViewModels)
        {
            item.PropertyChanged -= OnItemPropertyChanged;
            item.Dispose();
        }
        _allViewModels.Clear();
    }
}