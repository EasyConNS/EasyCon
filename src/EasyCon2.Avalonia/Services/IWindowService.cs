using Avalonia.Controls;

namespace EasyCon2.Avalonia.Services;

/// <summary>
/// 子窗口管理服务抽象：按键注册表化的单例窗口管理器。
/// 窗口的构造（View/ViewModel 装配）由组合根在启动时经 <see cref="Register"/> 注册，
/// 本接口只暴露通用的单例展示语义，使 Services 层零 feature 依赖。
/// </summary>
public interface IWindowService
{
    /// <summary>子窗口 Owner（组合根注入）。</summary>
    Window? Owner { get; set; }

    /// <summary>注册单实例窗口工厂（组合根启动时调用）。</summary>
    void Register(string key, Func<Window> factory);

    /// <summary>单例展示：已存在且可见则激活（最小化→还原→Activate）返回 false；否则创建并 Show 返回 true。</summary>
    bool Show(string key);
}

/// <summary>子窗口注册表的标准键。</summary>
public static class WindowKeys
{
    public const string EspConfig = "esp-config";
    public const string AlertConfig = "alert-config";
    public const string ModelsConfig = "models-config";
    public const string McpConfig = "mcp-config";
    public const string KeyMapping = "key-mapping";
    public const string ScriptSyntax = "script-syntax";
    public const string AudioLabels = "audio-labels";
    public const string AudioMatchTest = "audio-match-test";
    public const string DrawingBoard = "drawing-board";
}