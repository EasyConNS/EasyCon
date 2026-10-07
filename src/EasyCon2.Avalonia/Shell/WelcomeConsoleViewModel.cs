using CommunityToolkit.Mvvm.ComponentModel;
using EasyCon2.Avalonia.Services;
using EasyCon2.Avalonia.Terminal;
using EasyCon2.Avalonia.Services;
using System.Collections.ObjectModel;

namespace EasyCon2.Avalonia.Shell;

/// <summary>
/// 欢迎台跑马灯：单行欢迎语的彩虹色逐字渲染，脚本运行期间持续滚动动画。
/// </summary>
public partial class WelcomeConsoleViewModel : ObservableObject
{
    private static readonly RgbColor[] WelcomePalette =
    [
        new(0xF9, 0x5D, 0x6A),
        new(0xF8, 0xB4, 0x4C),
        new(0x9C, 0xD8, 0x5B),
        new(0x46, 0xD6, 0xC8),
        new(0x5A, 0x9C, 0xFF),
        new(0xC7, 0x7D, 0xFF)
    ];

    private readonly IScriptService _scriptService;
    private readonly IUiDispatcher _ui;
    private readonly System.Timers.Timer _timer = new(120);
    private string _welcomeText = string.Empty;
    private int _welcomeColorOffset;

    public ObservableCollection<TerminalLine> WelcomeLines { get; } = new();

    public WelcomeConsoleViewModel(IScriptService scriptService, string initialWelcomeText, IUiDispatcher uiDispatcher)
    {
        _scriptService = scriptService;
        _ui = uiDispatcher;
        _welcomeText = initialWelcomeText;

        _timer.Elapsed += (_, _) =>
        {
            if (!_scriptService.IsRunning)
                return;

            _ui.Post(() =>
            {
                _welcomeColorOffset++;
                UpdateWelcomeConsoleColors();
            });
        };

        RefreshWelcomeConsole();
        _timer.Start();
    }

    /// <summary>欢迎语文本变化（用户在设置页输入）后重渲染。</summary>
    public void SetWelcomeText(string text)
    {
        _welcomeText = text ?? string.Empty;
        RefreshWelcomeConsole();
    }

    /// <summary>脚本运行状态切换时重置跑马灯。</summary>
    public void Refresh() => RefreshWelcomeConsole();

    public void Stop()
    {
        _timer.Stop();
        _timer.Dispose();
    }

    private void RefreshWelcomeConsole()
    {
        WelcomeLines.Clear();
        WelcomeLines.Add(CreateWelcomeLine());
    }

    private TerminalLine CreateWelcomeLine()
    {
        var line = new TerminalLine();
        FillWelcomeLine(line);
        return line;
    }

    private void UpdateWelcomeConsoleColors()
    {
        if (WelcomeLines.Count == 0)
        {
            RefreshWelcomeConsole();
            return;
        }

        var line = WelcomeLines[0];
        line.Segments.Clear();
        FillWelcomeLine(line);

        // 仅触发重绘通知，不让集合经历 Clear 状态，避免跑马灯偏移被重置。
        WelcomeLines[0] = line;
    }

    private void FillWelcomeLine(TerminalLine line)
    {
        var welcomeText = _welcomeText ?? string.Empty;
        for (var i = 0; i < welcomeText.Length; i++)
        {
            var color = WelcomePalette[Mod(i - _welcomeColorOffset, WelcomePalette.Length)];
            line.Segments.Add(new TextSegment(welcomeText[i].ToString(), color));
        }
    }

    private static int Mod(int value, int divisor)
    {
        var result = value % divisor;
        return result < 0 ? result + divisor : result;
    }
}