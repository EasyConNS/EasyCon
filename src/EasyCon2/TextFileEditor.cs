using System.IO;
using System.Text;

namespace EasyCon2;

public class TextFileEditor
{
    #region 属性

    /// <summary>
    /// 当前文件路径
    /// </summary>
    public string CurrentFilePath { get; private set; }


    private const string defaultName = "未命名脚本";
    public string FileName => CurrentFilePath == "" ? defaultName : Path.GetFileName(CurrentFilePath);
    public string ShowFileText => IsModified ? $"{FileName}(已编辑)" : FileName;

    /// <summary>
    /// 文件内容
    /// </summary>
    public string Content { get; private set; }

    /// <summary>
    /// 文件是否已打开
    /// </summary>
    public bool IsFileOpened => !string.IsNullOrEmpty(CurrentFilePath);

    /// <summary>
    /// 内容是否有修改且未保存
    /// </summary>
    public bool IsModified { get; private set; }

    /// <summary>
    /// 文件编码（默认UTF-8）
    /// </summary>
    public Encoding FileEncoding { get; set; } = Encoding.UTF8;

    #endregion

    #region 事件

    /// <summary>
    /// 文件打开时触发
    /// </summary>
    public event EventHandler<FileOperationEventArgs> FileOpened;

    /// <summary>
    /// 文件保存时触发
    /// </summary>
    public event EventHandler<FileOperationEventArgs> FileSaved;

    /// <summary>
    /// 文件关闭时触发
    /// </summary>
    public event EventHandler<FileOperationEventArgs> FileClosed;

    /// <summary>
    /// 内容修改时触发
    /// </summary>
    public event EventHandler<ContentChangedEventArgs> ContentChanged;

    /// <summary>
    /// 需要保存确认时触发（UI层应显示确认对话框）
    /// </summary>
    public event EventHandler<SaveConfirmationEventArgs> SaveConfirmationRequired;

    /// <summary>
    /// 需要保存到文件时触发（UI层应显示保存对话框）
    /// </summary>
    public event EventHandler<SaveToFileEventArgs> SaveToFileRequired;

    #endregion

    #region 构造函数

    public TextFileEditor()
    {
        Content = string.Empty;
    }

    #endregion

    #region 公共方法

    /// <summary>
    /// 打开文件
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <returns>是否成功打开</returns>
    public async Task<bool> OpenFileAsync(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("文件路径不能为空");

        // 检查是否需要保存当前内容
        if (IsModified)
        {
            var shouldSave = await RequestSaveConfirmationAsync("打开新文件");
            if (shouldSave == SaveConfirmationResult.Cancel)
                return false;

            if (shouldSave == SaveConfirmationResult.Save)
            {
                if (!await SaveOrSaveAsAsync())
                    return false;
            }
            // 如果选择不保存，继续打开新文件
        }

        try
        {
            // 读取文件内容
            using (var reader = new StreamReader(filePath, FileEncoding, true))
            {
                Content = await reader.ReadToEndAsync();
            }

            CurrentFilePath = filePath;
            IsModified = false;

            OnFileOpened(new FileOperationEventArgs(filePath));
            OnContentChanged(new ContentChangedEventArgs(false));

            return true;
        }
        catch (Exception ex)
        {
            throw new FileOperationException($"打开文件失败: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// 关闭当前文件
    /// </summary>
    /// <returns>是否成功关闭</returns>
    public async Task<bool> CloseFileAsync()
    {
        if (!IsFileOpened)
        {
            // 没有打开文件，但可能有未保存的内容
            if (IsModified)
            {
                var shouldSave = await RequestSaveConfirmationAsync("关闭编辑器");
                if (shouldSave == SaveConfirmationResult.Cancel)
                    return false;

                if (shouldSave == SaveConfirmationResult.Save)
                {
                    if (!await SaveOrSaveAsAsync())
                        return false;
                }
            }

            OnFileClosed(new FileOperationEventArgs(null));
            return true;
        }

        // 检查是否需要保存
        if (IsModified)
        {
            var shouldSave = await RequestSaveConfirmationAsync("关闭文件");
            if (shouldSave == SaveConfirmationResult.Cancel)
                return false;

            if (shouldSave == SaveConfirmationResult.Save)
            {
                if (!await SaveCurrentFileAsync())
                    return false;
            }
        }

        var filePath = CurrentFilePath;
        CurrentFilePath = null;
        Content = string.Empty;
        IsModified = false;

        OnFileClosed(new FileOperationEventArgs(filePath));
        OnContentChanged(new ContentChangedEventArgs(false));

        return true;
    }

    /// <summary>
    /// 保存当前文件
    /// </summary>
    /// <returns>是否成功保存</returns>
    public async Task<bool> SaveFileAsync()
    {
        if (!IsFileOpened)
        {
            // 没有打开文件，需要另存为
            return await SaveAsFileAsync();
        }

        return await SaveCurrentFileAsync();
    }

    /// <summary>
    /// 另存为
    /// </summary>
    /// <param name="filePath">保存路径（可选，如果为null则触发SaveToFileRequired事件）</param>
    /// <returns>是否成功保存</returns>
    public async Task<bool> SaveAsFileAsync(string filePath = null)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            // 请求UI层提供保存路径
            var saveToFileArgs = new SaveToFileEventArgs();
            OnSaveToFileRequired(saveToFileArgs);

            if (saveToFileArgs.Cancel || string.IsNullOrWhiteSpace(saveToFileArgs.FilePath))
                return false;

            filePath = saveToFileArgs.FilePath;
        }

        try
        {
            // 确保目录存在
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // 写入文件
            using (var writer = new StreamWriter(filePath, false, FileEncoding))
            {
                await writer.WriteAsync(Content);
            }

            CurrentFilePath = filePath;
            IsModified = false;

            OnFileSaved(new FileOperationEventArgs(filePath));

            return true;
        }
        catch (Exception ex)
        {
            throw new FileOperationException($"保存文件失败: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// 设置文件内容
    /// </summary>
    /// <param name="content">新的内容</param>
    /// <param name="forceModify">是否强制标记为已修改（默认true）</param>
    public void SetContent(string content, bool forceModify = true)
    {
        if (Content == content)
            return;

        Content = content;
        IsModified = forceModify || IsModified;

        OnContentChanged(new ContentChangedEventArgs(IsModified));
    }

    /// <summary>
    /// 清空内容
    /// </summary>
    public void ClearContent()
    {
        SetContent(string.Empty);
    }

    #endregion

    #region 私有方法

    /// <summary>
    /// 保存当前文件
    /// </summary>
    private async Task<bool> SaveCurrentFileAsync()
    {
        try
        {
            // 备份原文件（可选）
            if (File.Exists(CurrentFilePath))
            {
                var backupPath = $"{CurrentFilePath}.backup";
                File.Copy(CurrentFilePath, backupPath, true);
            }

            // 写入文件
            using (var writer = new StreamWriter(CurrentFilePath, false, FileEncoding))
            {
                await writer.WriteAsync(Content);
            }

            IsModified = false;

            OnFileSaved(new FileOperationEventArgs(CurrentFilePath));
            OnContentChanged(new ContentChangedEventArgs(false));

            return true;
        }
        catch (Exception ex)
        {
            throw new FileOperationException($"保存文件失败: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// 请求保存确认
    /// </summary>
    /// <param name="operationName">操作名称</param>
    /// <returns>用户选择</returns>
    private async Task<SaveConfirmationResult> RequestSaveConfirmationAsync(string operationName)
    {
        var args = new SaveConfirmationEventArgs(operationName);
        OnSaveConfirmationRequired(args);

        // 等待UI层响应
        await args.WaitForResponseAsync();

        return args.Result;
    }

    /// <summary>
    /// 保存或另存为
    /// </summary>
    private async Task<bool> SaveOrSaveAsAsync()
    {
        if (IsFileOpened)
        {
            return await SaveCurrentFileAsync();
        }
        else
        {
            return await SaveAsFileAsync();
        }
    }

    #endregion

    #region 事件触发方法

    protected virtual void OnFileOpened(FileOperationEventArgs e)
    {
        FileOpened?.Invoke(this, e);
    }

    protected virtual void OnFileSaved(FileOperationEventArgs e)
    {
        FileSaved?.Invoke(this, e);
    }

    protected virtual void OnFileClosed(FileOperationEventArgs e)
    {
        FileClosed?.Invoke(this, e);
    }

    protected virtual void OnContentChanged(ContentChangedEventArgs e)
    {
        ContentChanged?.Invoke(this, e);
    }

    protected virtual void OnSaveConfirmationRequired(SaveConfirmationEventArgs e)
    {
        SaveConfirmationRequired?.Invoke(this, e);
    }

    protected virtual void OnSaveToFileRequired(SaveToFileEventArgs e)
    {
        SaveToFileRequired?.Invoke(this, e);
    }

    #endregion
}

#region 事件参数类

/// <summary>
/// 文件操作事件参数
/// </summary>
public class FileOperationEventArgs : EventArgs
{
    public string FilePath { get; }
    public DateTime Timestamp { get; }

    public FileOperationEventArgs(string filePath)
    {
        FilePath = filePath;
        Timestamp = DateTime.Now;
    }
}

/// <summary>
/// 内容修改事件参数
/// </summary>
public class ContentChangedEventArgs : EventArgs
{
    public bool IsModified { get; }
    public DateTime Timestamp { get; }

    public ContentChangedEventArgs(bool isModified)
    {
        IsModified = isModified;
        Timestamp = DateTime.Now;
    }
}

/// <summary>
/// 保存确认事件参数
/// </summary>
public class SaveConfirmationEventArgs : EventArgs
{
    public string OperationName { get; }
    public SaveConfirmationResult Result { get; set; }
    public bool IsResponded => Result != SaveConfirmationResult.None;

    private readonly TaskCompletionSource<bool> _responseSource;

    public SaveConfirmationEventArgs(string operationName)
    {
        OperationName = operationName;
        Result = SaveConfirmationResult.None;
        _responseSource = new TaskCompletionSource<bool>();
    }

    /// <summary>
    /// 等待UI层响应
    /// </summary>
    public async Task WaitForResponseAsync()
    {
        await _responseSource.Task;
    }

    /// <summary>
    /// 设置响应结果（由UI层调用）
    /// </summary>
    public void SetResponse(SaveConfirmationResult result)
    {
        Result = result;
        _responseSource.SetResult(true);
    }
}

/// <summary>
/// 保存到文件事件参数
/// </summary>
public class SaveToFileEventArgs : EventArgs
{
    public string FilePath { get; set; }
    public bool Cancel { get; set; }
    public DateTime Timestamp { get; }

    public SaveToFileEventArgs()
    {
        Timestamp = DateTime.Now;
    }
}

#endregion

#region 枚举

/// <summary>
/// 保存确认结果
/// </summary>
public enum SaveConfirmationResult
{
    None = 0,
    Save,
    DontSave,
    Cancel
}

#endregion

#region 自定义异常

/// <summary>
/// 文件操作异常
/// </summary>
public class FileOperationException : Exception
{
    public FileOperationException(string message) : base(message) { }
    public FileOperationException(string message, Exception innerException)
        : base(message, innerException) { }
}

#endregion