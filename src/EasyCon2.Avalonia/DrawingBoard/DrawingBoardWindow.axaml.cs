using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace EasyCon2.Avalonia.DrawingBoard;

public partial class DrawingBoardWindow : Window
{
    public DrawingBoardWindow()
    {
        InitializeComponent();
        // 挂在带透明背景的外框上：未加载图片时 Image 本身不参与命中测试
        PictureFrame.PointerPressed += OnPicturePointerPressed;
    }

    // 指针坐标属于视觉树信息，只在此换算成整数像素后交给 ViewModel
    private void OnPicturePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        PointerPoint point = e.GetCurrentPoint(PictureBox);
        if (!point.Properties.IsLeftButtonPressed || DataContext is not DrawingBoardViewModel vm)
            return;
        Point position = point.Position;
        vm.OnPicturePressed((int)position.X, (int)position.Y);
    }
}