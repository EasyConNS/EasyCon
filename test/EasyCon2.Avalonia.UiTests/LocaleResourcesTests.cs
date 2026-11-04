using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using System;

namespace EasyCon2.Avalonia.UiTests;

/// <summary>
/// 语言字典守卫：两个 locale 资源必须能被 XAML 加载器解析（该文件曾出现过损坏/NUL 字节问题），
/// 且主窗口状态栏/工具栏引用的关键 Text.* 键在两种语言下都存在——
/// 缺键时 DynamicResource 静默显示为空，L10n.T 快照则会回显键名。
/// </summary>
[TestFixture]
public class LocaleResourcesTests
{
    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        // 与其他 fixture 相同的幂等初始化；XAML 资源加载依赖 Avalonia 资产定位器
        if (Application.Current == null)
            TestAppBuilder.BuildAvaloniaApp().SetupWithoutStarting();
    }

    private static ResourceDictionary LoadLocale(string name)
    {
        var dict = AvaloniaXamlLoader.Load(new Uri($"avares://EasyCon2.Avalonia/Resources/Locales/{name}.axaml")) as ResourceDictionary;
        Assert.That(dict, Is.Not.Null, $"{name}.axaml 应能加载为 ResourceDictionary");
        return dict!;
    }

    [Test]
    public void LocaleDictionariesParse_AndContainMainWindowKeys()
    {
        string[] spotKeys =
        {
            "Text.Status.UntitledScript",
            "Text.Status.NoScriptSelected",
            "Text.Status.NotConnected",
            "Text.Status.Connected",
            "Text.Btn.ConnectSwitch",
            "Text.Btn.DisconnectSwitch",
            "Text.Panel.Capture",
            "Text.Btn.StartMapping",
            "Text.Btn.MonitorHide",
            "Text.Btn.Run",
            "Text.WorkTree.Sort",
            "Text.WorkTree.Sort.NameAscending",
            "Text.WorkTree.Sort.NameDescending",
            "Text.WorkTree.Sort.ModifiedNewest",
            "Text.WorkTree.Sort.ModifiedOldest",
            "Text.WorkTree.Sort.ExtensionAscending",
            "Text.WorkTree.Sort.ExtensionDescending",
            "Text.Btn.SaveScript",
            "Text.Btn.SaveScriptAs",
        };

        foreach (var locale in new[] { "zh_CN", "en_US" })
        {
            var dict = LoadLocale(locale);
            Assert.That(dict.Count, Is.GreaterThan(100), $"{locale}.axaml 应包含完整的键值表");

            foreach (var key in spotKeys)
            {
                var found = dict.TryGetValue(key, out var value);
                Assert.That(found, Is.True, $"{locale}.axaml 缺少键 {key}");
                Assert.That(value as string, Is.Not.Empty, $"{locale} 的 {key} 值不应为空");
            }
        }
    }
}