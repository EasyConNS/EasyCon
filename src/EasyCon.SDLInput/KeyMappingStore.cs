using EasyCon.Core.Config;

namespace EasyCon.SDLInput;

/// <summary>按键映射的内存读缓存：首次访问加载一次，保存时刷新缓存。</summary>
public sealed class KeyMappingStore
{
    private readonly Func<KeyMappingConfig> _loader;
    private readonly Action<KeyMappingConfig> _saver;
    private KeyMappingConfig? _current;

    public KeyMappingStore(Func<KeyMappingConfig> loader, Action<KeyMappingConfig> saver)
    {
        _loader = loader;
        _saver = saver;
    }

    /// <summary>进程内单例：真实磁盘 IO + 保存时广播变更。</summary>
    public static KeyMappingStore Instance { get; } = new(SdlKeyMappingDefaults.LoadFromDiskOrDefaults, ConfigManager.SaveKeyMapping);

    /// <summary>当前生效的映射（内存缓存，首次访问加载一次）。</summary>
    public KeyMappingConfig Current => _current ??= _loader();

    /// <summary>保存并刷新缓存：先更新缓存，再写盘并触发 KeyMappingChanged，避免订阅方读到旧值。</summary>
    public void Save(KeyMappingConfig mapping)
    {
        _current = mapping;
        _saver(mapping);
    }

    /// <summary>清空缓存，下次访问时重新加载。</summary>
    public void Reset() => _current = null;
}