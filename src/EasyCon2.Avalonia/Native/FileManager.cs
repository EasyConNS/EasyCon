using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace EasyCon2.Avalonia.Native;

/// <summary>
/// 跨平台“在系统文件管理器中打开路径”工具。
/// 参考 sourcegit Native.OS.OpenInFileManager：macOS 用 open、Linux 用 xdg-open、Windows 用 explorer。
/// </summary>
public static class FileManager
{
    /// <summary>
    /// 在系统文件管理器中打开指定路径：目录直接打开，文件则定位（macOS 用 -R）。
    /// </summary>
    public static void OpenInFileManager(string path)
    {
        if (string.IsNullOrEmpty(path))
            return;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            if (Directory.Exists(path))
                Process.Start("open", Quote(path));
            else if (File.Exists(path))
                Process.Start("open", $"{Quote(path)} -R");
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            if (Directory.Exists(path))
                Process.Start("xdg-open", Quote(path));
            else if (File.Exists(path))
                Process.Start("xdg-open", Quote(Path.GetDirectoryName(path)!));
        }
        else
        {
            // Windows
            if (Directory.Exists(path) || File.Exists(path))
                Process.Start(new ProcessStartInfo("explorer.exe", Quote(path)) { UseShellExecute = true });
        }
    }

    private static string Quote(string p) => $"\"{p}\"";
}
