#nullable enable
using MaaFramework.Binding;

namespace EasyCon.Core.Maa;

/// <summary>固定版本原生运行环境；只在第一次 Maa 操作时加载。</summary>
public static class MaaEnvironment
{
    private static readonly Lazy<string> Root = new(() =>
    {
        string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "EasyCon", "maa", "5.14.3");
        string bootstrap = Path.Combine(root, "host-bootstrap-v1", "pipeline");
        Directory.CreateDirectory(bootstrap);
        string temporary = Path.Combine(bootstrap, Guid.NewGuid().ToString("N") + ".tmp");
        File.WriteAllText(temporary, "{\"EasyCon.HostReady\":{\"recognition\":\"DirectHit\",\"action\":\"DoNothing\"}}");
        File.Move(temporary, Path.Combine(bootstrap, "host.json"), true);
#pragma warning disable CS0618
        string version = MaaGlobal.Shared.Version;
        if (version.TrimStart('v') != "5.14.3") throw new InvalidOperationException($"Maa runtime 版本不符: {version}，要求 5.14.3");
        MaaGlobal.Shared.SetOption(GlobalOption.LogDir, Path.Combine(root, "logs"));
        MaaGlobal.Shared.SetOption(GlobalOption.StdoutLevel, 0);
#pragma warning restore CS0618
        return root;
    });

    public static string CacheDirectory => Root.Value;
    public static string BootstrapResource => Path.Combine(CacheDirectory, "host-bootstrap-v1");
    internal static MaaResource LoadBootstrap(string? resourceDirectory = null)
    {
        _ = CacheDirectory;
        MaaResource resource = new();
        try
        {
            // 空宿主节点不使用推理。避免每个短运行触发自动 GPU 探测/设备初始化。
            if (!resource.SetOption(ResourceOption.InferenceDevice, InferenceDevice.CPU) ||
                !resource.SetOption(ResourceOption.InferenceExecutionProvider, InferenceExecutionProvider.CPU))
                throw new InvalidOperationException("Maa bootstrap CPU 配置失败");
            string path = resourceDirectory ?? BootstrapResource;
            if (resource.AppendBundle(path).Wait() != MaaJobStatus.Succeeded || !resource.IsLoaded)
                throw new InvalidDataException($"Maa 宿主资源加载失败: {path}");
            return resource;
        }
        catch { resource.Dispose(); throw; }
    }
    public static string RuntimeVersion
    {
        get
        {
            _ = CacheDirectory;
#pragma warning disable CS0618
            return MaaGlobal.Shared.Version;
#pragma warning restore CS0618
        }
    }
}