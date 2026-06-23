using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EasyCon.Core.Config;
using EasyCon.Core.LLM.Mcp;
using System.Collections.ObjectModel;
using System.Text.RegularExpressions;

namespace EasyCon2.Avalonia.Core.Mcp;

/// <summary>
/// MCP 服务器配置 ViewModel，镜像 ModelsConfigViewModel 模式。
/// </summary>
public partial class McpConfigViewModel : ObservableObject
{
    public ObservableCollection<McpServerItemViewModel> Servers { get; } = [];

    [ObservableProperty]
    private string _errorMessage = "";

    [ObservableProperty]
    private bool _hasError;

    public Action? OnSaveCallback { get; set; }

    public void Load()
    {
        var config = ConfigManager.LoadMcpConfig();
        Servers.Clear();
        foreach (var (key, server) in config.Mcp.Servers)
        {
            var vm = new McpServerItemViewModel(key, server) { RequestDelete = OnServerDeleteRequested };
            Servers.Add(vm);
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

        var config = new McpConfig();
        var usedKeys = new HashSet<string>(StringComparer.Ordinal);

        foreach (var vm in Servers)
        {
            var key = ResolveKey(vm, usedKeys);
            config.Mcp.Servers[key] = vm.ToServerConfig();
        }

        ConfigManager.SaveMcpConfig(config);
        OnSaveCallback?.Invoke();
        return true;
    }

    private bool Validate(out string error)
    {
        for (var i = 0; i < Servers.Count; i++)
        {
            var vm = Servers[i];
            var label = Servers.Count > 1 ? $"第 {i + 1} 个服务器" : "服务器";

            if (string.IsNullOrWhiteSpace(vm.Name))
            {
                error = $"{label}：名称不能为空";
                return false;
            }

            if (string.IsNullOrWhiteSpace(vm.Command))
            {
                error = $"{label}：命令不能为空";
                return false;
            }
        }

        error = "";
        return true;
    }

    [RelayCommand]
    private void AddServer()
    {
        var vm = new McpServerItemViewModel
        {
            Name = "",
            RequestDelete = OnServerDeleteRequested
        };
        Servers.Add(vm);
        ClearError();
    }

    private void OnServerDeleteRequested(McpServerItemViewModel item)
    {
        Servers.Remove(item);
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
    private static string ResolveKey(McpServerItemViewModel vm, HashSet<string> usedKeys)
    {
        var baseKey = !string.IsNullOrWhiteSpace(vm.OriginalKey)
            ? vm.OriginalKey
            : Slugify(vm.Name);

        if (string.IsNullOrWhiteSpace(baseKey))
            baseKey = "server";

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

/// <summary>
/// 单个 MCP 服务器配置项 ViewModel。
/// </summary>
public partial class McpServerItemViewModel : ObservableObject
{
    public string? OriginalKey { get; }

    [ObservableProperty]
    private string _name = "";

    [ObservableProperty]
    private string _command = "";

    [ObservableProperty]
    private string _argsText = "";  // 逗号分隔的参数文本（UI 友好）

    [ObservableProperty]
    private string _envText = "";   // KEY=VALUE 格式，每行一个（UI 友好）

    [ObservableProperty]
    private bool _enabled = true;

    public Action<McpServerItemViewModel>? RequestDelete { get; set; }

    public McpServerItemViewModel() { }

    public McpServerItemViewModel(string key, McpServerConfig config)
    {
        OriginalKey = key;
        Name = config.Name;
        Command = config.Command;
        ArgsText = string.Join(", ", config.Args);
        EnvText = string.Join("\n", config.Env.Select(kv => $"{kv.Key}={kv.Value}"));
        Enabled = config.Enabled;
    }

    [RelayCommand]
    private void Delete() => RequestDelete?.Invoke(this);

    public McpServerConfig ToServerConfig()
    {
        var args = string.IsNullOrWhiteSpace(ArgsText)
            ? []
            : ArgsText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

        var env = new Dictionary<string, string>();
        if (!string.IsNullOrWhiteSpace(EnvText))
        {
            foreach (var line in EnvText.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = line.Split('=', 2);
                if (parts.Length == 2)
                {
                    var key = parts[0].Trim();
                    var value = parts[1].Trim();
                    if (!string.IsNullOrWhiteSpace(key))
                        env[key] = value;
                }
            }
        }

        return new McpServerConfig
        {
            Name = Name,
            Transport = McpTransport.Stdio,
            Command = Command,
            Args = args,
            Env = env,
            Enabled = Enabled
        };
    }
}
