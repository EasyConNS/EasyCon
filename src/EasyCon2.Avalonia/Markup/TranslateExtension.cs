using Avalonia;
using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.MarkupExtensions;
using System;

namespace EasyCon2.Avalonia.Markup;

/// <summary>
/// axaml 中的本地化标记扩展：<c>{loc:Translate Text.Main.Run}</c>。
/// 经 DynamicResource 解析，切换语言（App.SetLocale）后自动刷新。
/// 键约定：Text.*，资源位于 Resources/Locales/{culture}.axaml。
/// </summary>
public class TranslateExtension : MarkupExtension
{
    public string Key { get; set; }

    public TranslateExtension(string key)
    {
        Key = key;
    }

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        return new DynamicResourceExtension(Key).ProvideValue(serviceProvider);
    }
}

/// <summary>
/// ViewModel/代码侧的本地化查表：L10n.T("Text.Main.Run")。
/// 查不到键时原样返回键名，便于发现遗漏。
/// 注意：字符串在赋值时快照，语言切换后状态类文本在下一次赋值时才更新。
/// </summary>
public static class L10n
{
    public static string T(string key)
    {
        // 必须走 TryGetResource：SetLocale 把语言字典合并进 MergedDictionaries，
        // 而普通索引器 Resources[key] 只查根条目、不遍历 MergedDictionaries，
        // 用它的话所有快照文本（状态栏/按钮）永远 miss、回显键名。
        var app = Application.Current;
        if (app != null
            && app.Resources.TryGetResource(key, app.RequestedThemeVariant, out var value)
            && value is string s)
            return s;
        return key;
    }
}