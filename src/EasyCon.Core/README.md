# EasyCon.Core 模块

## 模块概述

EasyCon.Core是整个EasyCon2项目的核心模块，提供脚本运行、项目管理、配置管理等核心功能。该模块作为业务逻辑的中心枢纽，协调各个子模块的工作，为上层UI提供统一的API接口。

## 核心功能

### 1. 脚本执行引擎
- **多语言支持**: 支持ECS、Lua、Python等多种脚本语言
- **运行时管理**: 脚本生命周期管理
- **错误处理**: 统一的异常处理和错误报告
- **调试支持**: 断点、单步执行、变量监视

### 2. 项目管理
- **项目结构**: 管理脚本项目的文件组织
- **依赖管理**: 处理脚本间的依赖关系
- **资源管理**: 图像标签、配置文件等资源管理
- **构建系统**: 脚本编译和打包

### 3. 配置管理
- **应用配置**: 全局应用设置
- **用户配置**: 用户个性化设置
- **项目配置**: 项目级别的配置
- **运行时配置**: 动态运行时配置

### 4. 设备抽象
- **统一接口**: 为不同硬件提供统一访问接口
- **插件系统**: 支持第三方设备驱动
- **热插拔支持**: 设备动态加载和卸载

## 主要组件

### ECCore 类
核心功能入口，提供静态方法访问各项核心功能：

```csharp
public static partial class ECCore
{
    public const string ImgDir = "ImgLabel";
    
    // 获取可用的视频采集源
    public static IEnumerable<string> GetCaptureSources() => 
        ECCapture.GetCaptureCamera();
    
    // 获取采集卡类型
    public static IEnumerable<(string, int)> GetCaptureTypes() => 
        ECCapture.GetCaptureTypes();
    
    // 获取设备名称列表
    public static List<string> GetDeviceNames() => 
        EasyDevice.ECDevice.GetPortNames();
    
    // 获取启用的搜索方法
    public static IEnumerable<SearchMethod> GetEnableSearchMethods() => 
        ECSearch.GetEnableSearchMethods();
    
    // 加载图像标签
    public static (IEnumerable<ImgLabel>, int, int) LoadImgLabels(params string[] paths)
    {
        var ILs = ImmutableArray.CreateBuilder<ImgLabel>();
        var set = new HashSet<string>();
        var total = 0;
        var rept = 0;

        foreach (var path in paths)
        {
            var ilpath = Path.Combine(path, ImgDir);
            if (!Directory.Exists(ilpath)) continue;

            foreach (var file in Directory.GetFiles(ilpath, "*.IL"))
            {
                total++;
                try
                {
                    var il = ECSearch.LoadIL(file);
                    if (!set.Add(il.name))
                    {
                        rept++;
                        Console.WriteLine($"重复标签:{il.name}, 路径：{file}");
                        continue;
                    }
                    ILs.Add(il);
                }
                catch
                {
                    Console.WriteLine($"[!错误!]无法加载标签文件:{file}");
                }
            }
        }
        return (ILs.ToImmutable(), total, rept);
    }
}
```

### Scripter 类
脚本管理器，负责脚本的加载、编译和执行：

```csharp
public class Scripter
{
    private readonly Dictionary<string, IRunner> _runners = new();
    private readonly ProjectManager _projectManager;
    private readonly Config _config;
    
    public Scripter(ProjectManager projectManager, Config config)
    {
        _projectManager = projectManager;
        _config = config;
        RegisterRunners();
    }
    
    private void RegisterRunners()
    {
        // 注册不同语言的运行器
        _runners["ecs"] = new EasyRunner();
        _runners["lua"] = new LuaRunner();
        _runners["py"] = new PyRunner();
    }
    
    public Script LoadScript(string filePath)
    {
        var extension = Path.GetExtension(filePath).ToLower();
        var runner = _runners[extension];
        return runner.Load(filePath);
    }
    
    public void RunScript(Script script, IOutputAdapter output)
    {
        var runner = _runners[script.Language];
        runner.Run(script, output);
    }
    
    public void StopScript(Script script)
    {
        var runner = _runners[script.Language];
        runner.Stop(script);
    }
}
```

### ProjectManager 类
项目管理器，管理脚本项目的结构和资源：

```csharp
public class ProjectManager
{
    public class ProjectInfo
    {
        public string Name { get; set; }
        public string Path { get; set; }
        public string MainScript { get; set; }
        public List<string> ScriptFiles { get; set; }
        public List<string> ImageLabels { get; set; }
        public Dictionary<string, object> Metadata { get; set; }
    }
    
    public ProjectManager()
    {
        Projects = new List<ProjectInfo>();
    }
    
    public List<ProjectInfo> Projects { get; }
    
    public ProjectInfo CreateProject(string name, string path)
    {
        var project = new ProjectInfo
        {
            Name = name,
            Path = path,
            MainScript = $"main.{GetDefaultScriptExtension()}",
            ScriptFiles = new List<string>(),
            ImageLabels = new List<string>(),
            Metadata = new Dictionary<string, object>()
        };
        
        Projects.Add(project);
        SaveProject(project);
        return project;
    }
    
    public ProjectInfo LoadProject(string projectPath)
    {
        var json = File.ReadAllText(projectPath);
        var project = JsonSerializer.Deserialize<ProjectInfo>(json);
        Projects.Add(project);
        return project;
    }
    
    public void SaveProject(ProjectInfo project)
    {
        var projectPath = Path.Combine(project.Path, "project.json");
        var json = JsonSerializer.Serialize(project, new JsonSerializerOptions
        {
            WriteIndented = true
        });
        File.WriteAllText(projectPath, json);
    }
    
    public void AddScriptFile(ProjectInfo project, string scriptPath)
    {
        project.ScriptFiles.Add(scriptPath);
        SaveProject(project);
    }
    
    public void AddImageLabel(ProjectInfo project, string labelPath)
    {
        project.ImageLabels.Add(labelPath);
        SaveProject(project);
    }
}
```

### Config 类
配置管理器，管理应用的各种配置：

```csharp
public class Config
{
    private readonly Dictionary<string, object> _configurations = new();
    private readonly string _configPath;
    
    public Config(string configPath = "config.json")
    {
        _configPath = configPath;
        Load();
    }
    
    public T Get<T>(string key, T defaultValue = default)
    {
        if (_configurations.TryGetValue(key, out var value))
        {
            try
            {
                return (T)Convert.ChangeType(value, typeof(T));
            }
            catch
            {
                return defaultValue;
            }
        }
        return defaultValue;
    }
    
    public void Set<T>(string key, T value)
    {
        _configurations[key] = value;
        Save();
    }
    
    public void Load()
    {
        if (File.Exists(_configPath))
        {
            var json = File.ReadAllText(_configPath);
            var config = JsonSerializer.Deserialize<Dictionary<string, object>>(json);
            _configurations.Clear();
            foreach (var kvp in config)
            {
                _configurations[kvp.Key] = kvp.Value;
            }
        }
    }
    
    public void Save()
    {
        var json = JsonSerializer.Serialize(_configurations, new JsonSerializerOptions
        {
            WriteIndented = true
        });
        File.WriteAllText(_configPath, json);
    }
}
```

### GamePadAdapter 类
游戏手柄适配器，提供统一的手柄访问接口：

```csharp
public class GamePadAdapter
{
    public enum GamePadType
    {
        Unknown,
        Xbox360,
        XboxOne,
        PS3,
        PS4,
        PS5,
        SwitchPro,
        SwitchJoyCon
    }
    
    public class GamePadInfo
    {
        public int Index { get; set; }
        public string Name { get; set; }
        public GamePadType Type { get; set; }
        public bool IsConnected { get; set; }
    }
    
    public static List<GamePadInfo> GetConnectedGamePads()
    {
        var gamepads = new List<GamePadInfo>();
        
        // 检测连接的游戏手柄
        for (int i = 0; i < 4; i++)
        {
            var state = GamePad.GetState(i);
            if (state.IsConnected)
            {
                gamepads.Add(new GamePadInfo
                {
                    Index = i,
                    Name = GetGamePadName(i),
                    Type = DetectGamePadType(i),
                    IsConnected = true
                });
            }
        }
        
        return gamepads;
    }
    
    public static GamePadState GetGamePadState(int index)
    {
        return GamePad.GetState(index);
    }
    
    public static void SetVibration(int index, float leftMotor, float rightMotor)
    {
        GamePad.SetVibration(index, leftMotor, rightMotor);
    }
}
```

## 脚本运行器

### IRunner 接口
所有脚本运行器的通用接口：

```csharp
public interface IRunner
{
    string Language { get; }
    string FileExtension { get; }
    
    Script Load(string filePath);
    void Run(Script script, IOutputAdapter output);
    void Stop(Script script);
    bool IsRunning(Script script);
}
```

### EasyRunner (ECS脚本运行器)
```csharp
public class EasyRunner : IRunner
{
    public string Language => "ecs";
    public string FileExtension => ".ecs";
    
    private readonly Dictionary<string, Script> _runningScripts = new();
    
    public Script Load(string filePath)
    {
        var source = File.ReadAllText(filePath);
        var compilation = new Compilation(source);
        var evaluation = compilation.Compile();
        
        return new Script
        {
            Path = filePath,
            Language = Language,
            Source = source,
            Evaluation = evaluation
        };
    }
    
    public void Run(Script script, IOutputAdapter output)
    {
        if (_runningScripts.ContainsKey(script.Path))
        {
            throw new InvalidOperationException("脚本已在运行中");
        }
        
        _runningScripts[script.Path] = script;
        script.Evaluation.SetOutputAdapter(output);
        
        Task.Run(() =>
        {
            try
            {
                script.Evaluation.Execute();
            }
            finally
            {
                _runningScripts.Remove(script.Path);
            }
        });
    }
    
    public void Stop(Script script)
    {
        script.Evaluation.Stop();
        _runningScripts.Remove(script.Path);
    }
    
    public bool IsRunning(Script script)
    {
        return _runningScripts.ContainsKey(script.Path);
    }
}
```

### LuaRunner (Lua脚本运行器)
```csharp
public class LuaRunner : IRunner
{
    public string Language => "lua";
    public string FileExtension => ".lua";
    
    public Script Load(string filePath)
    {
        // 使用MoonSharp或NLua等Lua库
        var script = new Script
        {
            Path = filePath,
            Language = Language,
            Source = File.ReadAllText(filePath)
        };
        
        return script;
    }
    
    public void Run(Script script, IOutputAdapter output)
    {
        // 创建Lua脚本环境
        var lua = new MoonSharp.Interpreter.Script();
        
        // 注册输出适配器
        lua.Globals["output_adapter"] = new LuaOutputAdapter(output);
        
        // 执行脚本
        lua.DoString(script.Source);
    }
    
    public void Stop(Script script)
    {
        // 停止Lua脚本执行
    }
    
    public bool IsRunning(Script script)
    {
        // 检查脚本是否在运行
        return false;
    }
}
```

## 扩展功能

### ImgLabelExt 图像标签扩展
```csharp
public static class ImgLabelExt
{
    public static Mat GetTemplate(this ImgLabel label)
    {
        if (label.Template == null)
        {
            label.Template = Cv2.ImRead(label.ImagePath);
        }
        return label.Template;
    }
    
    public static MatchResult FindIn(this ImgLabel label, Mat source, double threshold = 0.85)
    {
        return ECSearch.FindImage(source, label.GetTemplate(), threshold);
    }
    
    public static List<MatchResult> FindAllIn(this ImgLabel label, Mat source, double threshold = 0.85)
    {
        return ECSearch.FindAllImages(source, label.GetTemplate(), threshold);
    }
}
```

### KeyExt 按键扩展
```csharp
public static class KeyExt
{
    public static NSKeys ToNSKeys(this KeyCode keyCode)
    {
        return keyCode switch
        {
            KeyCode.A => NSKeys.A,
            KeyCode.B => NSKeys.B,
            KeyCode.X => NSKeys.X,
            KeyCode.Y => NSKeys.Y,
            KeyCode.Up => NSKeys.Up,
            KeyCode.Down => NSKeys.Down,
            KeyCode.Left => NSKeys.Left,
            KeyCode.Right => NSKeys.Right,
            _ => throw new ArgumentException($"不支持的按键: {keyCode}")
        };
    }
    
    public static string ToCommand(this NSKeys key)
    {
        return key switch
        {
            NSKeys.A => "A",
            NSKeys.B => "B",
            NSKeys.X => "X",
            NSKeys.Y => "Y",
            _ => key.ToString()
        };
    }
}
```

## 辅助工具

### PushPlusClient 推送通知客户端
```csharp
public class PushPlusClient
{
    private readonly string _token;
    private readonly HttpClient _httpClient = new();
    
    public PushPlusClient(string token)
    {
        _token = token;
    }
    
    public async Task<bool> SendNotificationAsync(string title, string content)
    {
        var url = "http://www.pushplus.plus/send";
        var data = new
        {
            token = _token,
            title = title,
            content = content,
            template = "html"
        };
        
        var json = JsonSerializer.Serialize(data);
        var response = await _httpClient.PostAsync(url, 
            new StringContent(json, Encoding.UTF8, "application/json"));
        
        return response.IsSuccessStatusCode;
    }
}
```

## 使用示例

### 基本脚本执行
```csharp
// 创建核心实例
var scripter = new Scripter(projectManager, config);

// 加载脚本
var script = scripter.LoadScript("test_script.ecs");

// 创建输出适配器
var output = new GamePadAdapter(device);

// 执行脚本
scripter.RunScript(script, output);

// 停止脚本
scripter.StopScript(script);
```

### 项目管理
```csharp
// 创建新项目
var project = projectManager.CreateProject("MyProject", "./projects/MyProject");

// 添加脚本文件
projectManager.AddScriptFile(project, "./scripts/main.ecs");

// 添加图像标签
projectManager.AddImageLabel(project, "./labels/button_a.il");

// 保存项目
projectManager.SaveProject(project);
```

### 配置管理
```csharp
// 创建配置管理器
var config = new Config("app_config.json");

// 读取配置
var debugMode = config.Get("DebugMode", false);
var logLevel = config.Get("LogLevel", "Info");

// 设置配置
config.Set("DebugMode", true);
config.Set("LogLevel", "Debug");

// 配置会自动保存
```

## 性能优化

### 脚本缓存
```csharp
public class ScriptCache
{
    private readonly Dictionary<string, Script> _cache = new();
    private readonly Timer _cleanupTimer;
    
    public ScriptCache()
    {
        _cleanupTimer = new Timer(CleanupCache, null, 
            TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
    }
    
    public Script GetOrCreate(string filePath, Func<string, Script> factory)
    {
        if (!_cache.ContainsKey(filePath))
        {
            _cache[filePath] = factory(filePath);
        }
        return _cache[filePath];
    }
    
    private void CleanupCache(object? state)
    {
        var expiredScripts = _cache.Where(kvp => 
            !File.Exists(kvp.Key) || File.GetLastWriteTime(kvp.Key) > kvp.Value.LoadTime)
            .ToList();
        
        foreach (var script in expiredScripts)
        {
            _cache.Remove(script.Key);
        }
    }
}
```

## 依赖项

- **.NET**: System, System.IO, System.Text.Json
- **项目依赖**: EasyCon.Device, EasyCon.Capture, EasyCon.Script

## 未来发展

### 计划功能
- [ ] 插件系统
- [ ] 远程执行支持
- [ ] 云端配置同步
- [ ] 性能分析工具
- [ ] 脚本调试器集成

---

**模块维护者**: EasyCon.Core开发团队  
**最后更新**: 2026年4月16日  
**版本**: 1.0