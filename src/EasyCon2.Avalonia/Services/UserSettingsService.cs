using Avalonia.Threading;
using EasyCon.Capture;
using EasyCon.Core.Config;
using EasyCon.Core.Services;

namespace EasyCon2.Avalonia.Services;

/// <summary>
/// <see cref="IUserSettingsService"/> 默认实现：ConfigManager 读写盘 + 500ms 防抖。
/// 防抖线程模型照抄原 MainWindowViewModel.ScheduleSaveUserSettings（Task.Run + Task.Delay +
/// Dispatcher.UIThread.Post 回 UI 线程写盘），未改走 IUiDispatcher，以保持测试时序不变。
/// </summary>
public sealed class UserSettingsService : IUserSettingsService
{
    private readonly Func<ConfigState> _load;
    private readonly Action<ConfigState> _save;
    private readonly ILogService? _log;
    private CancellationTokenSource? _saveDefer;

    public UserSettingsService(Func<ConfigState>? load = null, Action<ConfigState>? save = null, ILogService? log = null)
    {
        _load = load ?? ConfigManager.LoadConfig;
        _save = save ?? ConfigManager.SaveConfig;
        _log = log;
    }

    /// <inheritdoc />
    public ConfigState Current { get; private set; } = new();

    /// <inheritdoc />
    public bool IsLoading { get; private set; }

    /// <inheritdoc />
    public void Load(Action<ConfigState> apply)
    {
        IsLoading = true;
        try
        {
            Current = SafeLoad();
            apply(Current);
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>读盘失败回退默认值 + 默认主题并记日志（语义照抄原 LoadUserSettings 的 catch 块）。</summary>
    private ConfigState SafeLoad()
    {
        try
        {
            ConfigState state = _load();
            new HostProfiles
            {
                Controller = state.ControllerProfile,
                Capture = state.CaptureProfile ?? new CaptureProfile(Backend: state.CaptureType),
            }.Validate();
            return state;
        }
        catch (Exception ex)
        {
            ThemeManager.Instance.ApplyColorScheme(ThemeManager.whiteGraySchemeName);
            ThemeManager.Instance.ApplyThemeStyle(ThemeManager.ClassicStyleName);
            _log?.AddLog($"读取用户配置失败，已使用默认设置: {ex.Message}");
            return new ConfigState();
        }
    }

    /// <inheritdoc />
    public void Save(Action<ConfigState> collect)
    {
        if (IsLoading)
            return;

        collect(Current);

        try
        {
            _save(Current);
        }
        catch (Exception ex)
        {
            _log?.AddLog($"保存用户配置失败: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public void ScheduleSave(Action<ConfigState> collect)
    {
        if (IsLoading)
            return;

        _saveDefer?.Cancel();
        _saveDefer?.Dispose();
        _saveDefer = new CancellationTokenSource();
        var token = _saveDefer.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(500, token);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            Dispatcher.UIThread.Post(() =>
            {
                if (!token.IsCancellationRequested)
                    Save(collect);
            });
        });
    }

    /// <inheritdoc />
    public void Flush(Action<ConfigState> collect)
    {
        _saveDefer?.Cancel();
        _saveDefer?.Dispose();
        _saveDefer = null;
        Save(collect);
    }

    internal static string NormalizeCaptureType(string? captureType)
        => CaptureSourceCatalog.NormalizeBackendName(captureType);

    internal static string NormalizeColorSchemeName(string? colorSchemeName, bool legacyDarkMode)
    {
        return colorSchemeName switch
        {
            ThemeManager.whiteGraySchemeName or "工业灰" => ThemeManager.whiteGraySchemeName,
            ThemeManager.WarmToneSchemeName or "暖色调" => ThemeManager.WarmToneSchemeName,
            ThemeManager.DarkModeSchemeName => ThemeManager.DarkModeSchemeName,
            _ => legacyDarkMode ? ThemeManager.DarkModeSchemeName : ThemeManager.whiteGraySchemeName
        };
    }

    internal static string NormalizeThemeStyleName(string? themeStyleName, string colorSchemeName)
    {
        return themeStyleName switch
        {
            ThemeManager.ClassicStyleName => ThemeManager.ClassicStyleName,
            ThemeManager.RoundedStyleName => ThemeManager.RoundedStyleName,
            _ => colorSchemeName == ThemeManager.whiteGraySchemeName
                ? ThemeManager.ClassicStyleName
                : ThemeManager.RoundedStyleName
        };
    }
}