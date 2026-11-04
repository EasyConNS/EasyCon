using EasyCon2.Avalonia.FileTree;
using System.Globalization;

namespace EasyCon2.Avalonia.Core.Tests;

[TestFixture]
public class FileTreeSortComparerTests
{
    [Test]
    public void NaturalNames_SortNumericRunsAndLeadingZeroesNaturally()
    {
        string[] names = ["脚本10.ecs", "脚本2.ecs", "脚本1.ecs", "脚本02.ecs"];

        Array.Sort(names, NaturalFileNameComparer.Instance);

        Assert.That(names, Is.EqualTo(new[] { "脚本1.ecs", "脚本2.ecs", "脚本02.ecs", "脚本10.ecs" }));
    }

    [Test]
    public void NaturalNames_HandleVeryLongNumbersWithoutOverflow()
    {
        string[] names = ["item999999999999999999999999999999999999", "item1000000000000000000000000000000000000"];

        Array.Sort(names, NaturalFileNameComparer.Instance);

        Assert.That(names[0], Is.EqualTo("item999999999999999999999999999999999999"));
    }

    [Test]
    public void NaturalNames_AreCultureIndependent()
    {
        CultureInfo original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
            int turkishResult = NaturalFileNameComparer.Instance.Compare("item2", "ITEM10");
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            int englishResult = NaturalFileNameComparer.Instance.Compare("item2", "ITEM10");

            Assert.That(turkishResult, Is.EqualTo(englishResult));
            Assert.That(turkishResult, Is.LessThan(0));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [TestCase(FileTreeSortMode.NameAscending, new[] { "folder2", "folder10", "a2.ecs", "a02.ecs", "a10.txt" })]
    [TestCase(FileTreeSortMode.NameDescending, new[] { "folder10", "folder2", "a10.txt", "a02.ecs", "a2.ecs" })]
    public void DirectoriesStayAheadOfFiles_WhenSortingByName(FileTreeSortMode mode, string[] expected)
    {
        FileTreeSortKey[] items =
        [
            Key("a10.txt", false),
            Key("folder10", true),
            Key("a2.ecs", false),
            Key("folder2", true),
            Key("a02.ecs", false)
        ];

        Array.Sort(items, new FileTreeSortComparer(mode));

        Assert.That(items.Select(item => item.Name), Is.EqualTo(expected));
    }

    [Test]
    public void ModifiedSorts_KeepUnknownTimesAtTheEndAndUseNameForTies()
    {
        DateTime early = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        DateTime late = early.AddDays(1);
        FileTreeSortKey[] items =
        [
            Key("unknown.ecs", false, null),
            Key("later.ecs", false, late),
            Key("same-b.ecs", false, early),
            Key("earlier.ecs", false, early),
            Key("latest.ecs", false, late)
        ];

        Array.Sort(items, new FileTreeSortComparer(FileTreeSortMode.ModifiedNewestFirst));
        Assert.That(items.Select(item => item.Name), Is.EqualTo(new[] { "later.ecs", "latest.ecs", "earlier.ecs", "same-b.ecs", "unknown.ecs" }));

        Array.Sort(items, new FileTreeSortComparer(FileTreeSortMode.ModifiedOldestFirst));
        Assert.That(items.Select(item => item.Name), Is.EqualTo(new[] { "earlier.ecs", "same-b.ecs", "later.ecs", "latest.ecs", "unknown.ecs" }));
    }

    [Test]
    public void ExtensionSorts_KeepExtensionlessFilesAtTheEndInBothDirections()
    {
        FileTreeSortKey[] items = [Key("z", false), Key("b.txt", false), Key("a.ECS", false), Key("a.ecs", false), Key("c.md", false)];

        Array.Sort(items, new FileTreeSortComparer(FileTreeSortMode.ExtensionAscending));
        Assert.That(items.Select(item => item.Name), Is.EqualTo(new[] { "a.ECS", "a.ecs", "c.md", "b.txt", "z" }));

        Array.Sort(items, new FileTreeSortComparer(FileTreeSortMode.ExtensionDescending));
        Assert.That(items.Select(item => item.Name), Is.EqualTo(new[] { "b.txt", "c.md", "a.ECS", "a.ecs", "z" }));
    }

    [Test]
    public void NameComparer_IsTransitiveForDeterministicMixedNames()
    {
        string[] names = ["2", "02", "10", "A", "a", "a 1", "脚本10", "脚本2", "x999999999999999999999999", "x1000000000000000000000000"];
        var comparer = NaturalFileNameComparer.Instance;

        foreach (string first in names)
            foreach (string second in names)
                foreach (string third in names)
                {
                    if (comparer.Compare(first, second) <= 0 && comparer.Compare(second, third) <= 0)
                        Assert.That(comparer.Compare(first, third), Is.LessThanOrEqualTo(0), $"{first} <= {second} <= {third}");
                }
    }

    [Test]
    public void SortModeSettings_MapSixStableStringsAndDefaultUnknownValues()
    {
        (FileTreeSortMode Mode, string Value)[] pairs =
        [
            (FileTreeSortMode.NameAscending, "name-asc"),
            (FileTreeSortMode.NameDescending, "name-desc"),
            (FileTreeSortMode.ModifiedNewestFirst, "modified-desc"),
            (FileTreeSortMode.ModifiedOldestFirst, "modified-asc"),
            (FileTreeSortMode.ExtensionAscending, "extension-asc"),
            (FileTreeSortMode.ExtensionDescending, "extension-desc")
        ];

        foreach (var (mode, value) in pairs)
        {
            Assert.That(FileTreeSortModeSettings.ToSettingValue(mode), Is.EqualTo(value));
            Assert.That(FileTreeSortModeSettings.FromSettingValue(value), Is.EqualTo(mode));
        }

        Assert.Multiple(() =>
        {
            Assert.That(FileTreeSortModeSettings.FromSettingValue(null), Is.EqualTo(FileTreeSortMode.NameAscending));
            Assert.That(FileTreeSortModeSettings.FromSettingValue(""), Is.EqualTo(FileTreeSortMode.NameAscending));
            Assert.That(FileTreeSortModeSettings.FromSettingValue("anything"), Is.EqualTo(FileTreeSortMode.NameAscending));
        });
    }

    private static FileTreeSortKey Key(string name, bool isDirectory, DateTime? lastWriteTimeUtc = null)
    {
        string extension = isDirectory ? string.Empty : Path.GetExtension(name);
        return new FileTreeSortKey(name, Path.Combine("root", name), isDirectory, extension, lastWriteTimeUtc);
    }
}