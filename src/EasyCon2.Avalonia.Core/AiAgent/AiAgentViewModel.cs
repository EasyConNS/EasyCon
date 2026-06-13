using System.Collections.ObjectModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EasyCon.Core.Config;
using EasyCon.Core.LLM;
using EasyCon.Core.LLM.Messages;
using EasyCon.Core.LLM.Models;

namespace EasyCon2.Avalonia.Core.AiAgent;

public partial class AiAgentViewModel : ObservableObject
{
    private readonly List<ChatMessage> _history = [];

    private string _conversationPrefix = "";
    private readonly StringBuilder _pendingReply = new();
    private ModelsConfig? _cachedConfig;

    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    private string _conversationText = "";

    [ObservableProperty]
    private int _outputCaretIndex;

    [ObservableProperty]
    private string _inputText = "";

    [ObservableProperty]
    private bool _isGenerating;

    [ObservableProperty]
    private ModelEntry? _selectedEntry;

    private CancellationTokenSource? _cts;

    public ObservableCollection<ModelEntry> AllModels { get; } = [];

    private bool _initialized;

    public AiAgentViewModel() { }

    partial void OnConversationTextChanged(string value)
    {
        OutputCaretIndex = value.Length;
    }

    partial void OnIsOpenChanged(bool value)
    {
        if (value && !_initialized)
        {
            _initialized = true;
            RefreshModels();
        }
    }

    [RelayCommand]
    private void Close()
    {
        IsOpen = false;
    }

    [RelayCommand]
    private void NewChat()
    {
        _history.Clear();
        _conversationPrefix = "";
        _pendingReply.Clear();
        ConversationText = "";
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task SendOrStopAsync()
    {
        if (IsGenerating)
        {
            _cts?.Cancel();
            return;
        }

        if (string.IsNullOrWhiteSpace(InputText))
            return;

        if (SelectedEntry is null)
        {
            var hint = AllModels.Count == 0
                ? "未配置模型供应商。请在配置目录下创建 models.json 文件。"
                : "请先选择一个模型。";
            ConversationText = AppendMessage(ConversationText, "系统", hint);
            return;
        }

        var message = InputText.Trim();
        InputText = "";
        IsGenerating = true;

        _conversationPrefix = AppendMessage(_conversationPrefix, "你", message);
        _history.Add(ChatMessage.User(message));

        _conversationPrefix = AppendMessage(_conversationPrefix, "AI", "");
        _pendingReply.Clear();
        ConversationText = _conversationPrefix;

        _cts = new CancellationTokenSource();
        try
        {
            var provider = GetProviderConfig(SelectedEntry.ProviderKey);
            using var client = ChatClientFactory.Create(provider);
            var request = new ChatRequest
            {
                Model = SelectedEntry.ModelId,
                Messages = BuildMessages()
            };

            await foreach (var chunk in client.SendStreamAsync(request, _cts.Token))
            {
                _pendingReply.Append(chunk);
                ConversationText = string.Concat(_conversationPrefix, _pendingReply);
            }

            _history.Add(ChatMessage.Assistant(_pendingReply.ToString()));
            _conversationPrefix = ConversationText;
        }
        catch (OperationCanceledException)
        {
            ConversationText = _conversationPrefix + "[已停止]";
            _conversationPrefix = ConversationText;
        }
        catch (Exception ex)
        {
            ConversationText = _conversationPrefix + $"[错误] {ex.Message}";
            _conversationPrefix = ConversationText;
        }
        finally
        {
            _pendingReply.Clear();
            _cts?.Dispose();
            _cts = null;
            IsGenerating = false;
        }
    }

    [RelayCommand]
    private void RefreshModels()
    {
        _cachedConfig = ConfigManager.LoadModelsConfig();
        AllModels.Clear();

        foreach (var (providerKey, provider) in _cachedConfig.Models.Providers)
        {
            if (provider.Api != "openai-completions") continue;

            foreach (var m in provider.Models)
            {
                AllModels.Add(new ModelEntry
                {
                    ProviderKey = providerKey,
                    ModelId = m.Id,
                    ModelName = m.Name,
                    ProviderLabel = providerKey
                });
            }
        }

        SelectedEntry = AllModels.Count > 0 ? AllModels[0] : null;
    }

    private ProviderConfig GetProviderConfig(string providerKey)
    {
        _cachedConfig ??= ConfigManager.LoadModelsConfig();
        return _cachedConfig.Models.Providers[providerKey];
    }

    private List<ChatMessage> BuildMessages()
    {
        var messages = new List<ChatMessage>(_history.Count + 1) { ChatMessage.System(SystemPrompts.Default) };
        messages.AddRange(_history);
        return messages;
    }

    private static string AppendMessage(string existing, string role, string content)
    {
        var sep = string.IsNullOrEmpty(existing) ? "" : $"{Environment.NewLine}{Environment.NewLine}";
        return $"{existing}{sep}{role}: {content}";
    }
}
