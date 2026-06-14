using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.Text;

namespace EasyCon2.Avalonia.Core.Terminal;

/// <summary>
/// 终端风格输出控件：支持 ANSI 颜色编码、虚拟化滚动、文本选择/复制、自动截断。
/// </summary>
public class TerminalControl : Control, ILogicalScrollable
{
    #region Styled Properties

    public static readonly StyledProperty<IList?> ItemsSourceProperty =
        StyledProperty<IList?>.Register<TerminalControl, IList?>(nameof(ItemsSource));

    public static readonly StyledProperty<double> FontSizeProperty =
        StyledProperty<double>.Register<TerminalControl, double>(nameof(FontSize), 13.0);

    public static readonly StyledProperty<FontFamily> FontFamilyProperty =
        StyledProperty<FontFamily>.Register<TerminalControl, FontFamily>(nameof(FontFamily),
            new FontFamily("Consolas,Courier New,monospace"));

    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        StyledProperty<IBrush?>.Register<TerminalControl, IBrush?>(nameof(Foreground),
            new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x38)));

    public static readonly StyledProperty<IBrush?> BackgroundProperty =
        StyledProperty<IBrush?>.Register<TerminalControl, IBrush?>(nameof(Background),
            new SolidColorBrush(Color.FromRgb(0xE0, 0xE1, 0xE6)));

    public static readonly StyledProperty<int> MaxLineCountProperty =
        StyledProperty<int>.Register<TerminalControl, int>(nameof(MaxLineCount), 10000);

    public static readonly StyledProperty<bool> AutoScrollProperty =
        StyledProperty<bool>.Register<TerminalControl, bool>(nameof(AutoScroll), true);

    public static readonly StyledProperty<IBrush> SelectionBrushProperty =
        StyledProperty<IBrush>.Register<TerminalControl, IBrush>(nameof(SelectionBrush),
            new SolidColorBrush(Color.FromArgb(0x60, 0x00, 0x78, 0xD4)));

    public static readonly StyledProperty<Thickness> ContentPaddingProperty =
        StyledProperty<Thickness>.Register<TerminalControl, Thickness>(nameof(ContentPadding), new Thickness(10));

    public static readonly StyledProperty<bool> MarqueeHorizontalOverflowProperty =
        StyledProperty<bool>.Register<TerminalControl, bool>(nameof(MarqueeHorizontalOverflow));

    #endregion

    #region Fields

    private readonly List<TerminalLine> _lines = new();
    private readonly DispatcherTimer _marqueeTimer;

    // Metrics
    private Typeface _typeface = default!;
    private Typeface _boldTypeface = default!;
    private double _lineHeight;
    private double _charWidth;
    private bool _metricsValid;

    // Scroll
    private Vector _offset;
    private Size _extent;
    private Size _viewport;
    private double _contentWidth;
    private double _marqueeLineWidth;
    private double _marqueeOffset;
    private bool _isAttached;
    private bool _isAtBottom = true;

    // Selection
    private record struct TextPos(int Line, int Col);
    private TextPos? _selAnchor;
    private TextPos? _selActive;
    private bool _isSelecting;

    #endregion

    #region CLR Properties

    public IList? ItemsSource
    {
        get => GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public double FontSize
    {
        get => GetValue(FontSizeProperty);
        set => SetValue(FontSizeProperty, value);
    }

    public FontFamily FontFamily
    {
        get => GetValue(FontFamilyProperty);
        set => SetValue(FontFamilyProperty, value);
    }

    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    public IBrush? Background
    {
        get => GetValue(BackgroundProperty);
        set => SetValue(BackgroundProperty, value);
    }

    public int MaxLineCount
    {
        get => GetValue(MaxLineCountProperty);
        set => SetValue(MaxLineCountProperty, value);
    }

    public bool AutoScroll
    {
        get => GetValue(AutoScrollProperty);
        set => SetValue(AutoScrollProperty, value);
    }

    public IBrush SelectionBrush
    {
        get => GetValue(SelectionBrushProperty);
        set => SetValue(SelectionBrushProperty, value);
    }

    public Thickness ContentPadding
    {
        get => GetValue(ContentPaddingProperty);
        set => SetValue(ContentPaddingProperty, value);
    }

    public bool MarqueeHorizontalOverflow
    {
        get => GetValue(MarqueeHorizontalOverflowProperty);
        set => SetValue(MarqueeHorizontalOverflowProperty, value);
    }

    #endregion

    #region Constructor

    static TerminalControl()
    {
        FocusableProperty.OverrideDefaultValue<TerminalControl>(true);
        ClipToBoundsProperty.OverrideDefaultValue<TerminalControl>(true);
    }

    public TerminalControl()
    {
        _marqueeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
        _marqueeTimer.Tick += OnMarqueeTick;
    }

    #endregion

    #region Overrides

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == ItemsSourceProperty)
            OnItemsSourceChanged(change);
        else if (change.Property == FontSizeProperty || change.Property == FontFamilyProperty)
        {
            _metricsValid = false;
            InvalidateVisual();
        }
        else if (change.Property == ContentPaddingProperty)
        {
            UpdateScroll();
        }
        else if (change.Property == BackgroundProperty ||
                 change.Property == ForegroundProperty ||
                 change.Property == SelectionBrushProperty)
        {
            InvalidateVisual();
        }
        else if (change.Property == MarqueeHorizontalOverflowProperty)
        {
            _marqueeOffset = 0;
            UpdateMarqueeTimer();
            InvalidateVisual();
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _isAttached = true;
        UpdateMarqueeTimer();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _isAttached = false;
        _marqueeTimer.Stop();
        base.OnDetachedFromVisualTree(e);
    }

    protected override Size MeasureOverride(Size availableSize) => availableSize;

    protected override Size ArrangeOverride(Size finalSize)
    {
        var result = base.ArrangeOverride(finalSize);
        var newViewport = new Size(finalSize.Width, finalSize.Height);
        if (_viewport != newViewport)
        {
            _viewport = newViewport;
            UpdateScroll();
        }
        return result;
    }

    public override void Render(DrawingContext context)
    {
        EnsureMetrics();

        // Background
        if (Background is { } bg)
            context.DrawRectangle(bg, null, new Rect(Bounds.Size));

        if (_lines.Count == 0) return;

        // Visible line range
        var pad = ContentPadding;
        var firstLine = Math.Max(0, (int)((_offset.Y - pad.Top) / _lineHeight));
        var lastLine = Math.Min(_lines.Count - 1,
            firstLine + (int)((_viewport.Height + pad.Top + pad.Bottom) / _lineHeight) + 1);

        var isMarqueeActive = IsMarqueeActive();

        // 1. Draw selection highlight (under text)
        if (!isMarqueeActive)
            DrawSelection(context, firstLine, lastLine);

        // 2. Draw text
        var defaultFg = Foreground ?? Brushes.Black;
        var fontSize = FontSize;
        for (var i = firstLine; i <= lastLine; i++)
        {
            var line = _lines[i];
            var y = pad.Top + i * _lineHeight - _offset.Y;
            var x = pad.Left - GetHorizontalRenderOffset();
            var lineWidth = MeasureLineWidth(line, fontSize, defaultFg);

            DrawLine(context, line, x, y, fontSize, defaultFg);

            if (isMarqueeActive && lineWidth > 0)
                DrawLine(context, line, x + GetMarqueeCycleWidth(), y, fontSize, defaultFg);
        }
    }

    private void DrawLine(DrawingContext context, TerminalLine line, double x, double y, double fontSize, IBrush defaultFg)
    {
        foreach (var seg in line.Segments)
        {
            if (string.IsNullOrEmpty(seg.Text)) continue;

            var fg = seg.Foreground != null
                ? new SolidColorBrush(seg.Foreground.Value)
                : defaultFg;

            var tf = seg.Bold ? _boldTypeface : _typeface;
            var ft = new FormattedText(
                seg.Text, CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, tf, fontSize, fg);

            if (seg.Background != null)
            {
                context.DrawRectangle(
                    new SolidColorBrush(seg.Background.Value), null,
                    new Rect(x, y, ft.Width, _lineHeight));
            }

            var textY = y + Math.Max(0, (_lineHeight - ft.Height) / 2);
            context.DrawText(ft, new Point(x, textY));

            if (seg.Underline)
            {
                var lineY = textY + ft.Height;
                context.DrawLine(new Pen(fg, 1),
                    new Point(x, lineY), new Point(x + ft.Width, lineY));
            }

            x += ft.Width;
        }
    }

    #endregion

    #region Selection & Clipboard

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        e.Handled = true;
        Focus(NavigationMethod.Pointer);

        var pos = HitTest(e.GetPosition(this));
        _selAnchor = pos;
        _selActive = pos;
        _isSelecting = true;
        InvalidateVisual();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_isSelecting) return;

        e.Handled = true;
        _selActive = HitTest(e.GetPosition(this));
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!_isSelecting) return;
        _isSelecting = false;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.C && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            var text = GetSelectedText();
            if (!string.IsNullOrEmpty(text))
                _ = CopyToClipboardAsync(text);
            e.Handled = true;
        }
    }

    private TextPos HitTest(Point pt)
    {
        var pad = ContentPadding;
        var line = (int)((pt.Y - pad.Top + _offset.Y) / _lineHeight);
        line = Math.Clamp(line, 0, Math.Max(0, _lines.Count - 1));
        var col = (int)((pt.X - pad.Left + _offset.X) / _charWidth);
        col = Math.Max(0, col);
        return new TextPos(line, col);
    }

    private string GetSelectedText()
    {
        if (_selAnchor == null || _selActive == null) return "";
        if (_selAnchor.Value == _selActive.Value) return "";

        var (start, end) = Normalize(_selAnchor.Value, _selActive.Value);
        var sb = new StringBuilder();

        for (var i = start.Line; i <= end.Line; i++)
        {
            if (i > start.Line) sb.Append('\n');
            var lineText = _lines[i].GetText();
            var s = i == start.Line ? Math.Min(start.Col, lineText.Length) : 0;
            var e = i == end.Line ? Math.Min(end.Col, lineText.Length) : lineText.Length;
            if (s < e && s < lineText.Length)
                sb.Append(lineText.AsSpan(s, e - s));
        }

        return sb.ToString();
    }

    private async System.Threading.Tasks.Task CopyToClipboardAsync(string text)
    {
        try
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard != null)
            {
                var transfer = new global::Avalonia.Input.DataTransfer();
                transfer.Add(global::Avalonia.Input.DataTransferItem.CreateText(text));
                await clipboard.SetDataAsync(transfer);
            }
        }
        catch
        {
            // Clipboard might not be available on all platforms
        }
    }

    private void DrawSelection(DrawingContext ctx, int firstLine, int lastLine)
    {
        if (_selAnchor == null || _selActive == null) return;
        if (_selAnchor.Value == _selActive.Value) return;

        var (start, end) = Normalize(_selAnchor.Value, _selActive.Value);
        var brush = SelectionBrush;

        for (var i = Math.Max(start.Line, firstLine); i <= Math.Min(end.Line, lastLine); i++)
        {
            var pad = ContentPadding;
            var y = pad.Top + i * _lineHeight - _offset.Y;
            var sCol = i == start.Line ? start.Col : 0;
            var eCol = i == end.Line ? Math.Min(end.Col, _lines[i].TextLength) : _lines[i].TextLength;

            var x = pad.Left + sCol * _charWidth - _offset.X;
            var w = (eCol - sCol) * _charWidth;
            if (w > 0)
                ctx.DrawRectangle(brush, null, new Rect(x, y, w, _lineHeight));
        }
    }

    private static (TextPos start, TextPos end) Normalize(TextPos a, TextPos b)
    {
        return a.Line < b.Line || (a.Line == b.Line && a.Col <= b.Col)
            ? (a, b)
            : (b, a);
    }

    #endregion

    #region Items Management

    private void OnItemsSourceChanged(AvaloniaPropertyChangedEventArgs change)
    {
        if (change.OldValue is INotifyCollectionChanged oldNcc)
            oldNcc.CollectionChanged -= OnCollectionChanged;

        if (change.NewValue is INotifyCollectionChanged newNcc)
            newNcc.CollectionChanged += OnCollectionChanged;

        Rebuild();
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add when e.NewItems != null:
                foreach (TerminalLine? line in e.NewItems)
                {
                    if (line != null) AddInternal(line);
                }
                break;

            case NotifyCollectionChangedAction.Reset:
                Rebuild();
                break;

            default:
                // Remove/Replace/Move — full rebuild (rare for terminal output)
                Rebuild();
                break;
        }
    }

    private void AddInternal(TerminalLine line)
    {
        _lines.Add(line);

        // Truncate old lines beyond MaxLineCount
        var max = MaxLineCount;
        if (_lines.Count > max)
            _lines.RemoveRange(0, _lines.Count - max);

        ScrollToBottomIfNeeded();
        UpdateScroll();
    }

    private void Rebuild()
    {
        _lines.Clear();

        if (ItemsSource != null)
        {
            foreach (TerminalLine? line in ItemsSource)
                if (line != null) _lines.Add(line);

            var max = MaxLineCount;
            if (_lines.Count > max)
                _lines.RemoveRange(0, _lines.Count - max);
        }

        ScrollToBottomIfNeeded();
        UpdateScroll();
    }

    private void ScrollToBottomIfNeeded()
    {
        if (!AutoScroll || !_isAtBottom) return;
        EnsureMetrics();

        var pad = ContentPadding;
        var maxY = Math.Max(0, _lines.Count * _lineHeight + pad.Top + pad.Bottom - _viewport.Height);
        _offset = new Vector(_offset.X, maxY);
    }

    #endregion

    #region Metrics

    private void EnsureMetrics()
    {
        if (_metricsValid) return;

        var ff = FontFamily;
        _typeface = new Typeface(ff);
        _boldTypeface = new Typeface(ff, FontStyle.Normal, FontWeight.Bold);

        var measure = new FormattedText(
            "M", CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            _typeface, FontSize, Brushes.Black);

        _lineHeight = measure.Height * 1.25; // line spacing
        _charWidth = measure.Width;
        _metricsValid = true;
    }

    #endregion

    #region ILogicalScrollable / IScrollable

    private EventHandler? _scrollInvalidated;
    private bool _canHorizontallyScroll;
    private bool _canVerticallyScroll;

    bool ILogicalScrollable.CanHorizontallyScroll
    {
        get => _canHorizontallyScroll;
        set => _canHorizontallyScroll = value;
    }

    bool ILogicalScrollable.CanVerticallyScroll
    {
        get => _canVerticallyScroll;
        set => _canVerticallyScroll = value;
    }

    // IScrollable also declares these (get-only); delegate to the same backing fields
    bool IScrollable.CanHorizontallyScroll => _canHorizontallyScroll;
    bool IScrollable.CanVerticallyScroll => _canVerticallyScroll;

    bool ILogicalScrollable.IsLogicalScrollEnabled => true;

    Size ILogicalScrollable.ScrollSize => new(_charWidth, _lineHeight);
    Size ILogicalScrollable.PageScrollSize => new(_viewport.Width, _viewport.Height);

    Size IScrollable.Extent => _extent;
    Size IScrollable.Viewport => _viewport;

    Vector IScrollable.Offset
    {
        get => _offset;
        set
        {
            if (_offset == value) return;
            _offset = value;

            // Detect if user scrolled back to bottom
            var maxY = _extent.Height - _viewport.Height;
            _isAtBottom = maxY <= 0 || _offset.Y >= maxY - _lineHeight;

            InvalidateVisual();
        }
    }

    event EventHandler? ILogicalScrollable.ScrollInvalidated
    {
        add => _scrollInvalidated += value;
        remove => _scrollInvalidated -= value;
    }

    void ILogicalScrollable.RaiseScrollInvalidated(EventArgs e)
    {
        _scrollInvalidated?.Invoke(this, e);
    }

    bool ILogicalScrollable.BringIntoView(Control target, Rect targetRect) => false;

    Control? ILogicalScrollable.GetControlInDirection(NavigationDirection direction, Control? from) => null;

    private void UpdateScroll()
    {
        EnsureMetrics();
        if (_viewport.Width <= 0 || _viewport.Height <= 0) return;

        var maxLineWidth = 0d;
        var defaultFg = Foreground ?? Brushes.Black;
        var fontSize = FontSize;
        foreach (var line in _lines)
        {
            var width = MeasureLineWidth(line, fontSize, defaultFg);
            if (width > maxLineWidth) maxLineWidth = width;
        }

        var pad = ContentPadding;
        _marqueeLineWidth = maxLineWidth;
        _contentWidth = maxLineWidth + pad.Left + pad.Right;
        _extent = new Size(
            Math.Max(_contentWidth, _viewport.Width),
            _lines.Count * _lineHeight + pad.Top + pad.Bottom);

        var marqueeCycleWidth = GetMarqueeCycleWidth();
        if (marqueeCycleWidth <= 0 || _marqueeOffset >= marqueeCycleWidth)
            _marqueeOffset = 0;

        if (_isAtBottom && AutoScroll)
            _offset = new Vector(_offset.X, Math.Max(0, _extent.Height - _viewport.Height));

        UpdateMarqueeTimer();
        _scrollInvalidated?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    private double GetHorizontalRenderOffset()
    {
        return IsMarqueeActive() ? _marqueeOffset : _offset.X;
    }

    private bool IsMarqueeActive()
    {
        return MarqueeHorizontalOverflow && _contentWidth > _viewport.Width;
    }

    private double GetMarqueeCycleWidth()
    {
        return _marqueeLineWidth + Math.Max(_charWidth * 4, 24);
    }

    private void UpdateMarqueeTimer()
    {
        if (_isAttached && IsMarqueeActive())
            _marqueeTimer.Start();
        else
            _marqueeTimer.Stop();
    }

    private void OnMarqueeTick(object? sender, EventArgs e)
    {
        if (!IsMarqueeActive())
        {
            if (_marqueeOffset != 0)
            {
                _marqueeOffset = 0;
                InvalidateVisual();
            }
            return;
        }

        var cycleWidth = GetMarqueeCycleWidth();
        _marqueeOffset = cycleWidth <= 0 ? 0 : (_marqueeOffset + 1) % cycleWidth;
        InvalidateVisual();
    }

    private double MeasureLineWidth(TerminalLine line, double fontSize, IBrush defaultFg)
    {
        var width = 0d;
        foreach (var seg in line.Segments)
        {
            if (string.IsNullOrEmpty(seg.Text)) continue;

            var fg = seg.Foreground != null
                ? new SolidColorBrush(seg.Foreground.Value)
                : defaultFg;
            var tf = seg.Bold ? _boldTypeface : _typeface;
            var ft = new FormattedText(
                seg.Text, CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, tf, fontSize, fg);
            width += ft.Width;
        }

        return width;
    }

    #endregion

    #region Public API

    /// <summary>清空所有行并重置滚动位置。</summary>
    public void Clear()
    {
        _lines.Clear();
        _selAnchor = null;
        _selActive = null;
        _isAtBottom = true;
        _offset = default;
        UpdateScroll();
    }

    /// <summary>获取当前选中文本。</summary>
    public string SelectedText => GetSelectedText();

    #endregion
}
