using Avalonia.Threading;
using EasyCon2.Avalonia.Controls;
using System.Threading.Tasks;

namespace EasyCon2.Avalonia.Services;

/// <summary>
/// 启动期 UI 预热 —— 把「首次打开按键映射窗口」才会付出的解析成本提前到启动阶段。
/// </summary>
public static class UiPreloader
{
    /// <summary>
    /// 预热按键映射窗口所需的资源：
    /// ① Icons.json（约 135KB）解析放后台线程，不占用启动；
    /// ② 控制器 SVG 的 Skia 建图放 UI 线程，但排在窗口首帧之后（Background 优先级），不影响启动观感。
    /// </summary>
    public static void Warmup()
    {
        // Lazy 工厂在 ExecutionAndPublication 模式下会缓存异常：预热失败后每次取图标
        // 都会重抛。这里吞掉异常，让首次真实使用时自然重试。
        Task.Run(() =>
        {
            try
            {
                IconRegistry.Preload();
            }
            catch
            {
                // 预热失败不致命，留给首次使用时重试
            }
        });

        Dispatcher.UIThread.Post(ControllerSvgCache.Warmup, DispatcherPriority.Background);
    }
}