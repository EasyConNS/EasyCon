using Avalonia.Threading;
using EasyCon.Capture;
using EasyCon2.Avalonia.Core.Services;
using EzCv;
using System.Text;

namespace EasyCon2.Avalonia.Services;

/// <summary>
/// IToolCallService 的独立实现，从 MainWindowViewModel 中提取。
/// 通过委托访问编辑器状态，通过注入获取底层服务依赖。
/// </summary>
public class ToolCallService : IToolCallService
{
    private readonly IScriptService _scriptService;
    private readonly ICaptureService _captureService;
    private readonly Queue<string> _logBuffer;
    private readonly Func<string?> _getProjectDirectoryPath;
    private readonly Func<string> _getEditorText;
    private readonly Action<string> _setEditorText;
    private readonly Func<string?> _getScriptPath;
    private readonly Func<bool> _hasScriptPath;
    private readonly Func<DeviceStatusInfo> _getDeviceStatus;

    public ToolCallService(
        IScriptService scriptService,
        ICaptureService captureService,
        Queue<string> logBuffer,
        Func<string?> getProjectDirectoryPath,
        Func<string> getEditorText,
        Action<string> setEditorText,
        Func<string?> getScriptPath,
        Func<bool> hasScriptPath,
        Func<DeviceStatusInfo> getDeviceStatus)
    {
        _scriptService = scriptService;
        _captureService = captureService;
        _logBuffer = logBuffer;
        _getProjectDirectoryPath = getProjectDirectoryPath;
        _getEditorText = getEditorText;
        _setEditorText = setEditorText;
        _getScriptPath = getScriptPath;
        _hasScriptPath = hasScriptPath;
        _getDeviceStatus = getDeviceStatus;
    }

    // ── 编辑区 ──────────────────────────────

    public string GetScriptContent() => _getEditorText() ?? string.Empty;

    public void WriteScriptContent(string content, bool append)
    {
        Dispatcher.UIThread.Invoke(() =>
        {
            if (append && !string.IsNullOrEmpty(_getEditorText()))
                _setEditorText(_getEditorText() + Environment.NewLine + content);
            else
                _setEditorText(content);
        });
    }

    public int EditScriptContent(string oldString, string newString, int count)
    {
        var current = _getEditorText() ?? string.Empty;
        if (string.IsNullOrEmpty(oldString))
            return -1;

        if (!current.Contains(oldString, StringComparison.Ordinal))
            return -1;

        var replaced = 0;
        var result = count <= 0
            ? current.Replace(oldString, newString, StringComparison.Ordinal)
            : ReplaceLimited(current, oldString, newString, count, out replaced);

        if (count <= 0)
            replaced = current.Split([oldString], StringSplitOptions.None).Length - 1;

        Dispatcher.UIThread.Invoke(() => _setEditorText(result));
        return replaced;
    }

    private static string ReplaceLimited(string source, string oldStr, string newStr, int maxCount, out int replaced)
    {
        replaced = 0;
        var sb = new StringBuilder(source.Length);
        var span = source.AsSpan();
        int pos = 0;

        while (replaced < maxCount)
        {
            var idx = source.IndexOf(oldStr, pos, StringComparison.Ordinal);
            if (idx < 0) break;

            sb.Append(span[pos..idx]);
            sb.Append(newStr);
            pos = idx + oldStr.Length;
            replaced++;
        }

        sb.Append(span[pos..]);
        return sb.ToString();
    }

    public string? GetScriptPath() => _hasScriptPath() ? _getScriptPath() : null;

    public async Task<bool> CompileScriptAsync()
    {
        var text = _getEditorText();
        var path = _hasScriptPath() ? _getScriptPath() : null;
        return await _scriptService.CompileAsync(text, path);
    }

    public async Task<string> FormatScriptAsync()
    {
        var text = _getEditorText();
        if (string.IsNullOrWhiteSpace(text))
            return "(编辑区无脚本内容)";

        var path = _hasScriptPath() ? _getScriptPath() : null;
        if (!await _scriptService.CompileAsync(text, path))
            return "(编译失败，无法格式化，请先用 compile_script 检查错误)";

        var formatted = _scriptService.GetFormattedCode();
        await Dispatcher.UIThread.InvokeAsync(() => _setEditorText(formatted));
        return formatted;
    }

    // ── 状态 ────────────────────────────────

    public DeviceStatusInfo GetDeviceStatus() => _getDeviceStatus();

    // ── 项目 ────────────────────────────────

    public string? GetProjectTree()
    {
        var projectDir = _getProjectDirectoryPath();
        if (string.IsNullOrEmpty(projectDir) || !Directory.Exists(projectDir))
            return null;

        var sb = new StringBuilder();
        BuildTreeMd(sb, projectDir, 0, false);
        return sb.ToString();
    }

    /// <inheritdoc/>
    public string? GetProjectDirectory()
    {
        var projectDir = _getProjectDirectoryPath();
        return !string.IsNullOrEmpty(projectDir) && Directory.Exists(projectDir)
            ? projectDir
            : null;
    }

    private static void BuildTreeMd(StringBuilder sb, string path, int depth, bool isInLib)
    {
        var indent = new string(' ', depth * 2);
        var entries = Directory.GetFileSystemEntries(path)
            .Where(e => !Path.GetFileName(e).StartsWith('.'))
            .OrderBy(e => !Directory.Exists(e))
            .ThenBy(e => Path.GetFileName(e), StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var name = Path.GetFileName(path);
        var currentIsLib = isInLib || string.Equals(name, "lib", StringComparison.OrdinalIgnoreCase);

        foreach (var entry in entries)
        {
            var entryName = Path.GetFileName(entry);
            var isDir = Directory.Exists(entry);

            if (isDir)
            {
                sb.Append(indent).Append("- ").Append(entryName).Append("/\n");
                BuildTreeMd(sb, entry, depth + 1, currentIsLib);
            }
            else
            {
                var tag = "";
                var ext = Path.GetExtension(entry).ToUpperInvariant();
                if (ext is ".IL" or ".ILX") tag = " `标签`";
                else if (currentIsLib && ext is ".ECS" or ".TXT") tag = " `库`";

                sb.Append(indent).Append("- ").Append(entryName).Append(tag).Append('\n');
            }
        }
    }

    // ── 脚本执行 ──────────────────────────────

    public async Task<bool> RunScriptAsync()
    {
        if (_scriptService.IsRunning) return false;

        var text = _getEditorText() ?? string.Empty;
        var path = _hasScriptPath() ? _getScriptPath() : null;
        if (!await _scriptService.CompileAsync(text, path))
            return false;

        if (path is not null)
            _scriptService.Run(path);
        else
            _scriptService.RunFromContent(text);
        return true;
    }

    public void StopScript() => _scriptService.Stop();

    public bool IsScriptRunning => _scriptService.IsRunning;

    // ── 视觉 ────────────────────────────────

    public string? GetCurrentFrameBase64()
    {
        using var mat = _captureService.GetMatFrame();
        if (mat is null || mat.Empty()) return null;

        using var resized = mat.Resize(0.5);
        var bytes = resized.ToBytes(".png");
        return Convert.ToBase64String(bytes);
    }

    // ── 日志 ────────────────────────────────

    public string GetRecentLogs(int maxLines)
    {
        if (_logBuffer.Count == 0)
            return "(暂无日志)";

        var lines = _logBuffer.Count <= maxLines
            ? _logBuffer.ToArray()
            : _logBuffer.Skip(_logBuffer.Count - maxLines).ToArray();

        return string.Join(Environment.NewLine, lines);
    }
}
