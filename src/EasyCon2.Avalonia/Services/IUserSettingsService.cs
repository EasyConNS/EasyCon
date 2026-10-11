using EasyCon.Core.Config;

namespace EasyCon2.Avalonia.Services;

/// <summary>
/// 用户配置（ConfigState）的加载、防抖保存与关窗冲刷。
/// 读/写盘策略（含 500ms 防抖、UI 线程回写）集中于此；
/// VM 只负责 ConfigState ↔ 自身属性的映射（apply/collect 两个方向）。
/// </summary>
public interface IUserSettingsService
{
    /// <summary>最近一次加载成功的配置。Load() 之前为默认值。</summary>
    ConfigState Current { get; }

    /// <summary>加载期间为 true；期间的 ScheduleSave 全部忽略（防 apply 触发回存风暴）。</summary>
    bool IsLoading { get; }

    /// <summary>读盘 + 在 IsLoading 保护下执行 apply。读盘失败回退默认值并记日志（语义同原 catch 块）。</summary>
    void Load(Action<ConfigState> apply);

    /// <summary>立即执行 collect 到 Current 并写盘（写盘异常记日志，不上抛）。</summary>
    void Save(Action<ConfigState> collect);

    /// <summary>500ms 防抖后在 UI 线程执行 collect + 写盘；IsLoading 期间直接忽略。</summary>
    void ScheduleSave(Action<ConfigState> collect);

    /// <summary>取消挂起的防抖并立即 Save（关窗前调用，防丢最后一次更改）。</summary>
    void Flush(Action<ConfigState> collect);
}
