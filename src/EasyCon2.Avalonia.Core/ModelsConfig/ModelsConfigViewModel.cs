using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EasyCon.Core.Config;
using EasyCon.Core.LLM.Models;
using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using ModelsConfigType = EasyCon.Core.LLM.Models.ModelsConfig;

namespace EasyCon2.Avalonia.Core.ModelsConfig;

public partial class ModelsConfigViewModel : ObservableObject
{
    public ObservableCollection<ProviderItemViewModel> Providers { get; } = [];

    [ObservableProperty]
    private string _errorMessage = "";

    [ObservableProperty]
    private bool _hasError;

    public Action? OnSaveCallback { get; set; }

    public void Load()
    {
        var config = ConfigManager.LoadModelsConfig();
        Providers.Clear();
        foreach (var (key, provider) in config.Models.Providers)
        {
            var vm = new ProviderItemViewModel(key, provider) { RequestDelete = OnProviderDeleteRequested };
            Providers.Add(vm);
        }
    }

    public bool Save()
    {
        if (!Validate(out var error))
        {
            ErrorMessage = error;
            HasError = true;
            return false;
        }

        var config = new ModelsConfigType();
        var usedKeys = new HashSet<string>(StringComparer.Ordinal);

        foreach (var vm in Providers)
        {
            var key = ResolveKey(vm, usedKeys);
            config.Models.Providers[key] = vm.ToProviderConfig();
        }

        ConfigManager.SaveModelsConfig(config);
        OnSaveCallback?.Invoke();
        return true;
    }

    private bool Validate(out string error)
    {
        for (var i = 0; i < Providers.Count; i++)
        {
            var vm = Providers[i];
            var label = Providers.Count > 1 ? $"第 {i + 1} 个供应商" : "供应商";

            if (string.IsNullOrWhiteSpace(vm.Name))
            {
                error = $"{label}：名称不能为空";
                return false;
            }

            if (string.IsNullOrWhiteSpace(vm.BaseUrl))
            {
                error = $"{label}：请求地址不能为空";
                return false;
            }
        }

        error = "";
        return true;
    }

    [RelayCommand]
    private void AddProvider()
    {
        var vm = new ProviderItemViewModel
        {
            Name = "",
            RequestDelete = OnProviderDeleteRequested
        };
        Providers.Add(vm);
        ClearError();
    }

    private void OnProviderDeleteRequested(ProviderItemViewModel item)
    {
        Providers.Remove(item);
        ClearError();
    }

    private void ClearError()
    {
        ErrorMessage = "";
        HasError = false;
    }

    /// <summary>
    /// 决定保存时的字典 key：优先沿用 OriginalKey，否则由 Name 生成 slug；
    /// 冲突时追加序号保证唯一。
    /// </summary>
    private static string ResolveKey(ProviderItemViewModel vm, HashSet<string> usedKeys)
    {
        var baseKey = !string.IsNullOrWhiteSpace(vm.OriginalKey)
            ? vm.OriginalKey
            : Slugify(vm.Name);

        if (string.IsNullOrWhiteSpace(baseKey))
            baseKey = "provider";

        var key = baseKey;
        var n = 2;
        while (usedKeys.Contains(key))
        {
            key = $"{baseKey}{n++}";
        }
        usedKeys.Add(key);
        return key;
    }

    private static string Slugify(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "";
        var slug = Regex.Replace(name.Trim().ToLowerInvariant(), @"[^a-z0-9\-]+", "-");
        slug = Regex.Replace(slug, @"-{2,}", "-").Trim('-');
        return slug;
    }
}