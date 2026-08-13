using EasyCon.Core.Config;
using Serilog;
using EasyScript;
using System;
using System.Drawing;

class ConsoleOutAdapter : IIoAdapter
{
    private readonly AlertDispatcher _dispatcher = new(ConfigManager.LoadAlert());

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
        FileLogger?.Information(message);
    }

    public void Alert(string message)
    {
        Task.Run(async () =>
        {
            try
            {
                _dispatcher.OnResult += (_, result) => Print(result);
                await _dispatcher.DispatchAsync(message);
            }
            catch (Exception e)
            {
                Print($"推送失败:{e.Message}");
            }
        }).Wait();
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

public static class ColorfulConsole
{
    public static void Write(string message, Color color)
    {
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

public static class AnsiColors
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