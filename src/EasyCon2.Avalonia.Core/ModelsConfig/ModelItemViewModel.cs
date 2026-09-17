using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EasyCon.Core.LLM.Models;

namespace EasyCon2.Avalonia.Core.ModelsConfig;

/// <summary>
/// 单个模型的编辑视图模型，对应 <see cref="ModelInfo"/>。
/// </summary>
public partial class ModelItemViewModel : ObservableObject
{
    public Action<ModelItemViewModel>? RequestDelete { get; set; }

    [ObservableProperty]
    private string id = "";

    [ObservableProperty]
    private string name = "";

    /// <summary>是否支持图像输入（视觉）。</summary>
    [ObservableProperty]
    private bool vision;

    /// <summary>
    /// 远端模型下拉框当前选中项；选中后填充本行 Id/Name 并复位。
    /// 数据源来自父级 ProviderItemViewModel.AvailableRemoteModels。
    /// </summary>
    [ObservableProperty]
    private string? selectedRemoteModel;

    public ModelItemViewModel() { }

    public ModelItemViewModel(ModelInfo info)
    {
        Id = info.Id;
        Name = info.Name;
        Vision = info.Vision;
    }

    [RelayCommand]
    private void Delete() => RequestDelete?.Invoke(this);

    /// <summary>下拉选择远端模型后，填充本行 Id/Name 并复位。</summary>
    partial void OnSelectedRemoteModelChanged(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        Id = value;
        if (string.IsNullOrWhiteSpace(Name))
            Name = value;
        // 复位，便于再次选择
        SelectedRemoteModel = null;
    }

    public ModelInfo ToModelInfo() => new()
    {
        Id = Id ?? "",
        Name = string.IsNullOrWhiteSpace(Name) ? Id ?? "" : Name,
        Vision = Vision
    };
}