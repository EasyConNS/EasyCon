using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace EasyCon2.Avalonia.Core.AiAgent;

/// <summary>
/// 对话消息条目基类，ItemsControl 通过 DataTemplate 按类型分发渲染。
/// </summary>
public abstract partial class ChatMessageItem : ObservableObject
{
}

/// <summary>
/// 用户消息，右侧气泡。
/// </summary>
public partial class UserMessage : ChatMessageItem
{
    [ObservableProperty]
    private string _content = "";
}

/// <summary>
/// AI 回复消息，左侧气泡，可包含思考过程折叠块。
/// </summary>
public partial class AssistantMessage : ChatMessageItem
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBubbleVisible))]
    private string _content = "";

    [ObservableProperty]
    private ThinkingBlock? _thinking;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBubbleVisible))]
    private bool _isStreaming;

    /// <summary>正文气泡是否可见：流式期间始终可见，结束后仅非空内容可见。</summary>
    public bool IsBubbleVisible => IsStreaming || !string.IsNullOrWhiteSpace(Content);
}

/// <summary>
/// 思考过程块，支持折叠/展开。
/// </summary>
public partial class ThinkingBlock : ObservableObject
{
    [ObservableProperty]
    private string _text = "";

    [ObservableProperty]
    private bool _isExpanded;

    [RelayCommand]
    private void ToggleExpand() => IsExpanded = !IsExpanded;
}

/// <summary>
/// 工具调用消息，紧凑行，结果可折叠展开。
/// </summary>
public partial class ToolCallMessage : ChatMessageItem
{
    [ObservableProperty]
    private string _name = "";

    [ObservableProperty]
    private bool _success = true;

    [ObservableProperty]
    private string _summary = "";

    [ObservableProperty]
    private string _fullResult = "";

    [ObservableProperty]
    private bool _isExpanded;

    [RelayCommand]
    private void ToggleExpand() => IsExpanded = !IsExpanded;
}
