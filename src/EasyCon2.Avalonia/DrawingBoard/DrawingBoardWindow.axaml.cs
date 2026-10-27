using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using EasyCon2.Avalonia.Services;
using ILogService = EasyCon.Core.Services.ILogService;

namespace EasyCon2.Avalonia.DrawingBoard;

public partial class DrawingBoardWindow : Window
{
    /// <summary>窗口最小高度至少为 axaml 中的 MinHeight，且要给预览留出这么高。</summary>
    private const double PreviewMinHeight = 80;

    private double _baseMinHeight;

    /// <summary>
    /// 组装画板窗口。对话框（提示框、文件选择）使用以本窗口为 Owner 的独立 <see cref="DialogService"/>：
    /// 共用主窗口的实例时，对话框归属主窗口，可能被画板窗口挡住。
    /// </summary>
    public static DrawingBoardWindow Create(IDrawingRunner runner, IImageProcessor imageProcessor, IUiDispatcher ui, ILogService logService)
    {
        DialogService dialogs = new();
        DrawingBoardViewModel board = new(runner, dialogs, imageProcessor, ui, logService);
        DrawingBoardWindow window = new() { DataContext = board };
        dialogs.Owner = window;
        window.Closed += (_, _) => board.Dispose();
        window.Activated += (_, _) => board.RefreshLocalizedTexts();
        return window;
    }

    public DrawingBoardWindow()
    {
        InitializeComponent();
        // 挂在带透明背景的外框上：未加载图片时 Image 本身不参与命中测试
        PictureFrame.PointerPressed += OnPicturePointerPressed;
        PictureFrame.PointerMoved += OnPicturePointerMoved;
        PictureFrame.PointerReleased += OnPicturePointerReleased;
        Opened += (_, _) => FitToScreen();
        _baseMinHeight = MinHeight;
        ControlsPanel.SizeChanged += (_, _) => FitMinHeightToControls();
    }

    // 布局：默认尺寸在小屏上可能超出可用区域，打开时收进屏幕工作区
    private void FitToScreen()
    {
        if (Screens.ScreenFromWindow(this) is not { } screen)
            return;
        double maxWidth = screen.WorkingArea.Width / screen.Scaling;
        double maxHeight = screen.WorkingArea.Height / screen.Scaling;
        if (Width > maxWidth)
            Width = Math.Max(MinWidth, maxWidth * 0.95);
        if (Height > maxHeight)
            Height = Math.Max(MinHeight, maxHeight * 0.95);
    }

    // 布局：预览下方的控件只占自身高度、不能被压缩（展开"图片调整"、勾选橡皮擦说明、窗口变窄换行都会让它变高），
    // 所以窗口最小高度跟着它走，避免底部控件被挤出窗口；必要时窗口随之增高
    private void FitMinHeightToControls()
    {
        Thickness margin = ((Grid)Content!).Margin;
        double needed = ControlsPanel.Bounds.Height + margin.Top + margin.Bottom + ((Grid)Content).RowSpacing + PreviewMinHeight;
        MinHeight = Math.Max(_baseMinHeight, Math.Ceiling(needed));
    }

    // 指针坐标与控件尺寸属于视觉树信息，只在此取出后交给 ViewModel 换算。
    // 用声明的逻辑尺寸：未载入图片时 Image 排布为 0×0，但外框仍按该尺寸占位；Viewbox 缩放不影响自身坐标系
    private void OnPicturePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        PointerPoint point = e.GetCurrentPoint(PictureBox);
        if (!point.Properties.IsLeftButtonPressed || DataContext is not DrawingBoardViewModel vm)
            return;
        // 捕获指针：拖出预览框外也能继续框选
        e.Pointer.Capture(PictureFrame);
        vm.OnPicturePressed(point.Position.X, point.Position.Y, PictureBox.Width, PictureBox.Height);
    }

    private void OnPicturePointerMoved(object? sender, PointerEventArgs e)
    {
        PointerPoint point = e.GetCurrentPoint(PictureBox);
        if (!point.Properties.IsLeftButtonPressed || DataContext is not DrawingBoardViewModel vm)
            return;
        vm.OnPictureDragged(point.Position.X, point.Position.Y, PictureBox.Width, PictureBox.Height);
    }

    private void OnPicturePointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        e.Pointer.Capture(null);
        (DataContext as DrawingBoardViewModel)?.OnPictureReleased();
    }
}