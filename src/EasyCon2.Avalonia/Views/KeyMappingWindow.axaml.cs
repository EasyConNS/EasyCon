using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using EasyCon2.Avalonia.Controls;
using EasyCon2.Avalonia.ViewModels;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;
using AvaloniaSvg = Avalonia.Svg.Skia.Svg;

namespace EasyCon2.Avalonia.Views;

public partial class KeyMappingWindow : Window
{
    private KeyMappingViewModel? _vm;

    public KeyMappingWindow()
    {
        InitializeComponent();
        // 手柄 SVG 复用启动期预热的共享 SvgSource，避免首次打开时解析延迟
        ControllerSvg.SvgSource = ControllerSvgCache.Instance;
        // 使用 Tunnel 策略：在子控件（Button）处理按键之前先截获
        AddHandler(InputElement.KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Tunnel);
        ControllerSvg.PointerPressed += OnControllerPointerPressed;
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (_vm != null)
        {
            _vm.RequestClose -= OnRequestClose;
            _vm.PropertyChanged -= OnViewModelPropertyChanged;
            foreach (KeyMappingRow row in _vm.Rows)
                row.PropertyChanged -= OnRowPropertyChanged;
        }

        _vm = DataContext as KeyMappingViewModel;
        if (_vm != null)
        {
            _vm.RequestClose += OnRequestClose;
            _vm.PropertyChanged += OnViewModelPropertyChanged;
            foreach (KeyMappingRow row in _vm.Rows)
                row.PropertyChanged += OnRowPropertyChanged;
        }

        RebuildControllerCss();
    }

    private void OnRequestClose() => Close();

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(KeyMappingViewModel.HighlightedControllerId))
            RebuildControllerCss();
    }

    private void OnRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(KeyMappingRow.IsUnmapped) or nameof(KeyMappingRow.IsHighlighted))
            RebuildControllerCss();
    }

    /// <summary>
    /// 重建注入手柄 SVG 的 CSS：
    /// ① 未绑定的按键分组用「未映射」色灰化；② 当前高亮分组用高亮色。
    /// 颜色资源查找（Application.Current.Resources）只允许出现在 code-behind。
    /// </summary>
    private void RebuildControllerCss()
    {
        if (_vm == null)
        {
            AvaloniaSvg.SetCss(ControllerSvg, "");
            return;
        }

        Color unmapped = GetResourceColor("MappingUnmappedColor", Colors.Gray);
        Color highlight = GetResourceColor("MappingHighlightColor", Colors.White);

        StringBuilder css = new();
        HashSet<string> dimmed = new(StringComparer.Ordinal);

        // ① 未绑定 → 灰化（同一分组只写一次）
        foreach (KeyMappingRow row in _vm.Rows)
        {
            if (!row.IsUnmapped || string.IsNullOrEmpty(row.ControllerId))
                continue;

            if (dimmed.Add(row.ControllerId))
                css.Append('#').Append(row.ControllerId).Append(" { color: #").Append(Hex(unmapped)).Append("; } ");
        }

        // ② 高亮规则写在最后，优先级更高
        if (!string.IsNullOrEmpty(_vm.HighlightedControllerId))
            css.Append('#').Append(_vm.HighlightedControllerId).Append(" { color: #").Append(Hex(highlight)).Append("; } ");

        AvaloniaSvg.SetCss(ControllerSvg, css.ToString());
    }

    private static string Hex(Color color)
    {
        return $"{color.R:X2}{color.G:X2}{color.B:X2}";
    }

    private static Color GetResourceColor(string key, Color fallback)
    {
        if (Application.Current is { } application &&
            application.TryGetResource(key, null, out object? value) &&
            value is Color color)
        {
            return color;
        }

        return fallback;
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        _vm?.OnKeyDown(e.Key);
        e.Handled = true;
    }

    /// <summary>
    /// 点击手柄图 —— 用 Svg 控件的命中测试拿到按键分组 id（如 face-a / stick-l / dpad-up），
    /// 交给 ViewModel 找到对应行并进入监听。
    /// </summary>
    private void OnControllerPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_vm == null)
            return;

        Point point = e.GetPosition(ControllerSvg);
        foreach (Svg.SvgElement element in ControllerSvg.HitTestElements(point))
        {
            if (string.IsNullOrEmpty(element.ID))
                continue;

            _vm.StartListeningForController(element.ID);
            break;
        }

        e.Handled = true;
    }
}