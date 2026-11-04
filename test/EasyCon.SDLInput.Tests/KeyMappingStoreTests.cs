using EasyCon.Core.Config;

namespace EasyCon.SDLInput.Tests;

/// <summary>校验内存缓存的加载/保存/重置语义（纯内存，不触碰磁盘）。</summary>
[TestFixture]
public class KeyMappingStoreTests
{
    [Test]
    public void Current_LoadsOnce_ThenServesFromCache()
    {
        int loadCount = 0;
        KeyMappingConfig config = new() { SchemaVersion = KeyMappingConfig.CurrentSchemaVersion };
        KeyMappingStore store = new(() =>
        {
            loadCount++;
            return config;
        }, _ => { });

        KeyMappingConfig first = store.Current;
        KeyMappingConfig second = store.Current;

        Assert.Multiple(() =>
        {
            Assert.That(loadCount, Is.EqualTo(1));
            Assert.That(first, Is.SameAs(second));
        });
    }

    [Test]
    public void Save_RefreshesCacheAndInvokesSaver()
    {
        KeyMappingConfig initial = new() { SchemaVersion = KeyMappingConfig.CurrentSchemaVersion };
        KeyMappingConfig updated = new() { SchemaVersion = KeyMappingConfig.CurrentSchemaVersion, A = 15 };
        KeyMappingConfig? saved = null;
        KeyMappingStore store = new(() => initial, m => saved = m);

        store.Save(updated);

        Assert.Multiple(() =>
        {
            Assert.That(store.Current, Is.SameAs(updated));
            Assert.That(saved, Is.SameAs(updated));
        });
    }

    [Test]
    public void Save_RefreshesCacheBeforeSaverRuns()
    {
        KeyMappingConfig initial = new() { SchemaVersion = KeyMappingConfig.CurrentSchemaVersion };
        KeyMappingConfig updated = new() { SchemaVersion = KeyMappingConfig.CurrentSchemaVersion, B = 15 };
        KeyMappingStore? store = null;
        KeyMappingConfig? observedInSaver = null;
        store = new KeyMappingStore(() => initial, _ => observedInSaver = store!.Current);

        store.Save(updated);

        Assert.That(observedInSaver, Is.SameAs(updated));
    }

    [Test]
    public void Reset_ForcesReload()
    {
        int loadCount = 0;
        KeyMappingStore store = new(() =>
        {
            loadCount++;
            return new KeyMappingConfig { SchemaVersion = KeyMappingConfig.CurrentSchemaVersion };
        }, _ => { });

        KeyMappingConfig first = store.Current;
        store.Reset();
        KeyMappingConfig second = store.Current;

        Assert.Multiple(() =>
        {
            Assert.That(loadCount, Is.EqualTo(2));
            Assert.That(second, Is.Not.SameAs(first));
        });
    }
}