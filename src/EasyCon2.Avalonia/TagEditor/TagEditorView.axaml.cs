using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using EasyCon2.Avalonia.Core.TagEditor;
using System;
using System.IO;

namespace EasyCon2.Avalonia.Core.TagEditor;

public partial class TagEditorView : UserControl
{
    // VM 只交付 PNG 字节与 int 坐标；显示位图在本视图解码并持有，替换时释放旧图，
    // SelectableImage 的 Rect 属性由这里与 VM 坐标互相同步
    private TagEditorViewModel? _boundVm;
    private Bitmap? _sourceBitmap;
    private Bitmap? _targetBitmap;
    private Bitmap? _rangePreviewBitmap;

    public TagEditorView()
    {
        InitializeComponent();
        ImageControl.PropertyChanged += OnImageControlPropertyChanged;
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (_boundVm != null)
            _boundVm.PropertyChanged -= OnViewModelPropertyChanged;
        ClearRenderedImages();

        if (DataContext is not TagEditorViewModel vm)
        {
            _boundVm = null;
            return;
        }

        _boundVm = vm;

        // 初始同步：VM 状态可能先于视图装载完成（如从标签文件加载）
        _sourceBitmap = DecodePng(vm.SourcePng);
        _targetBitmap = DecodePng(vm.TargetPng);
        _rangePreviewBitmap = DecodePng(vm.RangePreviewPng);
        ImageControl.Source = _sourceBitmap;
        TargetPreviewImage.Source = _targetBitmap;
        RangePreviewImage.Source = _rangePreviewBitmap;
        PushRectsToImageControl(vm);

        vm.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (sender is not TagEditorViewModel vm)
            return;

        switch (e.PropertyName)
        {
            case nameof(TagEditorViewModel.SourcePng):
                SwapImage(ref _sourceBitmap, DecodePng(vm.SourcePng));
                ImageControl.Source = _sourceBitmap;
                break;
            case nameof(TagEditorViewModel.TargetPng):
                SwapImage(ref _targetBitmap, DecodePng(vm.TargetPng));
                TargetPreviewImage.Source = _targetBitmap;
                break;
            case nameof(TagEditorViewModel.RangePreviewPng):
                SwapImage(ref _rangePreviewBitmap, DecodePng(vm.RangePreviewPng));
                RangePreviewImage.Source = _rangePreviewBitmap;
                break;
            case nameof(TagEditorViewModel.RangeX):
            case nameof(TagEditorViewModel.RangeY):
            case nameof(TagEditorViewModel.RangeWidth):
            case nameof(TagEditorViewModel.RangeHeight):
            case nameof(TagEditorViewModel.TargetX):
            case nameof(TagEditorViewModel.TargetY):
            case nameof(TagEditorViewModel.TargetWidth):
            case nameof(TagEditorViewModel.TargetHeight):
                PushRectsToImageControl(vm);
                break;
        }
    }

    // 用户圈选完成（SelectableImage 写入 Rect）→ 回写 VM 坐标
    private void OnImageControlPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (DataContext is not TagEditorViewModel vm)
            return;

        if (e.Property == SelectableImage.RangeRectProperty)
            ApplySelectionToCoordinates(vm, e.GetNewValue<Rect>(), isRange: true);
        else if (e.Property == SelectableImage.TargetRectProperty)
            ApplySelectionToCoordinates(vm, e.GetNewValue<Rect>(), isRange: false);
    }

    private static void ApplySelectionToCoordinates(TagEditorViewModel vm, Rect rect, bool isRange)
    {
        // 与净化前 VM 侧 OnXxxRectChanged 相同的有效性门槛：零面积圈选不落值
        if (rect.Width <= 0 || rect.Height <= 0)
            return;

        if (isRange)
        {
            vm.RangeX = (int)rect.X;
            vm.RangeY = (int)rect.Y;
            vm.RangeWidth = (int)rect.Width;
            vm.RangeHeight = (int)rect.Height;
        }
        else
        {
            vm.TargetX = (int)rect.X;
            vm.TargetY = (int)rect.Y;
            vm.TargetWidth = (int)rect.Width;
            vm.TargetHeight = (int)rect.Height;
        }
    }

    private void PushRectsToImageControl(TagEditorViewModel vm)
    {
        ImageControl.RangeRect = new Rect(vm.RangeX, vm.RangeY, vm.RangeWidth, vm.RangeHeight);
        ImageControl.TargetRect = new Rect(vm.TargetX, vm.TargetY, vm.TargetWidth, vm.TargetHeight);
    }

    private void ClearRenderedImages()
    {
        // 先解除控件引用再释放位图，避免渲染线程访问已释放内存
        ImageControl.Source = null;
        TargetPreviewImage.Source = null;
        RangePreviewImage.Source = null;
        SwapImage(ref _sourceBitmap, null);
        SwapImage(ref _targetBitmap, null);
        SwapImage(ref _rangePreviewBitmap, null);
    }

    private static void SwapImage(ref Bitmap? field, Bitmap? next)
    {
        var old = field;
        field = next;
        old?.Dispose();
    }

    private static Bitmap? DecodePng(byte[]? bytes)
    {
        if (bytes == null || bytes.Length == 0)
            return null;
        try
        {
            return new Bitmap(new MemoryStream(bytes));
        }
        catch
        {
            return null;
        }
    }
}