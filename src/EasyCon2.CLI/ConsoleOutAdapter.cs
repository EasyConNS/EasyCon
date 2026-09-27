using EasyCon.Core.Config;
using EasyScript;
using Serilog;
using System;
using System.Drawing;

class ConsoleOutAdapter : IIoAdapter
{
    private readonly AlertDispatcher _dispatcher = new(ConfigManager.LoadAlert());

    public ConsoleOutAdapter()
    {
        // 只订阅一次：此前每次 Alert 都 += 且从不退订，结果按调用次数翻倍打印
        _dispatcher.OnResult += (_, result) => Print(result);
    }

    /// <summary>可选的滚动文件日志器，设置后控制台输出会同步写入文件。</summary>
    public ILogger? FileLogger { get; set; }

    private bool _msgNewLine = true;
    private bool _msgFirstLine = true;

    public void Print(string message, bool newline = true)
    {
        _msgNewLine = _msgNewLine && newline;
        PrintInternal(message, null);
    }

    public void Info(string message, bool timestamp = false)
    {
        PrintInternal(message, Color.Green, timestamp);
    }

    public void Log(string message, bool timestamp = false)
    {
        PrintInternal(message, Color.White, timestamp);
    }

    public void Warn(string message, bool timestamp = false)
    {
        PrintInternal(message, Color.Orange, timestamp);
    }

    public void Error(string message, bool timestamp = false)
    {
        PrintInternal(message, Color.Red, timestamp);
    }

    private void PrintInternal(string message, Color? color, bool timestamp = true)
    {
        if (_msgNewLine)
        {
            if (!_msgFirstLine)
                Console.WriteLine();
            _msgFirstLine = false;
            if (timestamp)
                ColorfulConsole.Write(DateTime.Now.ToString("[HH:mm:ss.fff] "), Color.Gray);
        }
        ColorfulConsole.Write(message, color ?? Color.White);
        _msgNewLine = true;
        // 文件日志按严重度落盘（stdout 侧无严重度概念）
        var level = color == Color.Red ? Serilog.Events.LogEventLevel.Error
            : color == Color.Orange ? Serilog.Events.LogEventLevel.Warning
            : Serilog.Events.LogEventLevel.Information;
        FileLogger?.Write(level, message);
    }

    public void Alert(string message)
    {
        try
        {
            // CLI 脚本线程同步等待推送完成；有界 30s，防止慢速 HTTP 长时间卡住脚本推进
            var dispatch = _dispatcher.DispatchAsync(message);
            if (!dispatch.Wait(TimeSpan.FromSeconds(30)))
                Print("推送超时（30秒），已放弃等待");
        }
        catch (Exception e)
        {
            Print($"推送失败:{e.Message}");
        }
    }

    public string ReadLine()
    {
        try
        {
            return Console.ReadLine() ?? "";
        }
        catch
        {
            return "";
        }
    }

    public bool TryReadLine(out string line)
    {
        try
        {
            line = Console.ReadLine() ?? "";
            return true;
        }
        catch
        {
            line = "";
            return false;
        }
    }
}

internal static class ColorfulConsole
{
    private static bool AnsiEnabled => !Console.IsOutputRedirected && Environment.GetEnvironmentVariable("NO_COLOR") is null;

    internal static void Write(string message, Color color)
    {
        if (!AnsiEnabled)
        {
            // 重定向/NO_COLOR：剥离转义序列，保证 `ecs run x > log` 产物是纯文本
            Console.Write(message);
            return;
        }
        var ac = AnsiColors.White;
        if (color == Color.Gray)
            ac = AnsiColors.Gray;
        else if (color == Color.Green)
            ac = AnsiColors.Green;
        else if (color == Color.Orange)
            ac = AnsiColors.Orange;
        else if (color == Color.Red)
            ac = AnsiColors.Red;

        Console.Write($"{ac}{message}{AnsiColors.Reset}");
    }
}

internal static class AnsiColors
{
    public const string Reset = "\u001b[0m";
    public const string White = Reset;

    // 前景色
    public const string Green = "\u001b[32m";
    public const string BrightGreen = "\u001b[92m";
    // public const string White = "\u001b[97m";
    public const string Gray = "\u001b[90m";
    public const string Red = "\u001b[31m";       // 标准红
    public const string Orange = "\u001b[38;5;208m";

    // 装饰
    public const string Bold = "\u001b[1m";
    public const string Underline = "\u001b[4m";
}