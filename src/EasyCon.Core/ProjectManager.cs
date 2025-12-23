using System.IO.Compression;

namespace EC.Core;

public sealed class ProjectManager : IDisposable
{
    // 工程内容存储
    private string _mainSource = string.Empty;
    private readonly Dictionary<string, string> _libFiles = new();
    private readonly Dictionary<string, byte[]> _imageFiles = new();

    // 文件路径管理
    private string _zipFilePath;
    private bool _isModified;

    // 公共访问属性
    public string MainSource => _mainSource;
    public IReadOnlyDictionary<string, string> LibFiles => _libFiles;
    public IReadOnlyDictionary<string, byte[]> ImageFiles => _imageFiles;
    public string CurrentFilePath => _zipFilePath;
    public bool IsModified => _isModified;
    public bool IsLoaded => !string.IsNullOrEmpty(_zipFilePath);

    // 文件操作接口
    public void SetMainSource(string content)
    {
        if (content == null) throw new ArgumentNullException(nameof(content));
        if (_mainSource != content)
        {
            _mainSource = content;
            _isModified = true;
        }
    }

    public void SetLibFile(string fileName, string content)
    {
        ValidateFileName(fileName, ".txt");
        if (content == null) throw new ArgumentNullException(nameof(content));

        if (!_libFiles.TryGetValue(fileName, out var existing) || existing != content)
        {
            _libFiles[fileName] = content;
            _isModified = true;
        }
    }

    public void SetImageFile(string fileName, byte[] data)
    {
        ValidateFileName(fileName, ".il");
        if (data == null) throw new ArgumentNullException(nameof(data));

        // 检查内容是否变化
        bool changed = true;
        if (_imageFiles.TryGetValue(fileName, out var existing))
        {
            changed = !existing.SequenceEqual(data);
        }

        if (changed)
        {
            _imageFiles[fileName] = data;
            _isModified = true;
        }
    }

    public bool RemoveLibFile(string fileName)
    {
        if (_libFiles.Remove(fileName))
        {
            _isModified = true;
            return true;
        }
        return false;
    }

    public bool RemoveImageFile(string fileName)
    {
        if (_imageFiles.Remove(fileName))
        {
            _isModified = true;
            return true;
        }
        return false;
    }

    public void RenameLibFile(string oldName, string newName)
    {
        if (!_libFiles.ContainsKey(oldName))
            throw new FileNotFoundException($"库文件 '{oldName}' 不存在");

        ValidateFileName(newName, ".txt");

        if (_libFiles.ContainsKey(newName))
            throw new InvalidOperationException($"目标文件名 '{newName}' 已存在");

        string content = _libFiles[oldName];
        _libFiles.Remove(oldName);
        _libFiles[newName] = content;
        _isModified = true;
    }

    public void RenameImageFile(string oldName, string newName)
    {
        if (!_imageFiles.ContainsKey(oldName))
            throw new FileNotFoundException($"图像文件 '{oldName}' 不存在");

        ValidateFileName(newName, ".il");

        if (_imageFiles.ContainsKey(newName))
            throw new InvalidOperationException($"目标文件名 '{newName}' 已存在");

        byte[] data = _imageFiles[oldName];
        _imageFiles.Remove(oldName);
        _imageFiles[newName] = data;
        _isModified = true;
    }

    // 文件验证
    private void ValidateFileName(string fileName, string requiredExtension)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            throw new ArgumentException("文件名不能为空");

        if (fileName.Contains('/') || fileName.Contains('\\'))
            throw new ArgumentException("文件名不能包含路径分隔符");

        if (Path.GetInvalidFileNameChars().Any(c => fileName.Contains(c)))
            throw new ArgumentException("文件名包含无效字符");

        if (!fileName.EndsWith(requiredExtension, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"文件扩展名必须是{requiredExtension}");
    }

    // 工程加载与保存
    public void LoadFromZip(string zipFilePath)
    {
        if (!File.Exists(zipFilePath))
            throw new FileNotFoundException("ZIP文件不存在", zipFilePath);

        ClearProject();

        using (var fileStream = new FileStream(zipFilePath, FileMode.Open))
        using (var memoryStream = new MemoryStream())
        {
            fileStream.CopyTo(memoryStream);
            memoryStream.Seek(0, SeekOrigin.Begin);

            using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Read, true))
            {
                // 验证ZIP结构
                ValidateProjectStructure(archive);

                // 加载main.txt
                var mainEntry = archive.GetEntry("main.txt");
                using (var reader = new StreamReader(mainEntry.Open()))
                {
                    _mainSource = reader.ReadToEnd();
                }

                // 加载lib目录下的txt文件
                foreach (var entry in archive.Entries
                    .Where(e => e.FullName.StartsWith("lib/") && !e.FullName.EndsWith("/")))
                {
                    using (var reader = new StreamReader(entry.Open()))
                    {
                        _libFiles[Path.GetFileName(entry.Name)] = reader.ReadToEnd();
                    }
                }

                // 加载img目录下的il文件
                foreach (var entry in archive.Entries
                    .Where(e => e.FullName.StartsWith("img/") && !e.FullName.EndsWith("/")))
                {
                    using (var entryStream = entry.Open())
                    using (var ms = new MemoryStream())
                    {
                        entryStream.CopyTo(ms);
                        _imageFiles[Path.GetFileName(entry.Name)] = ms.ToArray();
                    }
                }
            }
        }

        _zipFilePath = zipFilePath;
        _isModified = false;
    }

    public void SaveToOriginalZip()
    {
        if (!IsLoaded)
            throw new InvalidOperationException("尚未加载任何工程文件");

        SaveToZip(_zipFilePath);
    }

    public void SaveToZip(string zipFilePath)
    {
        if (!IsLoaded && string.IsNullOrEmpty(zipFilePath))
            throw new InvalidOperationException("需要指定保存路径");

        using (var memoryStream = CreateZipStream())
        {
            // 确保目录存在
            var directory = Path.GetDirectoryName(zipFilePath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            using (var fileStream = new FileStream(zipFilePath, FileMode.Create))
            {
                memoryStream.CopyTo(fileStream);
            }
        }

        // 更新当前路径（如果是新路径）
        if (!string.Equals(zipFilePath, _zipFilePath, StringComparison.OrdinalIgnoreCase))
        {
            _zipFilePath = zipFilePath;
        }

        _isModified = false;
    }

    // 创建新工程
    public void CreateNewProject(string initialContent = "// 新的工程主文件")
    {
        ClearProject();
        _mainSource = initialContent;
        _isModified = true;
    }

    // 清空当前工程
    private void ClearProject()
    {
        _mainSource = string.Empty;
        _libFiles.Clear();
        _imageFiles.Clear();
        _zipFilePath = null;
        _isModified = false;
    }

    // 创建ZIP内存流
    private MemoryStream CreateZipStream()
    {
        var memoryStream = new MemoryStream();

        using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, true))
        {
            // 保存main.txt
            var mainEntry = archive.CreateEntry("main.txt");
            using (var writer = new StreamWriter(mainEntry.Open()))
            {
                writer.Write(_mainSource);
            }

            // 保存lib目录下的txt文件
            foreach (var file in _libFiles)
            {
                var entry = archive.CreateEntry($"lib/{file.Key}");
                using (var writer = new StreamWriter(entry.Open()))
                {
                    writer.Write(file.Value);
                }
            }

            // 保存img目录下的il文件
            foreach (var file in _imageFiles)
            {
                var entry = archive.CreateEntry($"img/{file.Key}");
                using (var stream = entry.Open())
                {
                    stream.Write(file.Value, 0, file.Value.Length);
                }
            }
        }

        memoryStream.Seek(0, SeekOrigin.Begin);
        return memoryStream;
    }

    // 验证项目结构
    private void ValidateProjectStructure(ZipArchive archive)
    {
        int mainTxtCount = 0;

        foreach (var entry in archive.Entries)
        {
            // 跳过目录条目
            if (entry.FullName.EndsWith("/")) continue;

            // 检查根目录下的main.txt
            if (entry.FullName == "main.txt")
            {
                mainTxtCount++;
                continue;
            }

            // 检查lib目录下的txt文件
            if (entry.FullName.StartsWith("lib/"))
            {
                // 检查是否在lib根目录下
                if (entry.FullName.Count(c => c == '/') != 1)
                    throw new InvalidDataException($"无效的lib目录结构: {entry.FullName}");

                // 检查文件扩展名
                if (!entry.Name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"lib目录下发现非txt文件: {entry.Name}");

                continue;
            }

            // 检查img目录下的il文件
            if (entry.FullName.StartsWith("img/"))
            {
                // 检查是否在img根目录下
                if (entry.FullName.Count(c => c == '/') != 1)
                    throw new InvalidDataException($"无效的img目录结构: {entry.FullName}");

                // 检查文件扩展名
                if (!entry.Name.EndsWith(".il", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"img目录下发现非il文件: {entry.Name}");

                continue;
            }

            // 发现不符合要求的条目
            throw new InvalidDataException($"发现未授权的文件或目录: {entry.FullName}");
        }

        // 必须只有一个main.txt文件
        if (mainTxtCount != 1)
        {
            throw new InvalidDataException(mainTxtCount == 0 ?
                "缺少main.txt文件" : "发现多个main.txt文件");
        }
    }

    // 资源清理
    public void Dispose()
    {
        ClearProject();
    }
}