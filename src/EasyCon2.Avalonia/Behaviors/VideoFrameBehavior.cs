using Avalonia;
using Avalonia.Controls;

namespace EasyCon2.Avalonia.Behaviors;

/// <summary>
/// Attached Property：绑定 ViewModel 的帧计数器，值变化时对 Image 调用 InvalidateVisual()。
/// 用于解决 WriteableBitmap 复用同一引用时 Image 不自动刷新的问题。
/// </summary>
public class VideoFrameBehavior
{
    public static readonly AttachedProperty<int> FrameIndexProperty =
        AvaloniaProperty.RegisterAttached<VideoFrameBehavior, Image, int>("FrameIndex");

    static VideoFrameBehavior()
    {
        FrameIndexProperty.Changed.AddClassHandler<Image>((image, e) =>
        {
            image.InvalidateVisual();
        });
    }

    public static int GetFrameIndex(Image element) => element.GetValue(FrameIndexProperty);
    public static void SetFrameIndex(Image element, int value) => element.SetValue(FrameIndexProperty, value);
}