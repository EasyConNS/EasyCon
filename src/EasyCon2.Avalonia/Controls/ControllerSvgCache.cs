using Avalonia.Svg.Skia;
using System;
using System.Threading;

namespace EasyCon2.Avalonia.Controls;

/// <summary>
/// 控制器 SVG 的共享 <see cref="SvgSource"/> 缓存。
/// 启动时预热一次，之后所有窗口复用同一个已解析/已建图的 source，
/// 避免首次打开按键映射窗口时出现约半秒的解析延迟。
/// </summary>
public static class ControllerSvgCache
{
    private const string ControllerUri = "avares://EasyCon2.Avalonia/Resources/Images/ns2-controller.svg";

    private static readonly Lazy<SvgSource> Source = new(
        () => SvgSource.Load(ControllerUri, null),
        LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>已加载的控制器 SVG 源（首次访问时加载）。</summary>
    public static SvgSource Instance => Source.Value;

    /// <summary>强制完成加载 —— 供启动预热调用。</summary>
    public static void Warmup()
    {
        _ = Source.Value;
    }
}