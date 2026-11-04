using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using EasyCon.Core.Config;
using EasyCon2.Avalonia.AlertConfig;
using EasyCon2.Avalonia.Connection;
using EasyCon2.Avalonia.KeyMapping;
using EasyCon2.Avalonia.Mcp;
using EasyCon2.Avalonia.ModelsConfig;
using EasyCon2.Avalonia.Monitoring;
using EasyCon2.Avalonia.Native;
using EasyCon2.Avalonia.Scripting;
using EasyCon2.Avalonia.Shell;
using System;
using System.Windows.Input;

namespace EasyCon2.Avalonia;

public partial class App
{
    /// <summary>
    /// GUI 诊断日志汇点，OnFrameworkInitializationCompleted 注入（写入日志文件）。
    /// </summary>
    public static Action<string>? LogSink { get; set; }

    /// <summary>
    /// 原生菜单专用极简命令：无参、永远可执行，且不触发 <see cref="CanExecuteChanged"/>
    /// （原生菜单不在视觉树中，无法响应 CanExecute 刷新）。
    /// 参考 sourcegit App.Commands.cs。
    /// </summary>
    public class Command : ICommand
    {
        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }

        public Command(Action<object?> action)
        {
            _action = action;
        }

        public bool CanExecute(object? parameter) => _action != null;
        public void Execute(object? parameter) => _action?.Invoke(parameter);

        private readonly Action<object?>? _action;
    }

    // 弹出独立的“关于”窗口（应用模态），参考 sourcegit App.OpenAboutCommand。
    public static readonly Command OpenAboutCommand = new(async _ =>
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime { MainWindow: { } owner })
            return;

        try
        {
            await new AboutWindow().ShowDialog(owner);
        }
        catch (Exception ex)
        {
            // async 委托经 Command.Execute 即为 async void，异常会沿 UI 线程上抛终止进程
            LogSink?.Invoke($"[命令] 打开关于窗口失败: {ex.Message}");
        }
    });

    // 切换到主窗口“用户配置”标签页（EasyCon 的偏好设置入口）；仅切换标签，不滚动到顶部。
    public static readonly Command OpenPreferencesCommand = new(_ =>
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow.DataContext: MainWindowViewModel vm })
            vm.SelectEditorTabCommand.Execute("2");
    });

    // 复用 MainWindowViewModel.CheckUpdateCommand，与“用户配置”页检查更新完全一致。
    public static readonly Command CheckForUpdateCommand = new(_ =>
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow.DataContext: MainWindowViewModel vm })
            vm.CheckUpdateCommand.Execute(null);
    });

    // 跨平台打开配置目录（参考 sourcegit Native.OS.OpenInFileManager）。
    public static readonly Command OpenConfigDirCommand = new(_ =>
        FileManager.OpenInFileManager(AppPaths.ConfigDir));
}