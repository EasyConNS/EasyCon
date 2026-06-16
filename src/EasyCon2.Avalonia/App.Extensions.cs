using Avalonia;
using Avalonia.Controls;

namespace EasyCon2.Avalonia;

public partial class App
{
    // 当前已激活的语言资源字典；切换语言时先从 MergedDictionaries 移除它。
    private ResourceDictionary? _activeLocale;

    /// <summary>
    /// 激活指定语言的资源字典：把目标语言合并进 <see cref="ResourceDictionary.MergedDictionaries"/>，
    /// 并移除上一次激活的，使 <c>{DynamicResource Text.*}</c> 解析到新语言。
    /// 参考 sourcegit App.SetLocale。当前未接入切换 UI，仅用于启动时设置默认语言。
    /// </summary>
    public static void SetLocale(string localeKey)
    {
        if (Application.Current is not App app)
            return;

        if (app.Resources[localeKey] is not ResourceDictionary target || target == app._activeLocale)
            return;

        if (app._activeLocale != null)
            app.Resources.MergedDictionaries.Remove(app._activeLocale);

        app.Resources.MergedDictionaries.Add(target);
        app._activeLocale = target;
    }
}
