using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EasyCon.Core.LLM;
using EasyCon.Core.LLM.Models;
using System.Collections.ObjectModel;

namespace EasyCon2.Avalonia.Core.ModelsConfig;

/// <summary>
/// 供应商的编辑视图模型，对应 <see cref="ProviderConfig"/> 的一个字典条目。
/// 字典 key 作为稳定标识符，显示用 <see cref="Name"/>。
/// </summary>
public partial class ProviderItemViewModel : ObservableObject
{
    public Action<ProviderItemViewModel>? RequestDelete { get; set; }

    /// <summary>原始字典 key（新建供应商时为空，保存时由 VM 决定最终 key）。</summary>
    public string OriginalKey { get; set; } = "";

    [ObservableProperty]
    private string name = "";

    /// <summary>主页 URL（可选）。</summary>
    [ObservableProperty]
    private string homePage = "";

    [ObservableProperty]
    private string baseUrl = "";

    [ObservableProperty]
    private string apiKey = "";

    /// <summary>是否明文显示 API Key。</summary>
    [ObservableProperty]
    private bool isApiKeyVisible;

    /// <summary>API 协议类型，当前仅 openai-completions。</summary>
    [ObservableProperty]
    private string api = "openai-completions";

    /// <summary>主页是否可点击（非空时为 true）。</summary>
    public bool HasHomePage => !string.IsNullOrWhiteSpace(HomePage);

    [ObservableProperty]
    private bool isExpanded = true;

    /// <summary>通过 /models 端点拉取的可用模型 ID 列表（每行下拉数据源）。</summary>
    [ObservableProperty]
    private ObservableCollection<string> availableRemoteModels = [];

    /// <summary>是否正在拉取远端模型列表。</summary>
    [ObservableProperty]
    private bool isFetchingModels;

    /// <summary>拉取状态文本（成功数 / 错误信息）。</summary>
    [ObservableProperty]
    private string? fetchModelsStatus;

    public ObservableCollection<ModelItemViewModel> Models { get; } = [];

    /// <summary>支持的 API 协议列表（供 ComboBox 绑定）。</summary>
    public static readonly string[] ApiTypes = ["openai-completions"];

    public ProviderItemViewModel() { }

    public ProviderItemViewModel(string key, ProviderConfig provider)
    {
        OriginalKey = key ?? "";
        Name = provider.Name;
        if (string.IsNullOrWhiteSpace(Name)) Name = OriginalKey;
        HomePage = provider.HomePage ?? "";
        BaseUrl = provider.BaseUrl ?? "";
        ApiKey = provider.ApiKey ?? "";
        Api = string.IsNullOrWhiteSpace(provider.Api) ? "openai-completions" : provider.Api;

        foreach (var m in provider.Models)
            Models.Add(new ModelItemViewModel(m) { RequestDelete = OnModelDeleteRequested });
    }

    partial void OnHomePageChanged(string value)
        => OnPropertyChanged(nameof(HasHomePage));

    [RelayCommand]
    private void ToggleExpand() => IsExpanded = !IsExpanded;

    [RelayCommand]
    private void ToggleApiKeyVisibility() => IsApiKeyVisible = !IsApiKeyVisible;

    [RelayCommand]
    private void Delete() => RequestDelete?.Invoke(this);

    [RelayCommand]
    private void AddModel()
    {
        var vm = new ModelItemViewModel { RequestDelete = OnModelDeleteRequested };
        Models.Add(vm);
    }

    /// <summary>
    /// 拉取远端 /models 端点，填充下拉数据源。
    /// </summary>
    [RelayCommand]
    private async Task FetchModelsAsync()
    {
        if (string.IsNullOrWhiteSpace(BaseUrl) || string.IsNullOrWhiteSpace(ApiKey))
        {
            FetchModelsStatus = "请先填写请求地址和 API Key";
            return;
        }

        IsFetchingModels = true;
        FetchModelsStatus = "获取中…";

        try
        {
            var client = new OpenAIChatClient(BaseUrl, ApiKey);
            var ids = await client.ListModelsAsync();
            client.Dispose();

            AvailableRemoteModels.Clear();
            foreach (var id in ids)
                AvailableRemoteModels.Add(id);

            FetchModelsStatus = ids.Count > 0
                ? $"获取成功，共 {ids.Count} 个模型，点击每行右侧下拉箭头填充"
                : "获取成功，但未返回任何模型";
        }
        catch (Exception ex)
        {
            FetchModelsStatus = $"获取失败：{ex.Message}";
        }
        finally
        {
            IsFetchingModels = false;
        }
    }

    private void OnModelDeleteRequested(ModelItemViewModel item) => Models.Remove(item);

    public ProviderConfig ToProviderConfig() => new()
    {
        Name = Name ?? "",
        HomePage = HomePage ?? "",
        BaseUrl = BaseUrl ?? "",
        ApiKey = ApiKey ?? "",
        Api = string.IsNullOrWhiteSpace(Api) ? "openai-completions" : Api,
        Models = [.. Models.Select(m => m.ToModelInfo())]
    };
}
