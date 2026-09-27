using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using EasyCon2.Avalonia.Markup;
using System;

namespace EasyCon2.Avalonia.UiTests;

/// <summary>
/// L10n.T 查找路径回归：语言字典经 SetLocale 合并进 Application.Resources.MergedDictionaries，
/// 而 ResourceDictionary 的普通索引器只查根条目、不遍历 MergedDictionaries（Avalonia 12 行为），
/// 所以 L10n.T 必须走 TryGetResource（与 DynamicResource 同一条查找链），
/// 否则状态栏/按钮的文本快照永远 miss、回显键名。
/// </summary>
[TestFixture]
public class L10nResolutionTests
{
    private ResourceDictionary? _merged;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        if (Application.Current == null)
            TestAppBuilder.BuildAvaloniaApp().SetupWithoutStarting();
    }

    [TearDown]
    public void TearDown()
    {
        if (_merged != null && Application.Current != null)
            Application.Current.Resources.MergedDictionaries.Remove(_merged);
        _merged = null;
    }

    [Test]
    public void T_ResolvesKeysMergedIntoDictionaries()
    {
        ResourceDictionary zh = LoadLocale("zh_CN");
        IResourceDictionary resources = Application.Current!.Resources;
        resources.MergedDictionaries.Add(zh);
        _merged = zh;

        string[] spotKeys = { "Text.Btn.Run", "Text.Status.UntitledScript", "Text.Btn.MonitorHide" };
        foreach (var key in spotKeys)
        {
            var value = L10n.T(key);
            Assert.That(value, Is.Not.Empty, $"{key} 应解析为非空文本");
            Assert.That(value, Is.Not.EqualTo(key), $"{key} 不应回显键名（索引器不遍历 MergedDictionaries）");
        }
    }

    [Test]
    public void T_SwappingMergedDictionarySwitchesLanguage()
    {
        ResourceDictionary zh = LoadLocale("zh_CN");
        ResourceDictionary en = LoadLocale("en_US");
        IResourceDictionary resources = Application.Current!.Resources;

        resources.MergedDictionaries.Add(zh);
        _merged = zh;
        var zhValue = L10n.T("Text.Btn.Run");

        resources.MergedDictionaries.Remove(zh);
        resources.MergedDictionaries.Add(en);
        _merged = en;
        var enValue = L10n.T("Text.Btn.Run");

        Assert.Multiple(() =>
        {
            Assert.That(enValue, Is.Not.Empty);
            Assert.That(enValue, Is.Not.EqualTo("Text.Btn.Run"));
            Assert.That(enValue, Is.Not.EqualTo(zhValue), "切换合并字典后 L10n.T 应返回另一语言的文本");
        });
    }

    [Test]
    public void T_ReturnsKeyItself_WhenMissing()
    {
        Assert.That(L10n.T("Text.DoesNotExist.X"), Is.EqualTo("Text.DoesNotExist.X"));
    }

    private static ResourceDictionary LoadLocale(string name)
    {
        var dict = AvaloniaXamlLoader.Load(new Uri($"avares://EasyCon2.Avalonia/Resources/Locales/{name}.axaml")) as ResourceDictionary;
        Assert.That(dict, Is.Not.Null, $"{name}.axaml 应能加载为 ResourceDictionary");
        return dict!;
    }
}