using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EasyCon.Core.Config;
using System.Collections.ObjectModel;
using AlertConfigType = EasyCon.Core.Config.AlertConfig;

namespace EasyCon2.Avalonia.Core.AlertConfig;

public partial class AlertConfigViewModel : ObservableObject
{
    private const int MaxVisibleItems = 5;
    private List<AlertItemViewModel> _allViewModels = [];

    [ObservableProperty]
    private ObservableCollection<AlertItemViewModel> visibleItems = [];

    [ObservableProperty]
    private int hiddenCount;

    [ObservableProperty]
    private bool hasHiddenItems;

    [ObservableProperty]
    private bool canAdd;

    [ObservableProperty]
    private string _errorMessage = "";

    [ObservableProperty]
    private bool _hasError;

    public Action? OnSaveCallback { get; set; }

    public void Load()
    {
        var config = ConfigManager.LoadAlert();
        _allViewModels = [.. config.alerts.Select(CreateViewModel)];
        RefreshVisibleItems();
    }

    public bool Save()
    {
        if (!Validate(out var error))
        {
            ErrorMessage = error;
            HasError = true;
            return false;
        }

        var config = new AlertConfigType
        {
            timeout = 10,
            alerts = [.. _allViewModels.Select(vm => vm.ToAlertItem())]
        };
        ConfigManager.SaveAlert(config);
        OnSaveCallback?.Invoke();
        return true;
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
    private void AddItem()
    {
        if (VisibleItems.Count >= MaxVisibleItems) return;

        var vm = CreateViewModel(new AlertItem { name = "" });
        _allViewModels.Add(vm);
        VisibleItems.Add(vm);
        UpdateHiddenState();
        ClearError();
    }

    private void OnItemDeleteRequested(AlertItemViewModel item)
    {
        var visIdx = VisibleItems.IndexOf(item);
        if (visIdx < 0) return;

        _allViewModels.Remove(item);
        VisibleItems.RemoveAt(visIdx);

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
        return vm;
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
    }
}